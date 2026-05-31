<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ArticleCard from '@/components/common/ArticleCard.vue'
import EventCard from '@/components/common/EventCard.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article, CommunityEvent } from '@/types/content'

const { data: articles } = useAsyncData(
  () => apiJson<Article[]>('/articles?status=published'),
)
const { data: events } = useAsyncData(
  () => apiJson<CommunityEvent[]>('/events?upcoming=true'),
)

const featuredArticles = computed(() =>
  [...(articles.value ?? [])]
    .sort((a, b) => +new Date(b.publishedAt) - +new Date(a.publishedAt))
    .slice(0, 3),
)

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

  <section v-if="featuredArticles.length" class="mx-auto max-w-6xl px-6 py-16">
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

  <section v-if="upcomingEvents.length" class="mx-auto max-w-4xl px-6 py-16">
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
    <div class="mx-auto max-w-3xl px-6 py-16 text-center">
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
