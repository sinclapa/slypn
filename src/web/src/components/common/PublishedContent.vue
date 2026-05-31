<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiFetch } from '@/lib/api'

interface PublishedItem {
  id: string
  slug: string
  title: string
  summary: string
  author: string
  publishedAt: string
  category: string
  type?: 'article' | 'blog'
  status: string
}

const items     = ref<PublishedItem[]>([])
const loading   = ref(false)
const loadError = ref<string | null>(null)
const busy      = ref<Record<string, boolean>>({})
const errors    = ref<Record<string, string | null>>({})

const sorted = computed(() =>
  [...items.value].sort(
    (a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt),
  ),
)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const [articlesResp, blogResp] = await Promise.all([
      apiFetch('/articles?status=published'),
      apiFetch('/blog?status=published'),
    ])
    if (!articlesResp.ok) throw new Error(`/articles: ${articlesResp.status} ${articlesResp.statusText}`)
    if (!blogResp.ok)     throw new Error(`/blog: ${blogResp.status} ${blogResp.statusText}`)
    const [a, b] = await Promise.all([
      articlesResp.json() as Promise<PublishedItem[]>,
      blogResp.json()     as Promise<PublishedItem[]>,
    ])
    items.value = [...a, ...b]
  } catch (err) {
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

async function remove(item: PublishedItem) {
  const label = item.type === 'blog' ? 'blog post' : 'article'
  if (!window.confirm(`Delete "${item.title}" (${label})?\n\nThis can't be undone.`)) return
  busy.value = { ...busy.value, [item.id]: true }
  errors.value = { ...errors.value, [item.id]: null }
  try {
    const resp = await apiFetch(
      `/articles/${item.id}?status=published`,
      { method: 'DELETE' },
    )
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    items.value = items.value.filter(x => x.id !== item.id)
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
          Everything live on /articles and /blog. Delete removes from Cosmos &mdash; can&rsquo;t be undone.
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

    <p v-if="!loading && !loadError && !items.length" class="mt-6 text-sm text-slypn-900/65">
      Nothing published yet.
    </p>

    <ul v-if="sorted.length > 0" class="mt-6 space-y-3">
      <li
        v-for="item in sorted"
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
            </div>
            <p class="mt-1 text-xs text-slypn-900/60">
              {{ item.author }} &middot; published {{ formatDate(item.publishedAt) }}
              &middot; {{ item.category || 'uncategorised' }}
            </p>
            <p class="mt-2 text-sm text-slypn-900/85">{{ item.summary }}</p>
          </div>
          <button
            type="button"
            class="rounded-md border border-rose-200 bg-white px-3 py-1.5 text-sm font-semibold text-rose-700 hover:bg-rose-50 disabled:opacity-50"
            :disabled="!!busy[item.id]"
            @click="remove(item)"
          >
            {{ busy[item.id] ? '…' : 'Delete' }}
          </button>
        </div>
        <p v-if="errors[item.id]" class="mt-2 rounded-md bg-rose-50 px-3 py-1.5 text-xs text-rose-700">
          {{ errors[item.id] }}
        </p>
      </li>
    </ul>
  </article>
</template>
