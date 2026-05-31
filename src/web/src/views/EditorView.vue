<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import RichTextEditor from '@/components/editor/RichTextEditor.vue'
import SaveIndicator from '@/components/editor/SaveIndicator.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { useBeforeUnload } from '@/composables/useBeforeUnload'
import { apiFetch } from '@/lib/api'

interface DraftPayload {
  type: 'article' | 'blog'
  title: string
  slug: string
  summary: string
  body: string
  category: string
  tags: string[]
  readingMinutes: number
}

const DRAFT_ID_KEY = 'slypn:editor:draft-id'
const EMPTY_DRAFT: DraftPayload = {
  type: 'article',
  title: '',
  slug: '',
  summary: '',
  body: '<p>Start writing&hellip;</p>',
  category: '',
  tags: [],
  readingMinutes: 0,
}

function makeId() {
  if ('randomUUID' in crypto) return crypto.randomUUID().replace(/-/g, '')
  return Math.random().toString(16).slice(2).padEnd(32, '0')
}

const draftId = ref<string>(localStorage.getItem(DRAFT_ID_KEY) ?? makeId())
localStorage.setItem(DRAFT_ID_KEY, draftId.value)

const draft = ref<DraftPayload>({ ...EMPTY_DRAFT })
const currentEtag = ref<string | null>(null)

const uploadError    = ref<string | null>(null)
const submitting     = ref(false)
const submitMessage  = ref<string | null>(null)
const submitError    = ref<string | null>(null)

interface ConflictState {
  serverDraft: DraftPayload
  serverEtag: string | null
}
const conflict = ref<ConflictState | null>(null)

async function loadExisting() {
  try {
    const resp = await apiFetch(`/drafts/${draftId.value}`)
    if (resp.status === 404) return
    if (!resp.ok) return
    const fetched = await resp.json() as DraftPayload
    draft.value = { ...EMPTY_DRAFT, ...fetched }
    currentEtag.value = resp.headers.get('ETag')
  } catch {
    // Network errors are fine on first load — we'll create on first autosave.
  }
}

async function save(value: DraftPayload) {
  const headers: Record<string, string> = {}
  if (currentEtag.value) headers['If-Match'] = currentEtag.value
  const resp = await apiFetch(`/drafts/${draftId.value}`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(value),
  })
  if (resp.status === 412) {
    // Someone else (another tab / session) updated this draft. Fetch the
    // server version so the user can decide.
    const reload = await apiFetch(`/drafts/${draftId.value}`)
    const serverDraft = reload.ok ? await reload.json() as DraftPayload : { ...EMPTY_DRAFT }
    conflict.value = {
      serverDraft,
      serverEtag: reload.ok ? reload.headers.get('ETag') : null,
    }
    throw new Error('412 — another tab updated this draft. Resolve the conflict before saving again.')
  }
  if (!resp.ok) {
    const body = await resp.text().catch(() => '')
    throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
  }
  // Capture the new etag so the next PUT sends a matching If-Match.
  currentEtag.value = resp.headers.get('ETag') ?? currentEtag.value
}

async function submitForReview() {
  if (submitting.value) return
  submitMessage.value = null
  submitError.value = null
  submitting.value = true
  try {
    await save(draft.value)
    const resp = await apiFetch(`/drafts/${draftId.value}/submit`, { method: 'POST' })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    submitMessage.value = 'Submitted for admin review. You can start a new draft now.'
    localStorage.removeItem(DRAFT_ID_KEY)
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}

function startFresh() {
  draftId.value = makeId()
  localStorage.setItem(DRAFT_ID_KEY, draftId.value)
  draft.value = { ...EMPTY_DRAFT }
  currentEtag.value = null
  conflict.value = null
}

// Conflict resolution -------------------------------------------------------
function resolveByDiscardingLocal() {
  if (!conflict.value) return
  draft.value = { ...EMPTY_DRAFT, ...conflict.value.serverDraft }
  currentEtag.value = conflict.value.serverEtag
  conflict.value = null
}

async function resolveByForcingLocal() {
  if (!conflict.value) return
  // Clear our etag so the next PUT goes through without If-Match — the
  // upsert path overwrites whatever's in Cosmos.
  currentEtag.value = null
  const localValue = draft.value
  conflict.value = null
  try {
    await save(localValue)
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : String(err)
  }
}

onMounted(loadExisting)

const { status, lastSavedAt, errorMessage } = useAutoSave(draft, save, { debounce: 1500 })

useBeforeUnload(computed(() =>
  status.value === 'pending' || status.value === 'saving' || status.value === 'error',
))

const tagsCsv = computed({
  get: () => draft.value.tags.join(', '),
  set: (v: string) => { draft.value.tags = v.split(',').map(s => s.trim()).filter(Boolean) },
})
</script>

<template>
  <HeroBanner
    eyebrow="Editor"
    title="Write something for the community"
    subtitle="Edits autosave 1.5s after you stop typing. Hit Submit when you're ready for an admin to review."
  >
    <template #actions>
      <button
        type="button"
        class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
        :disabled="submitting || !draft.title || !draft.summary || !draft.body"
        @click="submitForReview"
      >
        {{ submitting ? 'Submitting…' : 'Submit for review' }}
      </button>
      <button
        type="button"
        class="rounded-md border border-slypn-200 bg-white px-4 py-2 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
        @click="startFresh"
      >
        Start a new draft
      </button>
    </template>
  </HeroBanner>

  <section class="mx-auto max-w-3xl space-y-6 px-6 py-12">
    <div class="flex items-center justify-between">
      <p class="font-display text-xs uppercase tracking-widest text-slypn-500">
        Draft <code class="text-slypn-700">{{ draftId }}</code>
      </p>
      <SaveIndicator :status="status" :last-saved-at="lastSavedAt" :error="errorMessage" />
    </div>

    <div
      v-if="conflict"
      class="rounded-xl border border-amber-300 bg-amber-50 p-5 text-sm text-amber-900"
    >
      <p class="font-display font-bold text-amber-900">
        This draft was updated elsewhere
      </p>
      <p class="mt-1">
        Another tab or session saved a newer version while you were writing. Pick how to resolve:
      </p>
      <details class="mt-3 rounded-md bg-amber-100/60 p-3">
        <summary class="cursor-pointer text-xs font-semibold">Preview the server version</summary>
        <p class="mt-2 text-xs">Title: <strong>{{ conflict.serverDraft.title || '(empty)' }}</strong></p>
        <p class="text-xs">Summary: {{ conflict.serverDraft.summary || '(empty)' }}</p>
        <p class="mt-2 max-h-32 overflow-y-auto whitespace-pre-wrap text-xs" v-html="conflict.serverDraft.body" />
      </details>
      <div class="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          class="rounded-md border border-amber-300 bg-white px-3 py-1.5 text-sm font-semibold text-amber-900 hover:bg-amber-100"
          @click="resolveByDiscardingLocal"
        >
          Discard my changes, use server version
        </button>
        <button
          type="button"
          class="rounded-md bg-amber-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-amber-700"
          @click="resolveByForcingLocal"
        >
          Overwrite server with my version
        </button>
      </div>
    </div>

    <div class="space-y-4 rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <div>
        <label class="block text-sm font-medium text-slypn-800">Title</label>
        <input
          v-model="draft.title"
          type="text"
          maxlength="200"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="block text-sm font-medium text-slypn-800">Slug</label>
          <input
            v-model="draft.slug"
            type="text"
            maxlength="120"
            placeholder="my-article-title"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-slypn-800">Category</label>
          <input
            v-model="draft.category"
            type="text"
            maxlength="60"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
      </div>
      <div>
        <label class="block text-sm font-medium text-slypn-800">Summary</label>
        <textarea
          v-model="draft.summary"
          maxlength="500"
          rows="2"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="block text-sm font-medium text-slypn-800">Tags (comma-separated)</label>
          <input
            v-model="tagsCsv"
            type="text"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-slypn-800">Reading minutes</label>
          <input
            v-model.number="draft.readingMinutes"
            type="number"
            min="0"
            max="60"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
      </div>
    </div>

    <RichTextEditor
      v-model="draft.body"
      @upload-error="(msg) => uploadError = msg"
    />

    <p v-if="uploadError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Image upload failed: {{ uploadError }}
    </p>
    <p v-if="submitError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Submit failed: {{ submitError }}
    </p>
    <p v-if="submitMessage" class="rounded-md bg-emerald-50 px-4 py-2 text-sm text-emerald-900">
      {{ submitMessage }}
    </p>
  </section>
</template>
