<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
import { mockBlogPosts } from '@/mock/blog'

const sorted = [...mockBlogPosts].sort(
  (a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt),
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
    <ol class="space-y-12">
      <li v-for="post in sorted" :key="post.id" class="border-b border-slypn-100 pb-12 last:border-b-0">
        <p class="text-xs text-slypn-900/60">{{ formatDate(post.publishedAt) }} &middot; {{ post.author }}</p>
        <h2 class="mt-2 text-2xl font-bold text-slypn-700">{{ post.title }}</h2>
        <p class="mt-3 text-slypn-900/85">{{ post.excerpt }}</p>
        <p class="mt-4 whitespace-pre-line text-slypn-900/80">{{ post.body }}</p>
      </li>
    </ol>
  </section>
</template>
