<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ArticleCard from '@/components/common/ArticleCard.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article, ArticleCategory } from '@/types/content'

const categories: Array<'All' | ArticleCategory> = [
  'All',
  "Living with Parkinson's",
  'Treatment',
  'Community',
  'Lifestyle',
]
const selected = ref<typeof categories[number]>('All')

const { data: articles, loading, error, refresh } = useAsyncData(
  () => apiJson<Article[]>('/articles?status=published'),
)

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
  />

  <section class="mx-auto max-w-6xl px-6 py-16">
    <div class="flex flex-wrap gap-2">
      <button
        v-for="cat in categories"
        :key="cat"
        type="button"
        :class="[
          'rounded-full border px-4 py-1.5 text-sm font-medium transition-colors',
          selected === cat
            ? 'border-slypn-600 bg-slypn-600 text-white'
            : 'border-slypn-200 bg-white text-slypn-700 hover:bg-slypn-50',
        ]"
        @click="selected = cat"
      >
        {{ cat }}
      </button>
    </div>

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
