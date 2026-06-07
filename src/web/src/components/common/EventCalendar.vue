<script setup lang="ts">
import { computed, ref } from 'vue'
import type { CommunityEvent } from '@/types/content'

const props = defineProps<{ events: CommunityEvent[] }>()

const today  = new Date()
const cursor = ref(new Date(today.getFullYear(), today.getMonth(), 1))

const monthLabel = computed(() =>
  cursor.value.toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }),
)

interface DayEventInfo {
  event: CommunityEvent
  startsToday: boolean
  endsToday: boolean
}

interface Cell {
  date: Date
  inMonth: boolean
  events: DayEventInfo[]
}

const weeks = computed(() => {
  const first = new Date(cursor.value)
  const start = new Date(first)
  const dayOfWeek = (first.getDay() + 6) % 7 // Monday-first
  start.setDate(first.getDate() - dayOfWeek)

  const cells: Cell[] = []
  for (let i = 0; i < 42; i++) {
    const d = new Date(start)
    d.setDate(start.getDate() + i)

    const dayStart = new Date(d.getFullYear(), d.getMonth(), d.getDate())
    const nextDay  = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)

    // An event spans this day if it starts before next midnight AND ends after this midnight
    const dayEvents: DayEventInfo[] = props.events
      .filter(e => new Date(e.startsAt) < nextDay && new Date(e.endsAt) > dayStart)
      .map(e => ({
        event:       e,
        startsToday: new Date(e.startsAt) >= dayStart && new Date(e.startsAt) < nextDay,
        endsToday:   new Date(e.endsAt)   >= dayStart && new Date(e.endsAt)   <= nextDay,
      }))

    cells.push({ date: d, inMonth: d.getMonth() === first.getMonth(), events: dayEvents })
  }

  const rows: Cell[][] = []
  for (let i = 0; i < 6; i++) rows.push(cells.slice(i * 7, i * 7 + 7))
  return rows
})

const weekDays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']

const isToday = (d: Date) =>
  d.getFullYear() === today.getFullYear()
  && d.getMonth()    === today.getMonth()
  && d.getDate()     === today.getDate()

const shift    = (months: number) => {
  cursor.value = new Date(cursor.value.getFullYear(), cursor.value.getMonth() + months, 1)
}
const goToToday = () => {
  cursor.value = new Date(today.getFullYear(), today.getMonth(), 1)
}

// Pill colour: single-day events are solid; multi-day continuation is lighter
function pillClass(info: DayEventInfo): string {
  if (info.startsToday && info.endsToday)  return 'bg-slypn-200 text-slypn-800'
  if (info.startsToday  && !info.endsToday) return 'bg-slypn-300 text-slypn-900 rounded-r-none'
  if (!info.startsToday && info.endsToday)  return 'bg-slypn-200 text-slypn-800 rounded-l-none'
  return 'bg-slypn-100 text-slypn-700 rounded-none'
}
</script>

<template>
  <div class="w-full rounded-xl border border-slypn-100 bg-white p-5 shadow-sm">
    <div class="flex items-center justify-between">
      <h3 class="font-display text-xl font-bold text-slypn-700">{{ monthLabel }}</h3>
      <div class="flex gap-1">
        <button
          type="button"
          class="rounded-md border border-slypn-200 px-3 py-1 text-sm text-slypn-700 hover:bg-slypn-50"
          @click="goToToday"
        >
          Today
        </button>
        <button
          type="button"
          class="rounded-md border border-slypn-200 px-3 py-1 text-sm text-slypn-700 hover:bg-slypn-50"
          aria-label="Previous month"
          @click="shift(-1)"
        >
          &larr;
        </button>
        <button
          type="button"
          class="rounded-md border border-slypn-200 px-3 py-1 text-sm text-slypn-700 hover:bg-slypn-50"
          aria-label="Next month"
          @click="shift(1)"
        >
          &rarr;
        </button>
      </div>
    </div>

    <div class="mt-4 grid grid-cols-7 gap-1">
      <div
        v-for="d in weekDays"
        :key="d"
        class="text-center text-xs font-semibold uppercase tracking-wider text-slypn-500"
      >{{ d }}</div>

      <template v-for="(row, ri) in weeks" :key="ri">
        <div
          v-for="cell in row"
          :key="cell.date.toISOString()"
          :class="[
            'h-[88px] overflow-hidden rounded-md border p-1.5 text-left text-xs',
            cell.inMonth ? 'border-slypn-100 bg-white' : 'border-slypn-50 bg-slypn-50/40 text-slypn-900/40',
            isToday(cell.date) ? 'ring-2 ring-slypn-500' : '',
          ]"
        >
          <div
            :class="[
              'inline-flex h-5 w-5 items-center justify-center rounded-full font-semibold',
              isToday(cell.date) ? 'bg-slypn-600 text-white' : '',
            ]"
          >{{ cell.date.getDate() }}</div>

          <RouterLink
            v-for="info in cell.events"
            :key="`${info.event.id}-${cell.date.toISOString()}`"
            :to="{ name: 'event-detail', params: { id: info.event.id } }"
            :class="['mt-1 block truncate px-1.5 py-0.5 text-[11px] hover:opacity-80 rounded', pillClass(info)]"
            :title="info.event.title"
          >
            <template v-if="!info.startsToday">&rarr; </template>{{ info.startsToday ? info.event.title : info.event.title }}<template v-if="!info.endsToday"> &rarr;</template>
          </RouterLink>
        </div>
      </template>
    </div>
  </div>
</template>
