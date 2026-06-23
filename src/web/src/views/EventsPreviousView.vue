<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import HeroBanner from '@/components/common/HeroBanner.vue'
import EventCard from '@/components/common/EventCard.vue'
import PillFilter from '@/components/common/PillFilter.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { CommunityEvent } from '@/types/content'

const PAGE   = 10
const router = useRouter()

const { data: events, loading, error, refresh } = useAsyncData(
  () => apiJson<CommunityEvent[]>('/events'),
)

const startOfToday = (() => {
  const d = new Date()
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
})()

const sevenDaysAgo = new Date(startOfToday.getTime() - 7 * 24 * 60 * 60 * 1000)

// ── Type filter — single select (radio) ──────────────────────────────────────
const selectedType = ref('All')
const eventTypes = computed(() => {
  const set = new Set<string>()
  for (const e of events.value ?? []) if (e.type) set.add(e.type)
  return [...set].sort((a, b) => a.localeCompare(b))
})

const previous = computed(() =>
  [...(events.value ?? [])]
    .filter(e => new Date(e.startsAt) < sevenDaysAgo)
    .filter(e => selectedType.value === 'All' || e.type === selectedType.value)
    .sort((a, b) => +new Date(b.startsAt) - +new Date(a.startsAt)),
)

// ── Infinite scroll ──────────────────────────────────────────────────────────
const visibleCount   = ref(PAGE)
const visibleEvents  = computed(() => previous.value.slice(0, visibleCount.value))
const hasMore        = computed(() => visibleCount.value < previous.value.length)

function loadMore() {
  if (hasMore.value) visibleCount.value = Math.min(previous.value.length, visibleCount.value + PAGE)
}

// Reset when the list changes / loads.
watch(previous, () => { visibleCount.value = PAGE })

const sentinel = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | null = null

onMounted(() => {
  observer = new IntersectionObserver(
    (entries) => { if (entries[0].isIntersecting) loadMore() },
    { rootMargin: '300px' },
  )
  if (sentinel.value) observer.observe(sentinel.value)
})

watch(sentinel, (el, old) => {
  if (old) observer?.unobserve(old)
  if (el) observer?.observe(el)
})

onBeforeUnmount(() => observer?.disconnect())
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

  <section class="page-container py-16">
    <p v-if="loading && !events" class="text-center text-slypn-900/70">
      Loading events&hellip;
    </p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load events: {{ error }}.
      <button class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <template v-else>
      <!-- Type filter -->
      <PillFilter v-model="selectedType" :options="eventTypes" class="mb-6" />

      <RouterLink
        :to="{ name: 'events' }"
        class="mb-8 inline-flex items-center gap-1.5 text-sm text-slypn-500 hover:text-slypn-700"
      >
        &larr; Back to events
      </RouterLink>

      <div class="space-y-4">
        <EventCard
          v-for="event in visibleEvents"
          :key="event.id"
          :event="event"
          :past="true"
        />
        <p v-if="!previous.length" class="text-center text-slypn-900/70">
          No previous events found.
        </p>
      </div>

      <!-- Infinite-scroll sentinel -->
      <div v-if="hasMore" ref="sentinel" class="py-8 text-center text-sm text-slypn-900/50">
        Loading more events…
      </div>
    </template>
  </section>
</template>
