<script setup lang="ts">
import { computed, ref } from 'vue'
import type { CommunityEvent } from '@/types/content'

const props = defineProps<{ events: CommunityEvent[] }>()

const today = new Date()
const cursor = ref(new Date(today.getFullYear(), today.getMonth(), 1))

const monthLabel = computed(() =>
  cursor.value.toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }),
)

const weeks = computed(() => {
  const first = new Date(cursor.value)
  const start = new Date(first)
  const dayOfWeek = (first.getDay() + 6) % 7 // Monday-first
  start.setDate(first.getDate() - dayOfWeek)

  const cells: Array<{ date: Date; inMonth: boolean; events: CommunityEvent[] }> = []
  for (let i = 0; i < 42; i++) {
    const d = new Date(start)
    d.setDate(start.getDate() + i)
    const dayEvents = props.events.filter(e => {
      const ed = new Date(e.startsAt)
      return ed.getFullYear() === d.getFullYear()
        && ed.getMonth() === d.getMonth()
        && ed.getDate() === d.getDate()
    })
    cells.push({ date: d, inMonth: d.getMonth() === first.getMonth(), events: dayEvents })
  }
  const rows = []
  for (let i = 0; i < 6; i++) rows.push(cells.slice(i * 7, i * 7 + 7))
  return rows
})

const weekDays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']

const isToday = (d: Date) =>
  d.getFullYear() === today.getFullYear()
  && d.getMonth() === today.getMonth()
  && d.getDate() === today.getDate()

const shift = (months: number) => {
  cursor.value = new Date(cursor.value.getFullYear(), cursor.value.getMonth() + months, 1)
}
</script>

<template>
  <div class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm">
    <div class="flex items-center justify-between">
      <h3 class="font-display text-xl font-bold text-slypn-700">{{ monthLabel }}</h3>
      <div class="flex gap-1">
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

    <div class="mt-4 grid grid-cols-7 gap-1 text-center text-xs font-semibold uppercase tracking-wider text-slypn-500">
      <div v-for="d in weekDays" :key="d">{{ d }}</div>
    </div>

    <div class="mt-1 grid grid-cols-7 gap-1">
      <template v-for="(row, ri) in weeks" :key="ri">
        <div
          v-for="cell in row"
          :key="cell.date.toISOString()"
          :class="[
            'min-h-[88px] rounded-md border p-1.5 text-left text-xs',
            cell.inMonth ? 'border-slypn-100 bg-white' : 'border-slypn-50 bg-slypn-50/40 text-slypn-900/40',
            isToday(cell.date) ? 'ring-2 ring-slypn-500' : '',
          ]"
        >
          <div class="font-semibold">{{ cell.date.getDate() }}</div>
          <div v-for="e in cell.events" :key="e.id" class="mt-1 truncate rounded bg-slypn-100 px-1.5 py-0.5 text-[11px] text-slypn-800" :title="e.title">
            {{ e.title }}
          </div>
        </div>
      </template>
    </div>
  </div>
</template>
