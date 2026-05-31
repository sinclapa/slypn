<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiFetch } from '@/lib/api'

interface PendingArticle {
  id: string
  slug: string
  title: string
  summary: string
  body: string
  author: string
  authorId?: string
  publishedAt: string  // submission time while status=in-review
  category: string
  tags: string[]
  status: string
  readingMinutes: number
  type?: 'article' | 'blog'  // missing on legacy rows; treat as 'article'
}

const articles  = ref<PendingArticle[]>([])
const loading   = ref(false)
const loadError = ref<string | null>(null)
const expanded  = ref<Record<string, boolean>>({})
const busy      = ref<Record<string, 'publishing' | 'rejecting' | null>>({})
const errors    = ref<Record<string, string | null>>({})

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
    // /api/articles is type-filtered to "article", so blog submissions never
    // appear there. Pull both endpoints in parallel and merge.
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
  } catch (err) {
    errors.value = { ...errors.value, [article.id]: err instanceof Error ? err.message : String(err) }
  } finally {
    busy.value = { ...busy.value, [article.id]: null }
  }
}

async function reject(article: PendingArticle) {
  const feedback = window.prompt(`Why is "${article.title}" being rejected? (5-1000 characters)`, '')
  if (!feedback || feedback.trim().length < 5) {
    if (feedback !== null) {
      errors.value = { ...errors.value, [article.id]: 'Feedback must be at least 5 characters.' }
    }
    return
  }
  busy.value = { ...busy.value, [article.id]: 'rejecting' }
  errors.value = { ...errors.value, [article.id]: null }
  try {
    const resp = await apiFetch(`/articles/${article.id}/reject`, {
      method: 'POST',
      body: JSON.stringify({ feedback: feedback.trim() }),
    })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    articles.value = articles.value.filter(a => a.id !== article.id)
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
          Articles submitted by contributors, awaiting publish or reject.
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
                  <span
                    :class="[
                      'rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider',
                      article.type === 'blog'
                        ? 'bg-violet-100 text-violet-800'
                        : 'bg-slypn-100 text-slypn-700',
                    ]"
                  >
                    {{ article.type === 'blog' ? 'Blog' : 'Article' }}
                  </span>
                  <button
                    type="button"
                    class="text-left font-display text-lg font-bold text-slypn-700 hover:text-slypn-600"
                    @click="toggle(article.id)"
                  >
                    {{ article.title || '(untitled)' }}
                  </button>
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
                >
                  {{ busy[article.id] === 'publishing' ? '…' : 'Publish' }}
                </button>
                <button
                  type="button"
                  class="rounded-md border border-rose-200 bg-white px-3 py-1.5 text-sm font-semibold text-rose-700 hover:bg-rose-50 disabled:opacity-50"
                  :disabled="busy[article.id] !== null && busy[article.id] !== undefined"
                  @click="reject(article)"
                >
                  {{ busy[article.id] === 'rejecting' ? '…' : 'Reject' }}
                </button>
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
</template>
