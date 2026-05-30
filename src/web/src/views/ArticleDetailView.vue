<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { mockArticles } from '@/mock/articles'

const route = useRoute()
const article = computed(() =>
  mockArticles.find(a => a.slug === route.params.slug),
)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })
</script>

<template>
  <article v-if="article" class="mx-auto max-w-3xl px-6 py-16">
    <RouterLink to="/articles" class="text-sm text-slypn-600 hover:text-slypn-700">
      &larr; All articles
    </RouterLink>
    <p class="mt-6 font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
      {{ article.category }}
    </p>
    <h1 class="mt-2 text-4xl font-extrabold text-slypn-700 sm:text-5xl">
      {{ article.title }}
    </h1>
    <p class="mt-4 text-slypn-900/65">
      {{ article.author }} &middot; {{ formatDate(article.publishedAt) }} &middot; {{ article.readingMinutes }} min read
    </p>
    <p class="mt-6 text-xl text-slypn-900/85">{{ article.summary }}</p>

    <div class="prose mt-8 max-w-none text-slypn-900/85">
      <p v-for="(para, i) in article.body.split('\n\n')" :key="i" class="mt-5 leading-relaxed">
        {{ para }}
      </p>
    </div>

    <ul class="mt-12 flex flex-wrap gap-2">
      <li
        v-for="tag in article.tags"
        :key="tag"
        class="rounded-full bg-slypn-50 px-3 py-1 text-xs font-medium text-slypn-700"
      >
        #{{ tag }}
      </li>
    </ul>
  </article>

  <section v-else class="mx-auto max-w-3xl px-6 py-20 text-center">
    <h1 class="font-display text-3xl font-bold text-slypn-700">Article not found</h1>
    <RouterLink to="/articles" class="mt-6 inline-block text-slypn-600 underline underline-offset-4 hover:text-slypn-700">
      Back to articles
    </RouterLink>
  </section>
</template>
