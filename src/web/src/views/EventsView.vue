<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import EventCard from '@/components/common/EventCard.vue'
import EventCalendar from '@/components/common/EventCalendar.vue'
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

// Main window: events that started within the last 7 days or in the future, ascending
const windowEvents = computed(() =>
  [...(events.value ?? [])]
    .filter(e => new Date(e.startsAt) >= sevenDaysAgo)
    .sort((a, b) => +new Date(a.startsAt) - +new Date(b.startsAt)),
)

// Count of events older than the 7-day window (for the link label)
const previousCount = computed(() =>
  (events.value ?? []).filter(e => new Date(e.startsAt) < sevenDaysAgo).length,
)
</script>

<template>
  <HeroBanner
    eyebrow="Events"
    title="Coffee meet-ups, drinks, Q&amp;As, and fundraisers"
    subtitle="Most of our events are evening coffee meet-ups around South London. Drop-in, no booking — just turn up. Partners and carers welcome."
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

  <section class="mx-auto w-full max-w-4xl px-6 py-16">
    <p v-if="loading && !events" class="text-center text-slypn-900/70">
      Loading events&hellip;
    </p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load events: {{ error }}.
      <button class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <template v-else-if="view === 'list'">
      <!-- Main window: last 7 days + upcoming -->
      <div class="space-y-4">
        <EventCard
          v-for="event in windowEvents"
          :key="event.id"
          :event="event"
          :past="isPast(event)"
        />
        <p v-if="!windowEvents.length" class="text-center text-slypn-900/70">
          No upcoming events listed.
        </p>
      </div>

      <!-- Link to previous events page -->
      <div v-if="previousCount" class="mt-10 text-center">
        <RouterLink
          :to="{ name: 'events-previous' }"
          class="text-sm font-semibold text-slypn-600 hover:text-slypn-800"
        >
          Previous events ({{ previousCount }}) &rarr;
        </RouterLink>
      </div>
    </template>

    <EventCalendar v-else :events="events ?? []" />
  </section>
</template>
