<script setup lang="ts">
import { computed } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article } from '@/types/content'

const { data: posts, loading, error, refresh } = useAsyncData(
  () => apiJson<Article[]>('/blog'),
)

const sorted = computed(() =>
  [...(posts.value ?? [])].sort(
    (a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt),
  ),
)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })
</script>

<template>
  <HeroBanner
    eyebrow="Blog"
    title="Shorter, more frequent updates"
    subtitle="Meet-up recaps, thank-yous, member news. If you want to write something for the blog, mention it at the next meet-up."
  />

  <section class="mx-auto max-w-3xl px-6 py-16">
    <p v-if="loading && !posts" class="text-center text-slypn-900/70">Loading&hellip;</p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load the blog: {{ error }}.
      <button class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <p v-else-if="!sorted.length" class="text-center text-slypn-900/70">
      No posts yet. New entries show up here as members write them.
    </p>

    <ol v-else class="space-y-12">
      <li v-for="post in sorted" :key="post.id" class="border-b border-slypn-100 pb-12 last:border-b-0">
        <p class="text-xs text-slypn-900/60">{{ formatDate(post.publishedAt) }} &middot; {{ post.author }}</p>
        <h2 class="mt-2 text-2xl font-bold text-slypn-700">{{ post.title }}</h2>
        <p class="mt-3 text-slypn-900/85">{{ post.summary }}</p>
        <div class="prose prose-slypn mt-4 max-w-none text-slypn-900/80" v-html="post.body" />
      </li>
    </ol>
  </section>
</template>
