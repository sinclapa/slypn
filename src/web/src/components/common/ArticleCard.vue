<script setup lang="ts">
import { RouterLink } from 'vue-router'
import type { Article } from '@/types/content'

defineProps<{ article: Article }>()

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
</script>

<template>
  <article class="flex flex-col rounded-xl border border-slypn-100 bg-white p-6 shadow-sm transition-shadow hover:shadow-md">
    <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
      {{ article.category }}
    </p>
    <h3 class="mt-2 text-xl font-bold text-slypn-700">
      <RouterLink :to="`/articles/${article.slug || article.id}`" class="hover:text-slypn-600">
        {{ article.title }}
      </RouterLink>
    </h3>
    <p class="mt-3 flex-1 text-sm text-slypn-900/75">{{ article.summary }}</p>
    <div class="mt-4 flex items-center justify-between text-xs text-slypn-900/60">
      <span>{{ article.author }}</span>
      <span>{{ formatDate(article.publishedAt) }} &middot; {{ article.readingMinutes }} min read</span>
    </div>
  </article>
</template>
