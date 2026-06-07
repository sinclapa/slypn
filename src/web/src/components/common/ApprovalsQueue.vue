<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiFetch } from '@/lib/api'
import { useApprovalsStore } from '@/stores/approvals'

interface PendingArticle {
  id: string
  slug: string
  title: string
  summary: string
  body: string
  author: string
  authorId?: string
  publishedAt: string
  category: string
  tags: string[]
  status: string
  readingMinutes: number
  type?: 'article' | 'blog'
}

const approvalsStore = useApprovalsStore()

const articles  = ref<PendingArticle[]>([])
const loading   = ref(false)
const loadError = ref<string | null>(null)
const expanded  = ref<Record<string, boolean>>({})
const busy      = ref<Record<string, 'publishing' | 'revising' | null>>({})
const errors    = ref<Record<string, string | null>>({})

const reviseDialog = ref<{ show: boolean; article: PendingArticle | null; feedback: string }>({
  show: false, article: null, feedback: '',
})

const groupedByAuthor = computed(() => {
  const map = new Map<string, PendingArticle[]>()
  for (const a of articles.value) {
    const key = a.author || 'Unknown'
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push(a)
  }
  return Array.from(map.entries()).map(([author, items]) => ({ author, items }))
})

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const [articlesResp, blogResp] = await Promise.all([
      apiFetch('/articles?status=in-review'),
      apiFetch('/blog?status=in-review'),
    ])
    if (!articlesResp.ok) throw new Error(`/articles: ${articlesResp.status} ${articlesResp.statusText}`)
    if (!blogResp.ok)     throw new Error(`/blog: ${blogResp.status} ${blogResp.statusText}`)
    const [a, b] = await Promise.all([
      articlesResp.json() as Promise<PendingArticle[]>,
      blogResp.json()     as Promise<PendingArticle[]>,
    ])
    articles.value = [...a, ...b].sort(
      (x, y) => +new Date(y.publishedAt) - +new Date(x.publishedAt),
    )
    approvalsStore.pendingCount = articles.value.length
  } catch (err) {
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

async function publish(article: PendingArticle) {
  busy.value = { ...busy.value, [article.id]: 'publishing' }
  errors.value = { ...errors.value, [article.id]: null }
  try {
    const resp = await apiFetch(`/articles/${article.id}/publish`, { method: 'POST' })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    articles.value = articles.value.filter(a => a.id !== article.id)
    approvalsStore.pendingCount = articles.value.length
  } catch (err) {
    errors.value = { ...errors.value, [article.id]: err instanceof Error ? err.message : String(err) }
  } finally {
    busy.value = { ...busy.value, [article.id]: null }
  }
}

function openRevise(article: PendingArticle) {
  reviseDialog.value = { show: true, article, feedback: '' }
}

async function confirmRevise() {
  const { article, feedback } = reviseDialog.value
  if (!article) return
  if (feedback.trim().length < 5) {
    errors.value = { ...errors.value, [article.id]: 'Feedback must be at least 5 characters.' }
    reviseDialog.value.show = false
    return
  }
  reviseDialog.value.show = false
  busy.value = { ...busy.value, [article.id]: 'revising' }
  errors.value = { ...errors.value, [article.id]: null }
  try {
    const resp = await apiFetch(`/articles/${article.id}/revise`, {
      method: 'POST',
      body: JSON.stringify({ feedback: feedback.trim() }),
    })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    articles.value = articles.value.filter(a => a.id !== article.id)
    approvalsStore.pendingCount = articles.value.length
  } catch (err) {
    errors.value = { ...errors.value, [article.id]: err instanceof Error ? err.message : String(err) }
  } finally {
    busy.value = { ...busy.value, [article.id]: null }
  }
}

function toggle(id: string) {
  expanded.value = { ...expanded.value, [id]: !expanded.value[id] }
}

function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString('en-GB', {
    day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

onMounted(load)
</script>

<template>
  <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
    <div class="flex items-center justify-between">
      <div>
        <h2 class="font-display text-xl font-bold text-slypn-700">Approvals</h2>
        <p class="mt-1 text-sm text-slypn-900/75">
          Articles submitted by contributors, awaiting publish or revision.
        </p>
      </div>
      <button
        type="button"
        class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
        :disabled="loading"
        @click="load"
      >{{ loading ? 'Loading…' : 'Refresh' }}</button>
    </div>

    <p v-if="loadError" class="mt-4 rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      {{ loadError }}
    </p>

    <p v-if="!loading && !loadError && articles.length === 0" class="mt-6 text-sm text-slypn-900/65">
      No submissions waiting. Contributors&rsquo; new submissions will land here.
    </p>

    <ol v-if="articles.length > 0" class="mt-6 space-y-6">
      <li
        v-for="group in groupedByAuthor"
        :key="group.author"
        class="rounded-lg border border-slypn-100 bg-slypn-50/40 p-4"
      >
        <p class="font-display text-sm font-semibold uppercase tracking-widest text-slypn-500">
          {{ group.author }}
        </p>
        <ul class="mt-3 space-y-3">
          <li
            v-for="article in group.items"
            :key="article.id"
            class="rounded-md border border-slypn-100 bg-white p-4"
          >
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0 flex-1">
                <div class="flex flex-wrap items-center gap-2">
                  <span :class="[
                    'rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider',
                    article.type === 'blog' ? 'bg-violet-100 text-violet-800' : 'bg-slypn-100 text-slypn-700',
                  ]">{{ article.type === 'blog' ? 'Blog' : 'Article' }}</span>
                  <button
                    type="button"
                    class="text-left font-display text-lg font-bold text-slypn-700 hover:text-slypn-600"
                    @click="toggle(article.id)"
                  >{{ article.title || '(untitled)' }}</button>
                </div>
                <p class="mt-1 text-xs text-slypn-900/60">
                  Submitted {{ formatDateTime(article.publishedAt) }}
                  &middot; {{ article.category || 'uncategorised' }}
                  &middot; {{ article.readingMinutes }} min read
                </p>
                <p class="mt-2 text-sm text-slypn-900/85">{{ article.summary }}</p>
              </div>
              <div class="flex shrink-0 gap-2">
                <button
                  type="button"
                  class="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-emerald-700 disabled:opacity-50"
                  :disabled="busy[article.id] !== null && busy[article.id] !== undefined"
                  @click="publish(article)"
                >{{ busy[article.id] === 'publishing' ? '…' : 'Approve' }}</button>
                <button
                  type="button"
                  class="rounded-md border border-amber-300 bg-white px-3 py-1.5 text-sm font-semibold text-amber-700 hover:bg-amber-50 disabled:opacity-50"
                  :disabled="busy[article.id] !== null && busy[article.id] !== undefined"
                  @click="openRevise(article)"
                >{{ busy[article.id] === 'revising' ? '…' : 'Revise' }}</button>
              </div>
            </div>

            <div
              v-if="expanded[article.id]"
              class="prose prose-slypn mt-4 max-w-none border-t border-slypn-100 pt-4 text-sm"
              v-html="article.body"
            />

            <p v-if="errors[article.id]" class="mt-3 rounded-md bg-rose-50 px-3 py-1.5 text-xs text-rose-700">
              {{ errors[article.id] }}
            </p>
          </li>
        </ul>
      </li>
    </ol>
  </article>

  <!-- Revise dialog -->
  <Teleport to="body">
    <div
      v-if="reviseDialog.show"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      @mousedown.self="reviseDialog.show = false"
    >
      <div class="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h3 class="font-display font-semibold text-slypn-700">Request revision</h3>
        <p class="mt-1 text-sm text-slypn-900/70">
          <strong>{{ reviseDialog.article?.title }}</strong> will be sent back to the author as a draft with your feedback.
        </p>
        <div class="mt-4">
          <label class="block text-sm font-medium text-slypn-800">Feedback for the author</label>
          <textarea
            v-model="reviseDialog.feedback"
            rows="4"
            maxlength="1000"
            placeholder="Explain what needs to change (min 5 characters)…"
            class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            @keydown.esc.prevent="reviseDialog.show = false"
          />
          <p class="mt-1 text-right text-xs text-slypn-400">{{ reviseDialog.feedback.length }}/1000</p>
        </div>
        <div class="mt-5 flex justify-end gap-2">
          <button
            type="button"
            class="rounded-md px-3 py-1.5 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
            @click="reviseDialog.show = false"
          >Cancel</button>
          <button
            type="button"
            :disabled="reviseDialog.feedback.trim().length < 5"
            class="rounded-md bg-amber-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-amber-700 disabled:opacity-50"
            @click="confirmRevise"
          >Send back for revision</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
