<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import HeroBanner from '@/components/common/HeroBanner.vue'
import EventCard from '@/components/common/EventCard.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { CommunityEvent } from '@/types/content'

const PAGE_SIZE = 10
const page      = ref(1)
const router    = useRouter()

const { data: events, loading, error, refresh } = useAsyncData(
  () => apiJson<CommunityEvent[]>('/events'),
)

const startOfToday = (() => {
  const d = new Date()
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
})()

const sevenDaysAgo = new Date(startOfToday.getTime() - 7 * 24 * 60 * 60 * 1000)

const previous = computed(() =>
  [...(events.value ?? [])]
    .filter(e => new Date(e.startsAt) < sevenDaysAgo)
    .sort((a, b) => +new Date(b.startsAt) - +new Date(a.startsAt)),
)

const pageCount = computed(() => Math.ceil(previous.value.length / PAGE_SIZE))
const paged     = computed(() =>
  previous.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE),
)
</script>

<template>
  <HeroBanner
    eyebrow="Events"
    title="Previous events"
    subtitle="A record of past SLYPN meet-ups and activities."
  >
    <template #actions>
      <button
        type="button"
        class="flex items-center gap-1.5 text-sm font-semibold text-white/80 hover:text-white"
        @click="router.push({ name: 'events' })"
      >
        &larr; Upcoming events
      </button>
    </template>
  </HeroBanner>

  <section class="mx-auto w-full max-w-4xl px-6 py-16">
    <p v-if="loading && !events" class="text-center text-slypn-900/70">
      Loading events&hellip;
    </p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load events: {{ error }}.
      <button class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <template v-else>
      <div class="space-y-4">
        <EventCard
          v-for="event in paged"
          :key="event.id"
          :event="event"
          :past="true"
        />
        <p v-if="!previous.length" class="text-center text-slypn-900/70">
          No previous events found.
        </p>
      </div>

      <div v-if="pageCount > 1" class="mt-8 flex items-center justify-center gap-3">
        <button
          type="button"
          class="rounded-md border border-slypn-200 px-4 py-1.5 text-sm text-slypn-700 hover:bg-slypn-50 disabled:opacity-40"
          :disabled="page === 1"
          @click="page--"
        >
          &larr; Newer
        </button>
        <span class="text-sm text-slypn-700">{{ page }} / {{ pageCount }}</span>
        <button
          type="button"
          class="rounded-md border border-slypn-200 px-4 py-1.5 text-sm text-slypn-700 hover:bg-slypn-50 disabled:opacity-40"
          :disabled="page === pageCount"
          @click="page++"
        >
          Older &rarr;
        </button>
      </div>
    </template>
  </section>
</template>
