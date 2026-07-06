<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ArticlesIcon from '@/components/common/ArticlesIcon.vue'
import ArticleCard from '@/components/common/ArticleCard.vue'
import PillFilter from '@/components/common/PillFilter.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article } from '@/types/content'

// Named so <keep-alive :include> in App.vue caches this view, preserving the
// category filter and scroll position across the article-detail round-trip.
defineOptions({ name: 'ArticlesView' })

const selected = ref('All')

const { data: articles, loading, error, refresh } = useAsyncData(
  () => apiJson<Article[]>('/articles?status=published'),
)

const categories = computed(() => {
  const set = new Set<string>()
  for (const a of articles.value ?? []) if (a.category) set.add(a.category)
  return [...set].sort((a, b) => a.localeCompare(b))
})

const visible = computed(() => {
  const list = articles.value ?? []
  const sorted = [...list].sort(
    (a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt),
  )
  return selected.value === 'All'
    ? sorted
    : sorted.filter(a => a.category === selected.value)
})
</script>

<template>
  <HeroBanner
    eyebrow="Articles"
    title="Considered pieces, written by members"
    subtitle="Longer-form writing from the SLYPN community on living with Parkinson's, navigating treatment, and the small daily things that make a difference."
  >
    <template #brand>
      <ArticlesIcon class="w-56 text-slypn-700 sm:w-64 md:w-72" />
    </template>
  </HeroBanner>

  <section class="page-container py-16">
    <PillFilter v-model="selected" :options="categories" />

    <p v-if="loading && !articles" class="mt-12 text-center text-slypn-900/70">
      Loading articles&hellip;
    </p>

    <div v-else-if="error" class="mt-12 rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load articles: {{ error }}.
      <button class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <div v-else class="mt-8 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
      <ArticleCard v-for="article in visible" :key="article.id" :article="article" />
    </div>

    <p v-if="!loading && !error && !visible.length" class="mt-12 text-center text-slypn-900/70">
      No articles in this category yet.
    </p>
  </section>
</template>
