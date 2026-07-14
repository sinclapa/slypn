<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { apiErrorMessage, apiFetch } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'
import DraftEditor from '@/components/editor/DraftEditor.vue'

interface PublishedItem {
  id: string
  slug: string
  title: string
  summary: string
  author: string
  authorId?: string | null
  publishedAt: string
  category: string
  type?: 'article' | 'blog'
  status: string
  deletionRequestedBy?: string | null
}

const auth = useAuthStore()

const items     = ref<PublishedItem[]>([])
const loading   = ref(false)
const loadError = ref<string | null>(null)
const busy      = ref<Record<string, boolean>>({})
const errors    = ref<Record<string, string | null>>({})
// Published article ids that already have a revision waiting in the approvals queue.
const revisionPending = ref<Set<string>>(new Set())

// Admins manage everything; contributors only their own published content.
const visibleItems = computed(() => {
  const sorted = [...items.value].sort(
    (a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt),
  )
  if (auth.isAdmin) return sorted
  return sorted.filter(i => i.authorId && i.authorId === auth.oid)
})

// ── Filter / search / pagination ─────────────────────────────────────────────
const PAGE_SIZE = 10
const typeFilter = ref<'all' | 'article' | 'blog'>('all')
const search     = ref('')
const page       = ref(1)

const filtered = computed(() => {
  let list = visibleItems.value
  if (typeFilter.value !== 'all') {
    list = list.filter(i => (i.type ?? 'article') === typeFilter.value)
  }
  const q = search.value.trim().toLowerCase()
  if (q) {
    list = list.filter(i =>
      i.title.toLowerCase().includes(q) || i.summary.toLowerCase().includes(q))
  }
  return list
})

const totalPages = computed(() => Math.max(1, Math.ceil(filtered.value.length / PAGE_SIZE)))
const pagedItems = computed(() =>
  filtered.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE))

// Reset to the first page whenever the result set changes.
watch([typeFilter, search, visibleItems], () => { page.value = 1 })
// Keep the page in range if items are removed.
watch(totalPages, (n) => { if (page.value > n) page.value = n })

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const [articlesResp, blogResp, reviewArtResp, reviewBlogResp] = await Promise.all([
      apiFetch('/articles?status=published'),
      apiFetch('/blog?status=published'),
      apiFetch('/articles?status=in-review'),
      apiFetch('/blog?status=in-review'),
    ])
    if (!articlesResp.ok) throw new Error(`/articles: ${articlesResp.status} ${articlesResp.statusText}`)
    if (!blogResp.ok)     throw new Error(`/blog: ${blogResp.status} ${blogResp.statusText}`)
    const [a, b] = await Promise.all([
      articlesResp.json() as Promise<PublishedItem[]>,
      blogResp.json()     as Promise<PublishedItem[]>,
    ])
    items.value = [...a, ...b]

    // Build the set of published ids that have a pending revision.
    const pending = new Set<string>()
    for (const resp of [reviewArtResp, reviewBlogResp]) {
      if (!resp.ok) continue
      const list = await resp.json() as { replacesArticleId?: string | null }[]
      for (const r of list) if (r.replacesArticleId) pending.add(r.replacesArticleId)
    }
    revisionPending.value = pending
  } catch (err) {
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

// ── Edit (create a revision and open it in the editor dialog) ─────────────────
const editDraftId  = ref<string | null>(null)
const editDialogRef = ref<InstanceType<typeof DraftEditor> | null>(null)

async function edit(item: PublishedItem) {
  busy.value = { ...busy.value, [item.id]: true }
  errors.value = { ...errors.value, [item.id]: null }
  try {
    const resp = await apiFetch(`/articles/${item.id}/edit`, { method: 'POST' })
    if (!resp.ok) {
      throw new Error(await apiErrorMessage(resp))
    }
    const draft = await resp.json() as { id: string }
    editDraftId.value = draft.id
  } catch (err) {
    errors.value = { ...errors.value, [item.id]: err instanceof Error ? err.message : String(err) }
  } finally {
    busy.value = { ...busy.value, [item.id]: false }
  }
}

// Close via the editor's Close button: an edit creates a revision draft up front,
// so if the user made no changes, drop that untouched draft instead of leaving it
// in the approvals/editor queue. If they did change something, persist it.
async function closeEdit() {
  const id     = editDraftId.value
  const editor = editDialogRef.value
  if (id && editor) {
    if (editor.isDirty()) {
      await editor.flush()
    } else {
      try { await apiFetch(`/drafts/${id}`, { method: 'DELETE' }) } catch { /* best-effort */ }
    }
  }
  editDraftId.value = null
  await load()
}

// Submit consumes the draft server-side (it becomes an in-review article), so
// just close and refresh — no draft to clean up.
async function submittedEdit() {
  editDraftId.value = null
  await load()
}

// ── Delete (admins delete immediately; others request approval) ──────────────
async function remove(item: PublishedItem) {
  const label = item.type === 'blog' ? 'blog post' : 'article'
  const prompt = auth.isAdmin
    ? `Delete "${item.title}" (${label})?\n\nThis can't be undone.`
    : `Request deletion of "${item.title}" (${label})?\n\nAn admin must approve before it is removed.`
  if (!globalThis.confirm(prompt)) return

  busy.value = { ...busy.value, [item.id]: true }
  errors.value = { ...errors.value, [item.id]: null }
  try {
    const resp = auth.isAdmin
      ? await apiFetch(`/articles/${item.id}?status=published`, { method: 'DELETE' })
      : await apiFetch(`/articles/${item.id}/request-deletion`, { method: 'POST' })
    if (!resp.ok) {
      throw new Error(await apiErrorMessage(resp))
    }
    if (auth.isAdmin) {
      items.value = items.value.filter(x => x.id !== item.id)
    } else {
      const updated = await resp.json() as PublishedItem
      const idx = items.value.findIndex(x => x.id === item.id)
      if (idx >= 0) items.value.splice(idx, 1, { ...items.value[idx], ...updated })
    }
  } catch (err) {
    errors.value = { ...errors.value, [item.id]: err instanceof Error ? err.message : String(err) }
  } finally {
    busy.value = { ...busy.value, [item.id]: false }
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-GB', {
    day: 'numeric', month: 'short', year: 'numeric',
  })
}

onMounted(load)
</script>

<template>
  <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
    <div class="flex items-center justify-between">
      <div>
        <h2 class="font-display text-xl font-bold text-slypn-700">Published content</h2>
        <p class="mt-1 text-sm text-slypn-900/75">
          <template v-if="auth.isAdmin">Everything live on /articles and /blog.</template>
          <template v-else>Your published articles and blog posts.</template>
          Edits and deletions go through approval.
        </p>
      </div>
      <button
        type="button"
        class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
        :disabled="loading"
        @click="load"
      >
        {{ loading ? 'Loading…' : 'Refresh' }}
      </button>
    </div>

    <p v-if="loadError" class="mt-4 rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      {{ loadError }}
    </p>

    <!-- Toolbar: type filter + search -->
    <div v-if="visibleItems.length > 0" class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div class="inline-flex rounded-md border border-slypn-200 bg-white p-1">
        <button
          v-for="opt in (['all', 'article', 'blog'] as const)"
          :key="opt"
          type="button"
          :class="[
            'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
            typeFilter === opt ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50',
          ]"
          @click="typeFilter = opt"
        >{{ opt === 'all' ? 'All' : opt === 'article' ? 'Articles' : 'Blogs' }}</button>
      </div>
      <div class="relative sm:w-72">
        <input
          v-model="search"
          type="search"
          aria-label="Search title or summary"
          placeholder="Search title or summary…"
          class="w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
      </div>
    </div>

    <p v-if="!loading && !loadError && !visibleItems.length" class="mt-6 text-sm text-slypn-900/65">
      Nothing published yet.
    </p>

    <p v-else-if="visibleItems.length > 0 && !filtered.length" class="mt-6 text-sm text-slypn-900/65">
      No {{ typeFilter === 'all' ? 'items' : typeFilter === 'article' ? 'articles' : 'blog posts' }} match
      <template v-if="search">“{{ search }}”</template>.
    </p>

    <ul v-if="pagedItems.length > 0" class="mt-6 space-y-3">
      <li
        v-for="item in pagedItems"
        :key="item.id"
        class="rounded-md border border-slypn-100 bg-white p-4"
      >
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span
                :class="[
                  'rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider',
                  item.type === 'blog'
                    ? 'bg-violet-100 text-violet-800'
                    : 'bg-slypn-100 text-slypn-700',
                ]"
              >
                {{ item.type === 'blog' ? 'Blog' : 'Article' }}
              </span>
              <p class="font-display text-lg font-bold text-slypn-700">{{ item.title }}</p>
              <span
                v-if="revisionPending.has(item.id)"
                class="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-amber-800"
              >Revision pending</span>
              <span
                v-if="item.deletionRequestedBy"
                class="rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-rose-700"
              >Deletion requested</span>
            </div>
            <p class="mt-1 text-xs text-slypn-900/60">
              {{ item.author }} &middot; published {{ formatDate(item.publishedAt) }}
              &middot; {{ item.category || 'uncategorised' }}
            </p>
            <p class="mt-2 text-sm text-slypn-900/85">{{ item.summary }}</p>
          </div>
          <div class="flex shrink-0 gap-2">
            <button
              type="button"
              class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-sm font-semibold text-slypn-700 hover:bg-slypn-50 disabled:opacity-50"
              :disabled="!!busy[item.id] || revisionPending.has(item.id)"
              :title="revisionPending.has(item.id) ? 'A revision is already awaiting approval' : undefined"
              @click="edit(item)"
            >Edit</button>
            <button
              type="button"
              class="rounded-md border border-rose-200 bg-white px-3 py-1.5 text-sm font-semibold text-rose-700 hover:bg-rose-50 disabled:opacity-50"
              :disabled="!!busy[item.id] || !!item.deletionRequestedBy"
              @click="remove(item)"
            >
              {{ busy[item.id] ? '…' : (auth.isAdmin ? 'Delete' : 'Request deletion') }}
            </button>
          </div>
        </div>
        <p v-if="errors[item.id]" class="mt-2 rounded-md bg-rose-50 px-3 py-1.5 text-xs text-rose-700">
          {{ errors[item.id] }}
        </p>
      </li>
    </ul>

    <!-- Pagination -->
    <div v-if="totalPages > 1" class="mt-6 flex items-center justify-between">
      <p class="text-xs text-slypn-500">
        {{ filtered.length }} item{{ filtered.length === 1 ? '' : 's' }} · page {{ page }} of {{ totalPages }}
      </p>
      <div class="flex gap-2">
        <button
          type="button"
          class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-sm font-semibold text-slypn-700 hover:bg-slypn-50 disabled:opacity-40"
          :disabled="page <= 1"
          @click="page--"
        >Previous</button>
        <button
          type="button"
          class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-sm font-semibold text-slypn-700 hover:bg-slypn-50 disabled:opacity-40"
          :disabled="page >= totalPages"
          @click="page++"
        >Next</button>
      </div>
    </div>
  </article>

  <!-- Edit dialog — reuses the editor control -->
  <Teleport to="body">
    <div
      v-if="editDraftId"
      class="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slypn-900/40 p-4"
    >
      <div class="my-8 w-full max-w-3xl rounded-xl bg-white p-6 shadow-xl">
        <div class="mb-4 flex items-center justify-between">
          <h3 class="font-display text-lg font-bold text-slypn-700">Edit published content</h3>
          <p class="text-xs text-slypn-500">Submitting sends a revision for approval.</p>
        </div>
        <DraftEditor
          ref="editDialogRef"
          :draft-id="editDraftId"
          @close="closeEdit"
          @submitted="submittedEdit"
        />
      </div>
    </div>
  </Teleport>
</template>
