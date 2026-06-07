<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import RichTextEditor from '@/components/editor/RichTextEditor.vue'
import SaveIndicator from '@/components/editor/SaveIndicator.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { useBeforeUnload } from '@/composables/useBeforeUnload'
import { apiFetch, apiJson } from '@/lib/api'

interface DraftPayload {
  type: 'article' | 'blog'
  title: string
  slug: string
  summary: string
  body: string
  category: string
  tags: string[]
  readingMinutes: number
  revisionFeedback?: string | null
}

interface DraftSummary {
  id: string
  title: string
  type: string
  updatedAt: string
  _etag?: string
}

const DRAFT_ID_KEY = 'slypn:editor:draft-id'
const EMPTY_DRAFT: DraftPayload = {
  type: 'article', title: '', slug: '', summary: '',
  body: '', category: '', tags: [], readingMinutes: 1,
}

function makeId() {
  if ('randomUUID' in crypto) return crypto.randomUUID().replace(/-/g, '')
  return Math.random().toString(16).slice(2).padEnd(32, '0')
}

// ── Draft list ──────────────────────────────────────────────────────────────
const allDrafts   = ref<DraftSummary[]>([])
const loadingList = ref(false)
const listError   = ref<string | null>(null)
const showDrafts  = ref(true)

async function loadDraftList() {
  loadingList.value = true
  listError.value   = null
  try {
    const list = await apiJson<DraftSummary[]>('/drafts')
    allDrafts.value = list.sort((a, b) =>
      new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())
  } catch (err) {
    listError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loadingList.value = false
  }
}

// ── Active draft ─────────────────────────────────────────────────────────────
const draftId     = ref<string>(localStorage.getItem(DRAFT_ID_KEY) ?? makeId())
const draft       = ref<DraftPayload>({ ...EMPTY_DRAFT })
const currentEtag = ref<string | null>(null)
const switching   = ref(false)

const uploadError   = ref<string | null>(null)
const submitting    = ref(false)
const submitMessage = ref<string | null>(null)
const submitError   = ref<string | null>(null)
const deleteError   = ref<string | null>(null)

interface ConflictState { serverDraft: DraftPayload; serverEtag: string | null }
const conflict = ref<ConflictState | null>(null)

async function loadDraft(id: string) {
  switching.value = true
  try {
    const resp = await apiFetch(`/drafts/${id}`)
    if (resp.status === 404) return
    if (!resp.ok) return
    draft.value = { ...EMPTY_DRAFT, ...await resp.json() as DraftPayload }
    currentEtag.value = resp.headers.get('ETag')
  } catch {
    // new draft — nothing to load
  } finally {
    switching.value = false
  }
}

async function save(value: DraftPayload) {
  if (switching.value || !value.title.trim()) return

  const headers: Record<string, string> = {}
  if (currentEtag.value) headers['If-Match'] = currentEtag.value

  const resp = await apiFetch(`/drafts/${draftId.value}`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(value),
  })

  if (resp.status === 412) {
    const reload = await apiFetch(`/drafts/${draftId.value}`)
    conflict.value = {
      serverDraft: reload.ok ? await reload.json() as DraftPayload : { ...EMPTY_DRAFT },
      serverEtag:  reload.ok ? reload.headers.get('ETag') : null,
    }
    throw new Error('412 — another tab updated this draft.')
  }
  if (!resp.ok) {
    const body = await resp.text().catch(() => '')
    throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
  }
  currentEtag.value = resp.headers.get('ETag') ?? currentEtag.value

  // Keep the draft list in sync without a round-trip
  const summary: DraftSummary = {
    id:        draftId.value,
    title:     value.title,
    type:      value.type,
    updatedAt: new Date().toISOString(),
    _etag:     currentEtag.value ?? undefined,
  }
  const idx = allDrafts.value.findIndex(d => d.id === draftId.value)
  if (idx >= 0) allDrafts.value.splice(idx, 1, summary)
  else          allDrafts.value.unshift(summary)
}

async function openDraft(id: string) {
  if (draftId.value === id) return
  // Save unsaved changes before leaving
  if (draft.value.title.trim()) try { await save(draft.value) } catch { /* ignore */ }
  draftId.value = id
  localStorage.setItem(DRAFT_ID_KEY, id)
  draft.value   = { ...EMPTY_DRAFT }
  currentEtag.value = null
  conflict.value    = null
  submitMessage.value = null
  submitError.value   = null
  await loadDraft(id)
}

function startFresh() {
  const id = makeId()
  draftId.value       = id
  localStorage.setItem(DRAFT_ID_KEY, id)
  draft.value         = { ...EMPTY_DRAFT }
  currentEtag.value   = null
  conflict.value      = null
  submitMessage.value = null
  submitError.value   = null
}

async function deleteDraft(id: string, etag?: string) {
  if (!confirm('Delete this draft? This cannot be undone.')) return
  deleteError.value = null
  try {
    const resp = await apiFetch(`/drafts/${id}`, {
      method: 'DELETE',
      headers: etag ? { 'If-Match': etag } : {},
    })
    if (!resp.ok) { deleteError.value = `${resp.status} ${await resp.text().catch(() => '')}`; return }
    allDrafts.value = allDrafts.value.filter(d => d.id !== id)
    if (draftId.value === id) startFresh()
  } catch (err) {
    deleteError.value = err instanceof Error ? err.message : String(err)
  }
}

async function submitForReview() {
  if (submitting.value) return
  submitMessage.value = null
  submitError.value   = null
  submitting.value    = true
  try {
    await save(draft.value)
    const resp = await apiFetch(`/drafts/${draftId.value}/submit`, { method: 'POST' })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    submitMessage.value = 'Submitted for admin review.'
    allDrafts.value = allDrafts.value.filter(d => d.id !== draftId.value)
    localStorage.removeItem(DRAFT_ID_KEY)
    startFresh()
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

// ── Init ─────────────────────────────────────────────────────────────────────
onMounted(async () => {
  await loadDraft(draftId.value)
  await loadDraftList()
})

const { status, lastSavedAt, errorMessage } = useAutoSave(draft, save, { debounce: 1500 })

useBeforeUnload(computed(() =>
  status.value === 'pending' || status.value === 'saving' || status.value === 'error',
))

watch(() => draft.value.body, (html) => {
  const text  = html.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
  const words = text ? text.split(' ').length : 0
  draft.value.readingMinutes = Math.max(1, Math.round(words / 200))
})

const tagsCsv = computed({
  get: () => draft.value.tags.join(', '),
  set: (v: string) => { draft.value.tags = v.split(',').map(s => s.trim()).filter(Boolean) },
})

const canSubmit = computed(() =>
  !!draft.value.title.trim() && !!draft.value.summary.trim() && !!draft.value.body.trim())

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
</script>

<template>
  <HeroBanner
    eyebrow="Editor"
    title="Write something for the community"
    subtitle="Drafts autosave 1.5 s after you stop typing (title required). Submit when ready for review."
  />

  <section class="mx-auto max-w-3xl space-y-6 px-6 py-12">

    <!-- Drafts panel -->
    <div class="rounded-xl border border-slypn-100 bg-white shadow-sm">
      <button
        type="button"
        class="flex w-full items-center justify-between px-6 py-4 text-left"
        @click="showDrafts = !showDrafts"
      >
        <h2 class="font-display text-lg font-bold text-slypn-700">
          My drafts
          <span v-if="allDrafts.length" class="ml-1.5 rounded-full bg-slypn-100 px-2 py-0.5 text-xs font-semibold text-slypn-600">
            {{ allDrafts.length }}
          </span>
        </h2>
        <span class="text-slypn-400">{{ showDrafts ? '▲' : '▼' }}</span>
      </button>

      <div v-if="showDrafts" class="border-t border-slypn-100">
        <p v-if="loadingList" class="px-6 py-3 text-sm text-slypn-900/60">Loading…</p>
        <p v-else-if="listError" class="px-6 py-3 text-sm text-rose-600">{{ listError }}</p>

        <div v-if="allDrafts.length" class="divide-y divide-slypn-100">
          <div
            v-for="d in allDrafts"
            :key="d.id"
            :class="['flex items-center gap-3 px-6 py-3 transition-colors', d.id === draftId ? 'bg-slypn-50' : 'hover:bg-slypn-50/50']"
          >
            <span class="shrink-0 rounded-full bg-slypn-100 px-2 py-0.5 text-xs font-semibold text-slypn-700">
              {{ d.type === 'blog' ? 'Blog' : 'Article' }}
            </span>
            <p class="min-w-0 flex-1 truncate text-sm font-medium text-slypn-800">
              {{ d.title || '(untitled)' }}
            </p>
            <span class="shrink-0 text-xs text-slypn-400">{{ fmtDate(d.updatedAt) }}</span>
            <span v-if="d.id === draftId" class="shrink-0 text-xs font-semibold text-slypn-500">Editing</span>
            <button
              v-else
              type="button"
              class="shrink-0 rounded px-2 py-1 text-xs font-semibold text-slypn-600 hover:bg-slypn-100"
              @click="openDraft(d.id)"
            >Edit</button>
            <button
              type="button"
              class="shrink-0 rounded px-2 py-1 text-xs font-semibold text-rose-500 hover:bg-rose-50"
              title="Delete draft"
              @click="deleteDraft(d.id, d._etag)"
            >✕</button>
          </div>
        </div>

        <p v-else-if="!loadingList" class="px-6 py-3 text-sm text-slypn-900/50">No saved drafts yet.</p>

        <div class="flex items-center gap-4 border-t border-slypn-100 px-6 py-3">
          <button
            type="button"
            class="rounded-md border border-slypn-200 px-3 py-1.5 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
            @click="startFresh"
          >+ New draft</button>
          <p v-if="deleteError" class="text-xs text-rose-600">{{ deleteError }}</p>
        </div>
      </div>
    </div>

    <!-- Save indicator -->
    <div class="flex items-center justify-between">
      <p class="font-display text-xs uppercase tracking-widest text-slypn-500">
        Draft <code class="text-slypn-700">{{ draftId }}</code>
      </p>
      <SaveIndicator :status="status" :last-saved-at="lastSavedAt" :error="errorMessage" />
    </div>

    <!-- Conflict banner -->
    <div
      v-if="conflict"
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
          class="rounded-md border border-amber-300 bg-white px-3 py-1.5 text-sm font-semibold text-amber-900 hover:bg-amber-100"
          @click="resolveByDiscardingLocal">Discard mine, use server version</button>
        <button type="button"
          class="rounded-md bg-amber-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-amber-700"
          @click="resolveByForcingLocal">Overwrite server with my version</button>
      </div>
    </div>

    <!-- Revision feedback banner -->
    <div
      v-if="draft.revisionFeedback"
      class="rounded-xl border border-amber-300 bg-amber-50 p-5"
    >
      <p class="font-display font-bold text-amber-900">Admin requested revisions</p>
      <p class="mt-2 whitespace-pre-wrap text-sm text-amber-900">{{ draft.revisionFeedback }}</p>
    </div>

    <!-- Draft form -->
    <div class="space-y-4 rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <fieldset>
        <legend class="text-sm font-medium text-slypn-800">Type</legend>
        <div class="mt-2 inline-flex rounded-md border border-slypn-200 bg-white p-1">
          <button
            v-for="t in (['article', 'blog'] as const)"
            :key="t"
            type="button"
            :class="['rounded-md px-3 py-1.5 text-sm font-semibold transition-colors',
              draft.type === t ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50']"
            @click="draft.type = t"
          >{{ t === 'article' ? 'Article' : 'Blog post' }}</button>
        </div>
      </fieldset>

      <div>
        <label class="block text-sm font-medium text-slypn-800">
          Title <span class="text-rose-500">*</span>
        </label>
        <input
          v-model="draft.title"
          type="text"
          maxlength="200"
          placeholder="Required to save"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
        <p v-if="!draft.title.trim()" class="mt-1 text-xs text-slypn-400">Add a title to start saving.</p>
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
          <label class="block text-sm font-medium text-slypn-800">Reading time</label>
          <p class="mt-1 rounded-md border border-slypn-100 bg-slypn-50 px-3 py-2 text-sm text-slypn-700">
            {{ draft.readingMinutes }} min <span class="text-slypn-400">· auto-calculated</span>
          </p>
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

    <div class="flex items-center justify-between rounded-xl border border-slypn-100 bg-white px-6 py-4 shadow-sm">
      <div>
        <p v-if="!canSubmit" class="text-xs text-slypn-400">
          Title, summary and body are required to submit.
        </p>
      </div>
      <button
        type="button"
        class="rounded-md bg-slypn-600 px-5 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
        :disabled="submitting || !canSubmit"
        @click="submitForReview"
      >{{ submitting ? 'Submitting…' : 'Submit for review' }}</button>
    </div>

    <p v-if="submitError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Submit failed: {{ submitError }}
    </p>
    <p v-if="submitMessage" class="rounded-md bg-emerald-50 px-4 py-2 text-sm text-emerald-900">
      {{ submitMessage }}
    </p>

  </section>
</template>
