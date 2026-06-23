<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import HeroBanner from '@/components/common/HeroBanner.vue'
import logoUrl from '@/assets/logo.svg'
import ArticleCard from '@/components/common/ArticleCard.vue'
import EventCard from '@/components/common/EventCard.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article, CommunityEvent } from '@/types/content'

const { data: articles } = useAsyncData(
  () => apiJson<Article[]>('/articles?status=published'),
)
const { data: blogs } = useAsyncData(
  () => apiJson<Article[]>('/blog?status=published'),
)
const { data: events } = useAsyncData(
  () => apiJson<CommunityEvent[]>('/events?upcoming=true'),
)

const featuredArticles = computed(() =>
  [...(articles.value ?? [])]
    .sort((a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt))
    .slice(0, 3),
)

const featuredBlogs = computed(() =>
  [...(blogs.value ?? [])]
    .sort((a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt))
    .slice(0, 3),
)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

const upcomingEvents = computed(() =>
  [...(events.value ?? [])]
    .sort((a, b) => +new Date(a.startsAt) - +new Date(b.startsAt))
    .slice(0, 3),
)
</script>

<template>
  <HeroBanner
    eyebrow="South London"
    title="Younger Parkinson's Network"
    subtitle="A community for working-age people living with Parkinson's in South London — coffee meet-ups, drinks, activities, and fundraising events. Affiliated with Parkinson's UK."
  >
    <template #brand>
      <img
        :src="logoUrl"
        alt="SLYPN — South London Younger Parkinson's Network"
        class="h-48 w-auto sm:h-60 md:h-72"
        width="684"
        height="488"
      />
    </template>

    <template #actions>
      <RouterLink
        to="/about"
        class="rounded-md bg-slypn-600 px-5 py-3 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
      >
        About SLYPN
      </RouterLink>
      <RouterLink
        to="/events"
        class="rounded-md border border-slypn-200 bg-white px-5 py-3 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
      >
        Upcoming events
      </RouterLink>
    </template>
  </HeroBanner>

  <section v-if="featuredArticles.length" class="page-container py-16">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-slypn-500">Latest</p>
        <h2 class="mt-2 text-3xl font-bold text-slypn-700">From our members</h2>
      </div>
      <RouterLink to="/articles" class="text-sm font-medium text-slypn-600 hover:text-slypn-700">
        All articles &rarr;
      </RouterLink>
    </div>
    <div class="mt-8 grid gap-6 md:grid-cols-3">
      <ArticleCard v-for="a in featuredArticles" :key="a.id" :article="a" />
    </div>
  </section>

  <section v-if="featuredBlogs.length" class="page-container py-16">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-slypn-500">Latest</p>
        <h2 class="mt-2 text-3xl font-bold text-slypn-700">From the blog</h2>
      </div>
      <RouterLink to="/blog" class="text-sm font-medium text-slypn-600 hover:text-slypn-700">
        Read the blog &rarr;
      </RouterLink>
    </div>
    <div class="mt-8 grid gap-6 md:grid-cols-3">
      <RouterLink
        v-for="post in featuredBlogs"
        :key="post.id"
        :to="{ path: '/blog', hash: `#post-${post.id}` }"
        class="flex flex-col rounded-xl border border-slypn-100 bg-white p-6 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="text-xs text-slypn-900/60">{{ formatDate(post.publishedAt) }} &middot; {{ post.author }}</p>
        <h3 class="mt-2 text-xl font-bold text-slypn-700">{{ post.title }}</h3>
        <p class="mt-3 flex-1 text-sm text-slypn-900/75">{{ post.summary }}</p>
      </RouterLink>
    </div>
  </section>

  <section v-if="upcomingEvents.length" class="page-container py-16">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-slypn-500">Coming up</p>
        <h2 class="mt-2 text-3xl font-bold text-slypn-700">Upcoming events</h2>
      </div>
      <RouterLink to="/events" class="text-sm font-medium text-slypn-600 hover:text-slypn-700">
        All events &rarr;
      </RouterLink>
    </div>
    <div class="mt-8 space-y-4">
      <EventCard v-for="e in upcomingEvents" :key="e.id" :event="e" />
    </div>
  </section>

  <section class="border-t border-slypn-100 bg-white">
    <div class="page-container-prose py-16 text-center">
      <h2 class="font-display text-3xl font-bold text-slypn-700">Get the monthly newsletter</h2>
      <p class="mt-3 text-slypn-900/80">
        Meet-up dates, a featured article, fundraising progress, and the odd member story. About five minutes to read.
      </p>
      <RouterLink
        to="/newsletter"
        class="mt-6 inline-block rounded-md bg-slypn-600 px-6 py-3 text-sm font-semibold text-white hover:bg-slypn-700"
      >
        Subscribe
      </RouterLink>
    </div>
  </section>
</template>
