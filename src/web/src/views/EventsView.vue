<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import EventCard from '@/components/common/EventCard.vue'
import EventCalendar from '@/components/common/EventCalendar.vue'
import PillFilter from '@/components/common/PillFilter.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { CommunityEvent } from '@/types/content'

const view = ref<'list' | 'calendar'>('list')

const { data: events, loading, error, refresh } = useAsyncData(
  () => apiJson<CommunityEvent[]>('/events'),
)

const startOfToday = (() => {
  const d = new Date()
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
})()

const sevenDaysAgo = new Date(startOfToday.getTime() - 7 * 24 * 60 * 60 * 1000)

// Event has fully ended before today's midnight
const isPast = (e: CommunityEvent) => new Date(e.endsAt) < startOfToday

// ── Type filter — single select (radio) ──────────────────────────────────────
const selectedType = ref('All')
const eventTypes = computed(() => {
  const set = new Set<string>()
  for (const e of events.value ?? []) if (e.type) set.add(e.type)
  return [...set].sort((a, b) => a.localeCompare(b))
})
const typeFiltered = computed(() =>
  selectedType.value === 'All'
    ? (events.value ?? [])
    : (events.value ?? []).filter(e => e.type === selectedType.value),
)

// Main window: events that started within the last 7 days or in the future, ascending
const windowEvents = computed(() =>
  [...typeFiltered.value]
    .filter(e => new Date(e.startsAt) >= sevenDaysAgo)
    .sort((a, b) => +new Date(a.startsAt) - +new Date(b.startsAt)),
)

// Count of events older than the 7-day window (for the link label)
const previousCount = computed(() =>
  typeFiltered.value.filter(e => new Date(e.startsAt) < sevenDaysAgo).length,
)

// ── Infinite scroll: show the next 3 months first, then reveal more ───────────
const PAGE = 6
const threeMonthsOut = new Date(startOfToday.getFullYear(), startOfToday.getMonth() + 3, startOfToday.getDate())

const visibleCount = ref(0)
const visibleEvents = computed(() => windowEvents.value.slice(0, visibleCount.value))
const hasMore = computed(() => visibleCount.value < windowEvents.value.length)

// Initialise (and re-clamp) once events load: start with everything in the next
// 3 months, falling back to one page if nothing falls in that window.
watch(windowEvents, (list) => {
  const inThreeMonths = list.filter(e => new Date(e.startsAt) < threeMonthsOut).length
  visibleCount.value = Math.min(list.length, inThreeMonths || PAGE)
}, { immediate: true })

function loadMore() {
  if (hasMore.value) visibleCount.value = Math.min(windowEvents.value.length, visibleCount.value + PAGE)
}

const sentinel = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | null = null

onMounted(() => {
  observer = new IntersectionObserver(
    (entries) => { if (entries[0].isIntersecting) loadMore() },
    { rootMargin: '300px' },
  )
  if (sentinel.value) observer.observe(sentinel.value)
})

// Re-observe when the sentinel element appears/disappears (view toggle, more loaded).
watch(sentinel, (el, old) => {
  if (old) observer?.unobserve(old)
  if (el) observer?.observe(el)
})

onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <HeroBanner
    eyebrow="Events"
    title="Coffee meet-ups, drinks, Q&amp;As, and activities"
    subtitle="Most of our events are weekend or evening meet-ups around South London. Drop-in, no booking — just turn up. Partners and carers welcome. Some are limited capacity so please check."
  >
    <template #actions>
      <div class="inline-flex rounded-md border border-slypn-200 bg-white p-1">
        <button
          type="button"
          :class="[
            'rounded-md px-4 py-1.5 text-sm font-semibold',
            view === 'list' ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50',
          ]"
          @click="view = 'list'"
        >
          List
        </button>
        <button
          type="button"
          :class="[
            'rounded-md px-4 py-1.5 text-sm font-semibold',
            view === 'calendar' ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50',
          ]"
          @click="view = 'calendar'"
        >
          Calendar
        </button>
      </div>
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
      <!-- Type filter (applies to both list and calendar) -->
      <PillFilter v-model="selectedType" :options="eventTypes" class="mb-6" />

      <template v-if="view === 'list'">
        <!-- Link to previous events page -->
        <div v-if="previousCount" class="mb-6">
          <RouterLink
            :to="{ name: 'events-previous' }"
            class="text-sm font-semibold text-slypn-600 hover:text-slypn-800"
          >
            Previous events ({{ previousCount }}) &rarr;
          </RouterLink>
        </div>

        <!-- Main window: last 7 days + upcoming (3 months, then infinite scroll) -->
        <div class="space-y-4">
          <EventCard
            v-for="event in visibleEvents"
            :key="event.id"
            :event="event"
            :past="isPast(event)"
          />
          <p v-if="!windowEvents.length" class="text-center text-slypn-900/70">
            No upcoming events listed.
          </p>
        </div>

        <!-- Infinite-scroll sentinel -->
        <div v-if="hasMore" ref="sentinel" class="py-8 text-center text-sm text-slypn-900/50">
          Loading more events…
        </div>
      </template>

      <EventCalendar v-else :events="typeFiltered" />
    </template>
  </section>
</template>
