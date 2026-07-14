<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import DraftEditor from '@/components/editor/DraftEditor.vue'
import { apiFetch, apiJson } from '@/lib/api'
import { EMPTY_DRAFT, makeDraftId, type DraftPayload, type DraftSummary } from '@/lib/draft'
import { useAuthStore } from '@/stores/auth'

const DRAFT_ID_KEY = 'slypn:editor:draft-id'

const auth = useAuthStore()

// ── Draft list ──────────────────────────────────────────────────────────────
const allDrafts   = ref<DraftSummary[]>([])
const loadingList = ref(false)
const listError   = ref<string | null>(null)

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

// ── Pending review ────────────────────────────────────────────────────────────
// Drafts the author has submitted are locked in review (read-only here) until an
// admin approves or sends them back. The list endpoints are public, so filter to
// the signed-in author's own submissions.
interface PendingItem {
  id: string
  title: string
  type?: string
  slug?: string
  summary?: string
  body?: string
  category?: string
  tags?: string[]
  readingMinutes?: number
  publishedAt: string
  authorId?: string
  replacesArticleId?: string | null
}

const pendingItems   = ref<PendingItem[]>([])
const loadingPending = ref(false)
const pendingError   = ref<string | null>(null)

async function loadPending() {
  loadingPending.value = true
  pendingError.value   = null
  try {
    const [arts, blogs] = await Promise.all([
      apiJson<PendingItem[]>('/articles?status=in-review'),
      apiJson<PendingItem[]>('/blog?status=in-review'),
    ])
    const mine = [...arts, ...blogs].filter(x => auth.oid && x.authorId === auth.oid)
    pendingItems.value = mine.sort(
      (a, b) => new Date(b.publishedAt).getTime() - new Date(a.publishedAt).getTime())
  } catch (err) {
    pendingError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loadingPending.value = false
  }
}

// ── Combined list ─────────────────────────────────────────────────────────────
// Editable drafts and read-only in-review submissions share one list; the entry
// state drives whether the row opens for editing or just shows its review status.
interface DraftEntry {
  id: string
  title: string
  type?: string
  date: string
  state: 'draft' | 'in-review'
  isRevision?: boolean
  etag?: string
}

const entries = computed<DraftEntry[]>(() => {
  const drafts: DraftEntry[] = allDrafts.value.map(d => ({
    id: d.id, title: d.title, type: d.type, date: d.updatedAt, state: 'draft', etag: d._etag,
  }))
  const pending: DraftEntry[] = pendingItems.value.map(p => ({
    id: p.id, title: p.title, type: p.type, date: p.publishedAt,
    state: 'in-review', isRevision: !!p.replacesArticleId,
  }))
  return [...drafts, ...pending].sort(
    (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
})

// ── Open draft (DraftEditor owns the editing surface) ────────────────────────
const draftId         = ref<string>('')
const editorOpen      = ref(false)
const submitMessage   = ref<string | null>(null)
const deleteError     = ref<string | null>(null)
const draftEditorRef  = ref<InstanceType<typeof DraftEditor> | null>(null)
// Non-null while viewing an in-review submission read-only.
const readonlyContent = ref<DraftPayload | null>(null)

async function openDraft(id: string) {
  if (editorOpen.value && draftId.value === id && !readonlyContent.value) return // already editing
  if (editorOpen.value) await draftEditorRef.value?.flush()
  submitMessage.value = null
  readonlyContent.value = null
  draftId.value = id
  localStorage.setItem(DRAFT_ID_KEY, id)
  editorOpen.value = true
}

// Open an in-review submission in the editor, read-only.
async function openReadonly(item: PendingItem) {
  if (editorOpen.value && draftId.value === item.id && readonlyContent.value) return
  if (editorOpen.value) await draftEditorRef.value?.flush()
  submitMessage.value = null
  readonlyContent.value = {
    type:           item.type === 'blog' ? 'blog' : 'article',
    title:          item.title,
    slug:           item.slug ?? '',
    summary:        item.summary ?? '',
    body:           item.body ?? '',
    category:       item.category ?? '',
    tags:           item.tags ?? [],
    readingMinutes: item.readingMinutes ?? 1,
  }
  draftId.value = item.id
  localStorage.removeItem(DRAFT_ID_KEY)
  editorOpen.value = true
}

function onRowClick(e: DraftEntry) {
  if (e.state === 'draft') { openDraft(e.id); return }
  const item = pendingItems.value.find(p => p.id === e.id)
  if (item) openReadonly(item)
}

async function closeEditor() {
  await draftEditorRef.value?.flush()
  editorOpen.value = false
  readonlyContent.value = null
}

function onSaved(summary: DraftSummary) {
  const idx = allDrafts.value.findIndex(d => d.id === summary.id)
  if (idx >= 0) allDrafts.value.splice(idx, 1, summary)
  else          allDrafts.value.unshift(summary)
}

function onSubmitted(id: string) {
  allDrafts.value = allDrafts.value.filter(d => d.id !== id)
  localStorage.removeItem(DRAFT_ID_KEY)
  editorOpen.value = false
  submitMessage.value = 'Submitted for admin review.'
  loadPending()
}

// ── New draft dialog ─────────────────────────────────────────────────────────
const showNewDraft  = ref(false)
const newTitle      = ref('')
const newType       = ref<'article' | 'blog'>('article')
const newTitleInput = ref<HTMLInputElement | null>(null)

function openNewDraftDialog() {
  newTitle.value = ''
  newType.value  = 'article'
  showNewDraft.value = true
  nextTick(() => newTitleInput.value?.focus())
}

async function createDraft() {
  const title = newTitle.value.trim()
  if (!title) return
  if (editorOpen.value) await draftEditorRef.value?.flush()

  const id = makeDraftId()
  const payload = { ...EMPTY_DRAFT, title, type: newType.value }
  try {
    await apiFetch(`/drafts/${id}`, { method: 'PUT', body: JSON.stringify(payload) })
  } catch { /* DraftEditor autosave will retry */ }

  allDrafts.value.unshift({ id, title, type: newType.value, updatedAt: new Date().toISOString() })
  showNewDraft.value  = false
  submitMessage.value = null
  draftId.value       = id
  localStorage.setItem(DRAFT_ID_KEY, id)
  editorOpen.value    = true
}

// ── Delete ───────────────────────────────────────────────────────────────────
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
    if (draftId.value === id) { editorOpen.value = false; localStorage.removeItem(DRAFT_ID_KEY) }
  } catch (err) {
    deleteError.value = err instanceof Error ? err.message : String(err)
  }
}

onMounted(() => { loadDraftList(); loadPending() })

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
</script>

<template>
  <HeroBanner
    eyebrow="Editor"
    title="Write something for the community"
    subtitle="Drafts autosave 1.5 s after you stop typing (title required). Submit when ready for review."
  />

  <section class="page-container space-y-6 py-12">

    <!-- Drafts panel -->
    <div class="rounded-xl border border-slypn-100 bg-white shadow-sm">
      <div class="px-6 py-4">
        <h2 class="font-display text-lg font-bold text-slypn-700">
          My drafts
          <span v-if="entries.length" class="ml-1.5 rounded-full bg-slypn-100 px-2 py-0.5 text-xs font-semibold text-slypn-600">
            {{ entries.length }}
          </span>
        </h2>
      </div>

      <div class="border-t border-slypn-100">
        <p v-if="loadingList || loadingPending" class="px-6 py-3 text-sm text-slypn-900/60">Loading…</p>
        <p v-else-if="listError" class="px-6 py-3 text-sm text-rose-600">{{ listError }}</p>
        <p v-else-if="pendingError" class="px-6 py-3 text-sm text-rose-600">{{ pendingError }}</p>

        <div v-if="entries.length" class="divide-y divide-slypn-100">
          <div
            v-for="e in entries"
            :key="e.id"
            role="button"
            tabindex="0"
            :aria-current="editorOpen && e.id === draftId"
            :class="['flex cursor-pointer items-center gap-3 px-6 py-3 transition-colors',
              editorOpen && e.id === draftId
                ? (e.state === 'in-review'
                    ? 'bg-amber-100/60 ring-1 ring-inset ring-amber-300'
                    : 'bg-slypn-50 ring-1 ring-inset ring-slypn-300')
                : (e.state === 'in-review'
                    ? 'bg-amber-50/40 hover:bg-amber-50'
                    : 'hover:bg-slypn-50/50')]"
            @click="onRowClick(e)"
            @keydown.enter="onRowClick(e)"
          >
            <span class="shrink-0 rounded-full bg-slypn-100 px-2 py-0.5 text-xs font-semibold text-slypn-700">
              {{ e.type === 'blog' ? 'Blog' : 'Article' }}
            </span>
            <p class="min-w-0 flex-1 truncate text-sm font-medium text-slypn-800">
              {{ e.title || '(untitled)' }}
            </p>
            <span
              v-if="e.state === 'in-review' && e.isRevision"
              class="shrink-0 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-700"
            >Revision</span>
            <span
              v-if="e.state === 'in-review'"
              class="shrink-0 rounded-full bg-amber-500 px-2 py-0.5 text-xs font-semibold text-white"
            >In review</span>
            <span
              v-else-if="editorOpen && e.id === draftId"
              class="shrink-0 rounded-full bg-slypn-600 px-2 py-0.5 text-xs font-semibold text-white"
            >Editing</span>
            <span class="shrink-0 text-xs text-slypn-400">{{ fmtDate(e.date) }}</span>
            <button
              v-if="e.state === 'draft'"
              type="button"
              class="shrink-0 rounded px-2 py-1 text-xs font-semibold text-rose-500 hover:bg-rose-50"
              title="Delete draft"
              @click.stop="deleteDraft(e.id, e.etag)"
            >✕</button>
            <span v-else class="w-7 shrink-0" aria-hidden="true"></span>
          </div>
        </div>

        <p v-else-if="!loadingList && !loadingPending" class="px-6 py-3 text-sm text-slypn-900/50">No drafts or submissions yet.</p>

        <div class="flex items-center gap-4 border-t border-slypn-100 px-6 py-3">
          <button
            type="button"
            class="rounded-md border border-slypn-200 px-3 py-1.5 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
            @click="openNewDraftDialog"
          >+ New draft</button>
          <p v-if="deleteError" class="text-xs text-rose-600">{{ deleteError }}</p>
        </div>
      </div>
    </div>

    <!-- Editor (reused control; only shown once a draft is opened) -->
    <DraftEditor
      v-if="editorOpen"
      ref="draftEditorRef"
      :draft-id="draftId"
      :readonly="!!readonlyContent"
      :initial-content="readonlyContent"
      @close="closeEditor"
      @saved="onSaved"
      @submitted="onSubmitted"
    />

    <p v-if="submitMessage" class="rounded-md bg-emerald-50 px-4 py-2 text-sm text-emerald-900">
      {{ submitMessage }}
    </p>

    <!-- New draft dialog -->
    <Teleport to="body">
      <div
        v-if="showNewDraft"
        class="fixed inset-0 z-50 flex items-center justify-center bg-slypn-900/40 p-4"
        @click.self="showNewDraft = false"
        @keydown.esc="showNewDraft = false"
      >
        <div class="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
          <h3 class="font-display text-lg font-bold text-slypn-700">New draft</h3>
          <form class="mt-4 space-y-4" @submit.prevent="createDraft">
            <div>
              <label for="new-draft-title" class="block text-sm font-medium text-slypn-800">
                Title <span class="text-rose-500">*</span>
              </label>
              <input
                id="new-draft-title"
                ref="newTitleInput"
                v-model="newTitle"
                type="text"
                maxlength="200"
                required
                placeholder="What are you writing?"
                class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>
            <fieldset>
              <legend class="text-sm font-medium text-slypn-800">Type</legend>
              <div class="mt-2 inline-flex rounded-md border border-slypn-200 bg-white p-1">
                <button
                  v-for="t in (['article', 'blog'] as const)"
                  :key="t"
                  type="button"
                  :class="['rounded-md px-3 py-1.5 text-sm font-semibold transition-colors',
                    newType === t ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50']"
                  @click="newType = t"
                >{{ t === 'article' ? 'Article' : 'Blog post' }}</button>
              </div>
            </fieldset>
            <div class="flex justify-end gap-2 pt-2">
              <button
                type="button"
                class="rounded-md border border-slypn-200 px-4 py-2 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
                @click="showNewDraft = false"
              >Cancel</button>
              <button
                type="submit"
                class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
                :disabled="!newTitle.trim()"
              >Create draft</button>
            </div>
          </form>
        </div>
      </div>
    </Teleport>

  </section>
</template>
