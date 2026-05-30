<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ArticleCard from '@/components/common/ArticleCard.vue'
import { mockArticles } from '@/mock/articles'
import type { ArticleCategory } from '@/types/content'

const categories: Array<'All' | ArticleCategory> = [
  'All',
  "Living with Parkinson's",
  'Treatment',
  'Community',
  'Lifestyle',
]
const selected = ref<typeof categories[number]>('All')

const visible = computed(() => {
  const sorted = [...mockArticles].sort(
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

    <div class="mt-8 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
      <ArticleCard v-for="article in visible" :key="article.id" :article="article" />
    </div>

    <p v-if="!visible.length" class="mt-12 text-center text-slypn-900/70">
      No articles in this category yet.
    </p>
  </section>
</template>
