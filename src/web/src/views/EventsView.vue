<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import EventCard from '@/components/common/EventCard.vue'
import EventCalendar from '@/components/common/EventCalendar.vue'
import { mockEvents } from '@/mock/events'

const view = ref<'list' | 'calendar'>('list')

const upcoming = computed(() => {
  const now = Date.now()
  return [...mockEvents]
    .filter(e => +new Date(e.startsAt) >= now)
    .sort((a, b) => +new Date(a.startsAt) - +new Date(b.startsAt))
})
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

  <section class="mx-auto max-w-4xl px-6 py-16">
    <div v-if="view === 'list'" class="space-y-4">
      <EventCard v-for="event in upcoming" :key="event.id" :event="event" />
      <p v-if="!upcoming.length" class="text-center text-slypn-900/70">
        No upcoming events listed.
      </p>
    </div>

    <EventCalendar v-else :events="mockEvents" />
  </section>
</template>
