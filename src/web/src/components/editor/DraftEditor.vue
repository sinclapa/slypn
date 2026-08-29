<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import RichTextEditor from '@/components/editor/RichTextEditor.vue'
import FieldCounter from '@/components/common/FieldCounter.vue'
import ClearFieldButton from '@/components/common/ClearFieldButton.vue'
import SaveIndicator from '@/components/editor/SaveIndicator.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { useBeforeUnload } from '@/composables/useBeforeUnload'
import { apiErrorMessage, apiFetch } from '@/lib/api'
import { EMPTY_DRAFT, htmlToText, type DraftPayload, type DraftSummary } from '@/lib/draft'

const props = defineProps<{
  draftId: string
  /** Read-only view of an in-review submission — no fetch, no autosave, no edits. */
  readonly?: boolean
  /** Content to show in read-only mode (the in-review article/blog payload). */
  initialContent?: DraftPayload | null
}>()
const emit = defineEmits<{
  (e: 'close'): void
  (e: 'submitted', id: string): void
  (e: 'saved', summary: DraftSummary): void
}>()

const draft       = ref<DraftPayload>({ ...EMPTY_DRAFT })

// A revision draft may not change type: the article it replaces is already published under a
// type-specific URL. Enforced by the API on save and again on submit; this keeps the control
// from offering a choice that would be refused.
const typeLocked  = computed(() => !!draft.value.replacesArticleId)
const currentEtag = ref<string | null>(null)
const switching   = ref(false)
const uploadError = ref<string | null>(null)
const submitting  = ref(false)
const submitError = ref<string | null>(null)
// Not every refusal is a failure. Submitting a revision that matches what is already
// published comes back 409 — there is nothing to review, which is worth saying kindly
// rather than in red. 409 is only reachable from that guard on this endpoint.
const submitNotice = ref<string | null>(null)

interface ConflictState { serverDraft: DraftPayload; serverEtag: string | null }
const conflict = ref<ConflictState | null>(null)

// Snapshot of the content as loaded, so callers can tell whether the user
// actually changed anything (e.g. to discard an untouched revision draft).
// Field caps, mirroring DraftInput/ArticleInput server-side. Kept here so the counters
// and the maxlength attributes cannot drift from each other.
const LIMITS = { title: 200, summary: 500, category: 60, body: 50_000 } as const

const loadedSnapshot = ref('')
function snapshot(d: DraftPayload): string {
  return JSON.stringify({
    type: d.type, title: d.title, slug: d.slug,
    summary: d.summary, body: d.body, category: d.category,
  })
}
function isDirty(): boolean {
  return snapshot(draft.value) !== loadedSnapshot.value
}

async function loadDraft(id: string) {
  // Read-only mode shows a passed-in in-review payload; there's no draft to fetch.
  if (props.readonly) {
    draft.value = { ...EMPTY_DRAFT, ...props.initialContent }
    currentEtag.value = null
    conflict.value = null
    submitError.value = null
    submitNotice.value = null
    loadedSnapshot.value = snapshot(draft.value)
    return
  }
  switching.value = true
  conflict.value  = null
  submitError.value = null
  submitNotice.value = null
  try {
    const resp = await apiFetch(`/drafts/${id}`)
    if (!resp.ok) { draft.value = { ...EMPTY_DRAFT }; currentEtag.value = null; return }
    draft.value = { ...EMPTY_DRAFT, ...await resp.json() as DraftPayload }
    currentEtag.value = resp.headers.get('ETag')
  } catch {
    draft.value = { ...EMPTY_DRAFT }
    currentEtag.value = null
  } finally {
    switching.value = false
    loadedSnapshot.value = snapshot(draft.value)
  }
}

/// `force` writes even when nothing changed. Submitting uses it: the PUT is also the ETag
/// check, and it is how a draft edited in another tab surfaces as a 412 rather than being
/// submitted over silently. The autosave and the flush-before-close have no such need.
async function save(value: DraftPayload, { force = false } = {}) {
  if (props.readonly || switching.value || !value.title.trim()) return

  // Nothing to write. The autosave watcher fires on any change to the draft object, which is
  // not the same as the content differing from what is stored: loadDraft replaces the object
  // wholesale, so opening a second draft used to PUT the freshly loaded content straight back
  // a second and a half later, and a keystroke undone inside one debounce window wrote a copy
  // of what was already there.
  //
  // Compared against the last thing loaded *or written*, not just loaded — the snapshot moves
  // on every successful save below. Without that, typing, saving, then undoing would look
  // clean and quietly leave the server holding the edit the author had just taken back.
  if (!force && snapshot(value) === loadedSnapshot.value) return

  // A backstop: the editor refuses input at the limit, so this should only be reachable
  // for content that arrived over it (an older draft, or a write through the API). Caught
  // here rather than letting the API answer, so the author gets a sentence they can act on
  // instead of "The field Body must be a string with a maximum length of 50000".
  const bodyChars = htmlToText(value.body).length
  if (bodyChars > LIMITS.body) {
    throw new Error(
      `This piece is too long to save — ${bodyChars.toLocaleString()} characters, against a `
      + `limit of ${LIMITS.body.toLocaleString()}. Shorten it, or split it into two pieces.`,
    )
  }

  const headers: Record<string, string> = {}
  if (currentEtag.value) headers['If-Match'] = currentEtag.value

  const resp = await apiFetch(`/drafts/${props.draftId}`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(value),
  })

  if (resp.status === 412) {
    const reload = await apiFetch(`/drafts/${props.draftId}`)
    conflict.value = {
      serverDraft: reload.ok ? await reload.json() as DraftPayload : { ...EMPTY_DRAFT },
      serverEtag:  reload.ok ? reload.headers.get('ETag') : null,
    }
    throw new Error('412 — another tab updated this draft.')
  }
  if (!resp.ok) throw new Error(await apiErrorMessage(resp))
  currentEtag.value = resp.headers.get('ETag') ?? currentEtag.value
  // What the server now holds. Taken from the payload that was sent rather than draft.value,
  // which may have moved on during the request — if it did, the watcher has already scheduled
  // the next save, and it will compare against this and find itself dirty.
  loadedSnapshot.value = snapshot(value)
  emit('saved', {
    id: props.draftId,
    title: value.title,
    type: value.type,
    updatedAt: new Date().toISOString(),
    _etag: currentEtag.value ?? undefined,
  })
}

/** Force an immediate save of the current draft (used before switching/closing). */
async function flush() {
  try { await save(draft.value) } catch { /* best-effort */ }
}
defineExpose({ flush, isDirty })

async function submitForReview() {
  if (submitting.value) return
  submitError.value = null
  submitNotice.value = null
  submitting.value  = true
  try {
    await save(draft.value, { force: true })
    const resp = await apiFetch(`/drafts/${props.draftId}/submit`, { method: 'POST' })
    if (resp.status === 409) {
      submitNotice.value = await resp.text()
      return
    }
    if (!resp.ok) throw new Error(await apiErrorMessage(resp))
    emit('submitted', props.draftId)
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}

// ── Conflict resolution ─────────────────────────────────────────────────────
function resolveByDiscardingLocal() {
  if (!conflict.value) return
  draft.value = { ...EMPTY_DRAFT, ...conflict.value.serverDraft }
  currentEtag.value = conflict.value.serverEtag
  conflict.value = null
}

async function resolveByForcingLocal() {
  if (!conflict.value) return
  currentEtag.value = null
  const local = draft.value
  conflict.value = null
  try { await save(local) } catch (err) { submitError.value = err instanceof Error ? err.message : String(err) }
}

// ── Category autocomplete (type-scoped, free text) ───────────────────────────
const articleCategories = ref<string[]>([])
const blogCategories    = ref<string[]>([])

function distinctCategories(items: { category?: string }[]): string[] {
  const unique = new Set<string>()
  for (const item of items) {
    const category = (item.category ?? '').trim()
    if (category) unique.add(category)
  }
  return [...unique].sort((a, b) => a.localeCompare(b))
}

async function loadCategoryHints() {
  try {
    const [articlesResp, blogResp] = await Promise.all([
      apiFetch('/articles?status=published'),
      apiFetch('/blog?status=published'),
    ])
    if (!articlesResp.ok || !blogResp.ok) return
    const [articles, blog] = await Promise.all([
      articlesResp.json() as Promise<{ category?: string }[]>,
      blogResp.json()     as Promise<{ category?: string }[]>,
    ])
    articleCategories.value = distinctCategories(articles)
    blogCategories.value    = distinctCategories(blog)
  } catch {
    // hints are optional — ignore
  }
}

const categoryHints = computed(() =>
  draft.value.type === 'blog' ? blogCategories.value : articleCategories.value)

// The rich-text editor emits "<p></p>" when empty, so a non-empty HTML string
// isn't enough — require real text or an image before allowing submit.
function hasBodyContent(html: string): boolean {
  if (/<img\b/i.test(html)) return true
  return htmlToText(html).trim().length > 0
}

// Counted as an author reads it, not as it is stored: markup, link hrefs and image
// URLs do not show on the page, so charging for them would make the number meaningless.
// The editor blocks input against this same figure, so the count and the stop agree.
const bodyTextLength = computed(() => htmlToText(draft.value.body).length)

const missingToSubmit = computed(() => {
  const missing: string[] = []
  if (!draft.value.title.trim())         missing.push('title')
  if (!draft.value.summary.trim())       missing.push('summary')
  if (!hasBodyContent(draft.value.body)) missing.push('content')
  return missing
})
const canSubmit = computed(() => missingToSubmit.value.length === 0)

// ── Init ─────────────────────────────────────────────────────────────────────
onMounted(async () => {
  await loadDraft(props.draftId)
  loadCategoryHints()
})
watch(() => [props.draftId, props.readonly, props.initialContent], () => loadDraft(props.draftId))

const { status, lastSavedAt, errorMessage } = useAutoSave(draft, save, { debounce: 1500 })

useBeforeUnload(computed(() =>
  status.value === 'pending' || status.value === 'saving' || status.value === 'error',
))

watch(() => draft.value.body, (html) => {
  const text  = htmlToText(html).replace(/\s+/g, ' ').trim()
  const words = text ? text.split(' ').length : 0
  draft.value.readingMinutes = Math.max(1, Math.round(words / 200))
})
</script>

<template>
  <div data-testid="draft-editor" class="space-y-6">
    <!-- Save indicator (or read-only status) + close -->
    <div class="flex items-center justify-between">
      <span
        v-if="readonly"
        data-testid="draft-readonly-badge"
        class="inline-flex items-center gap-2 rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-800"
      >
        <span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>
        In review · read only
      </span>
      <SaveIndicator v-else :status="status" :last-saved-at="lastSavedAt" :error="errorMessage" />
      <button
        type="button"
        data-testid="draft-close"
        class="rounded-md border border-slypn-200 px-3 py-1.5 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
        @click="emit('close')"
      >Close</button>
    </div>

    <!-- Conflict banner -->
    <div
      v-if="conflict"
      data-testid="draft-conflict"
      class="rounded-xl border border-amber-300 bg-amber-50 p-5 text-sm text-amber-900"
    >
      <p class="font-display font-bold">This draft was updated elsewhere</p>
      <p class="mt-1">Another tab or session saved a newer version while you were writing.</p>
      <details class="mt-3 rounded-md bg-amber-100/60 p-3">
        <summary class="cursor-pointer text-xs font-semibold">Preview the server version</summary>
        <p class="mt-2 text-xs">Title: <strong>{{ conflict.serverDraft.title || '(empty)' }}</strong></p>
        <p class="text-xs">Summary: {{ conflict.serverDraft.summary || '(empty)' }}</p>
        <p class="mt-2 max-h-32 overflow-y-auto whitespace-pre-wrap text-xs" v-html="conflict.serverDraft.body" />
      </details>
      <div class="mt-4 flex flex-wrap gap-2">
        <button type="button"
          data-testid="draft-conflict-discard"
          class="rounded-md border border-amber-300 bg-white px-3 py-1.5 text-sm font-semibold text-amber-900 hover:bg-amber-100"
          @click="resolveByDiscardingLocal">Discard mine, use server version</button>
        <button type="button"
          data-testid="draft-conflict-overwrite"
          class="rounded-md bg-amber-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-amber-700"
          @click="resolveByForcingLocal">Overwrite server with my version</button>
      </div>
    </div>

    <!-- Revision feedback banner -->
    <div
      v-if="draft.revisionFeedback"
      data-testid="draft-revision-feedback"
      class="rounded-xl border border-amber-300 bg-amber-50 p-5"
    >
      <p class="font-display font-bold text-amber-900">Admin requested revisions</p>
      <p class="mt-2 whitespace-pre-wrap text-sm text-amber-900">{{ draft.revisionFeedback }}</p>
    </div>

    <!-- Draft form -->
    <div class="space-y-4 rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <fieldset>
        <legend class="text-sm font-medium text-slypn-800">Type</legend>

        <!-- A revision keeps the published item's id and slug so its URL survives, but the
             type picks the URL prefix — flipping it would move the item to the other prefix
             and 404 the address it was published under. Shown as a fact rather than hidden,
             so the type is still visible while editing. -->
        <p
          v-if="typeLocked"
          data-testid="draft-type-locked"
          class="mt-2 text-sm text-slypn-700"
        >
          {{ draft.type === 'blog' ? 'Blog post' : 'Article' }}
          <span class="text-slypn-900/60">— fixed once published</span>
        </p>

        <div v-else class="mt-2 inline-flex rounded-md border border-slypn-200 bg-white p-1">
          <button
            v-for="t in (['article', 'blog'] as const)"
            :key="t"
            type="button"
            :data-testid="`draft-type-${t}`"
            :disabled="readonly"
            :class="['rounded-md px-3 py-1.5 text-sm font-semibold transition-colors disabled:cursor-not-allowed',
              draft.type === t ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50 disabled:hover:bg-transparent']"
            @click="!readonly && (draft.type = t)"
          >{{ t === 'article' ? 'Article' : 'Blog post' }}</button>
        </div>
      </fieldset>

      <div>
        <label for="draft-title" class="block text-sm font-medium text-slypn-800">
          Title <span class="text-rose-500">*</span>
        </label>
        <input
          id="draft-title"
          v-model="draft.title"
          type="text"
          maxlength="200"
          :readonly="readonly"
          placeholder="Required to save"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm read-only:bg-slypn-50 read-only:text-slypn-600 focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
        <p v-if="!readonly && !draft.title.trim()" class="mt-1 text-xs text-slypn-400">Add a title to start saving.</p>
        <FieldCounter v-if="!readonly" :used="draft.title.length" :max="LIMITS.title" testid="draft-title-count" />
      </div>

      <div>
        <label for="draft-category" class="block text-sm font-medium text-slypn-800">Category</label>
        <div class="relative">
          <input
            id="draft-category"
            v-model="draft.category"
            type="text"
            maxlength="60"
            :readonly="readonly"
            list="draft-category-hints"
            autocomplete="off"
            placeholder="Pick an existing one or type a new category"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white py-2 pl-3 pr-9 text-sm shadow-sm read-only:bg-slypn-50 read-only:text-slypn-600 focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
          <ClearFieldButton
            v-if="!readonly && draft.category"
            field="category"
            testid="draft-category-clear"
            @clear="draft.category = ''"
          />
        </div>
        <datalist id="draft-category-hints">
          <option v-for="c in categoryHints" :key="c" :value="c" />
        </datalist>
        <FieldCounter v-if="!readonly" :used="draft.category.length" :max="LIMITS.category" testid="draft-category-count" />
      </div>

      <div>
        <label for="draft-summary" class="block text-sm font-medium text-slypn-800">Summary</label>
        <textarea
          id="draft-summary"
          v-model="draft.summary"
          maxlength="500"
          rows="2"
          :readonly="readonly"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm read-only:bg-slypn-50 read-only:text-slypn-600 focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
        <FieldCounter v-if="!readonly" :used="draft.summary.length" :max="LIMITS.summary" testid="draft-summary-count" />
      </div>

      <div>
        <span class="block text-sm font-medium text-slypn-800">Reading time</span>
        <p class="mt-1 rounded-md border border-slypn-100 bg-slypn-50 px-3 py-2 text-sm text-slypn-700">
          {{ draft.readingMinutes }} min <span class="text-slypn-400">· auto-calculated</span>
        </p>
      </div>
    </div>

    <RichTextEditor
      v-model="draft.body"
      :readonly="readonly"
      :max-length="LIMITS.body"
      :current-length="bodyTextLength"
      @upload-error="(msg) => uploadError = msg"
    />
    <!-- Only once it starts to matter. Counts HTML, which is what the limit applies to,
         so it runs ahead of the visible word count on a heavily formatted piece. -->
    <FieldCounter v-if="!readonly" :used="bodyTextLength" :max="LIMITS.body" testid="draft-body-count" />

    <p v-if="!readonly && uploadError" data-testid="draft-upload-error" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Image upload failed: {{ uploadError }}
    </p>

    <div v-if="!readonly" class="flex items-center justify-between rounded-xl border border-slypn-100 bg-white px-6 py-4 shadow-sm">
      <div>
        <p v-if="!canSubmit" data-testid="draft-submit-missing" class="text-xs text-slypn-400">
          Still required to submit: {{ missingToSubmit.join(', ') }}.
        </p>
      </div>
      <button
        type="button"
        data-testid="draft-submit"
        class="rounded-md bg-slypn-600 px-5 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
        :disabled="submitting || !canSubmit"
        @click="submitForReview"
      >{{ submitting ? 'Submitting…' : 'Submit for review' }}</button>
    </div>

    <p
      v-if="!readonly && submitNotice"
      data-testid="draft-submit-notice"
      class="flex items-start gap-2 rounded-md border border-slypn-100 bg-slypn-50 px-4 py-2 text-sm text-slypn-800"
    >
      <svg class="mt-0.5 h-4 w-4 shrink-0 text-slypn-500" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path fill-rule="evenodd" d="M18 10A8 8 0 1 1 2 10a8 8 0 0 1 16 0Zm-9-4a1 1 0 1 1 2 0 1 1 0 0 1-2 0Zm1 3a1 1 0 0 0-1 1v4a1 1 0 1 0 2 0v-4a1 1 0 0 0-1-1Z" clip-rule="evenodd" />
      </svg>
      <span>{{ submitNotice }}</span>
    </p>

    <p v-if="!readonly && submitError" data-testid="draft-submit-error" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Submit failed: {{ submitError }}
    </p>
  </div>
</template>
