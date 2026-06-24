<script setup lang="ts">
import { computed, ref } from 'vue'

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

const props = defineProps<{ start: Date; end: Date | null }>()
const emit  = defineEmits<{ change: [start: Date, end: Date | null] }>()

const now = new Date()
const thisYear  = now.getFullYear()
const thisMonth = now.getMonth()

// ── helpers ──────────────────────────────────────────────────────────────────

function ym(year: number, month: number) { return year * 12 + month }
function ymOfDate(d: Date) { return ym(d.getFullYear(), d.getMonth()) }
function dateOfYM(v: number): Date { return new Date(Math.floor(v / 12), v % 12, 1) }

// ── state ────────────────────────────────────────────────────────────────────

const open       = ref(false)
const pickerYear = ref(props.start.getFullYear())
const phase      = ref<'start' | 'end'>('start')  // which click we're waiting for
const anchorYM   = ref(0)                          // first-click ym while picking end
const hoverYM    = ref<number | null>(null)

// ── effective range (committed or live preview) ───────────────────────────────

const committedS = computed(() => ymOfDate(props.start))
// No end date → highlight just the start month (open-ended is conveyed by the label).
const committedE = computed(() => props.end ? ymOfDate(props.end) : ymOfDate(props.start))

const previewS = computed(() => {
  if (phase.value !== 'end') return committedS.value
  const h = hoverYM.value ?? anchorYM.value
  return Math.min(anchorYM.value, h)
})
const previewE = computed(() => {
  if (phase.value !== 'end') return committedE.value
  const h = hoverYM.value ?? anchorYM.value
  return Math.max(anchorYM.value, h)
})

// ── interaction ───────────────────────────────────────────────────────────────

function openPicker() {
  pickerYear.value = props.start.getFullYear()
  phase.value      = 'start'
  hoverYM.value    = null
  open.value       = true
}

function closePicker() {
  open.value    = false
  phase.value   = 'start'
  hoverYM.value = null
}

function pick(year: number, month: number) {
  const clicked = ym(year, month)
  if (phase.value === 'start') {
    anchorYM.value = clicked
    phase.value    = 'end'
  } else {
    const s = Math.min(anchorYM.value, clicked)
    const e = Math.max(anchorYM.value, clicked)
    emit('change', dateOfYM(s), dateOfYM(e))
    closePicker()
  }
}

// ── cell styling ──────────────────────────────────────────────────────────────

function bandClass(year: number, month: number): string {
  const m = ym(year, month)
  const s = previewS.value
  const e = previewE.value
  if (s === e) return ''
  if (m === s)              return 'absolute inset-y-0 left-1/2 right-0 bg-slypn-100'
  if (m === e)              return 'absolute inset-y-0 left-0 right-1/2 bg-slypn-100'
  if (m > s && m < e)       return 'absolute inset-0 bg-slypn-100'
  return ''
}

function isCurrent(year: number, month: number) {
  return year === thisYear && month === thisMonth
}

function btnClass(year: number, month: number): string {
  const m = ym(year, month)
  const s = previewS.value
  const e = previewE.value
  const current = isCurrent(year, month)

  if (m === s || m === e)
    return `bg-slypn-600 text-white rounded-full font-semibold${current ? ' ring-2 ring-offset-1 ring-slypn-400' : ''}`
  if (m > s && m < e)
    return `text-slypn-800${current ? ' font-semibold underline decoration-slypn-500 decoration-2 underline-offset-2' : ''}`
  return `text-slypn-700 hover:bg-slypn-50 rounded-full${current ? ' ring-1 ring-slypn-400' : ''}`
}

function selectThisMonth() {
  pick(thisYear, thisMonth)
}

// Commit an open-ended range: from the chosen start month onwards (no end).
function pickNoEnd() {
  const start = phase.value === 'end' ? anchorYM.value : committedS.value
  emit('change', dateOfYM(start), null)
  closePicker()
}

// ── trigger label ─────────────────────────────────────────────────────────────

const label = computed(() => {
  const fmt = (d: Date) => d.toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })
  return props.end ? `${fmt(props.start)} – ${fmt(props.end)}` : `${fmt(props.start)} onwards`
})

const hint = computed(() =>
  phase.value === 'end' ? 'Select end month, or no end date' : 'Select start month',
)
</script>

<template>
  <div class="relative">
    <!-- Trigger -->
    <button
      type="button"
      class="flex w-full items-center justify-between rounded-md border border-slypn-200 bg-white px-3 py-2 text-left text-sm shadow-sm hover:bg-slypn-50"
      @click="open ? closePicker() : openPicker()"
    >
      <span>
        <span class="block text-[10px] font-semibold uppercase tracking-widest text-slypn-400">Date range</span>
        <span class="font-medium text-slypn-700">{{ label }}</span>
      </span>
      <svg class="h-4 w-4 text-slypn-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
      </svg>
    </button>

    <!-- Backdrop -->
    <div v-if="open" class="fixed inset-0 z-10" @click="closePicker" />

    <!-- Picker panel -->
    <div
      v-if="open"
      class="absolute left-1/2 top-full z-20 mt-2 -translate-x-1/2 rounded-xl border border-slypn-200 bg-white shadow-xl md:left-0 md:translate-x-0"
    >
      <!-- Two year panels -->
      <div class="flex divide-x divide-slypn-100 p-4">

        <!-- Left year (the only panel shown on mobile) -->
        <div class="w-44 md:pr-4">
          <div class="mb-3 flex items-center justify-between">
            <button
              type="button"
              class="rounded p-1 text-slypn-500 hover:bg-slypn-50"
              aria-label="Previous year"
              @click="pickerYear--"
            >&larr;</button>
            <span class="text-sm font-semibold text-slypn-700">{{ pickerYear }}</span>
            <!-- Next-year arrow on mobile; desktop uses the second panel's arrow. -->
            <button
              type="button"
              class="rounded p-1 text-slypn-500 hover:bg-slypn-50 md:hidden"
              aria-label="Next year"
              @click="pickerYear++"
            >&rarr;</button>
            <!-- spacer keeps the desktop header symmetric -->
            <span class="hidden w-6 md:block" />
          </div>
          <div class="grid grid-cols-3">
            <div
              v-for="(m, i) in MONTHS"
              :key="i"
              class="relative py-0.5"
              @mouseenter="hoverYM = ym(pickerYear, i)"
              @mouseleave="hoverYM = null"
            >
              <div :class="bandClass(pickerYear, i)" />
              <button
                type="button"
                :class="['relative z-10 w-full py-1.5 text-center text-sm', btnClass(pickerYear, i)]"
                @click="pick(pickerYear, i)"
              >{{ m }}</button>
            </div>
          </div>
        </div>

        <!-- Right year (desktop only — mobile shows a single year) -->
        <div class="hidden w-44 pl-4 md:block">
          <div class="mb-3 flex items-center justify-between">
            <span class="w-6" />
            <span class="text-sm font-semibold text-slypn-700">{{ pickerYear + 1 }}</span>
            <button
              type="button"
              class="rounded p-1 text-slypn-500 hover:bg-slypn-50"
              @click="pickerYear++"
            >&rarr;</button>
          </div>
          <div class="grid grid-cols-3">
            <div
              v-for="(m, i) in MONTHS"
              :key="i"
              class="relative py-0.5"
              @mouseenter="hoverYM = ym(pickerYear + 1, i)"
              @mouseleave="hoverYM = null"
            >
              <div :class="bandClass(pickerYear + 1, i)" />
              <button
                type="button"
                :class="['relative z-10 w-full py-1.5 text-center text-sm', btnClass(pickerYear + 1, i)]"
                @click="pick(pickerYear + 1, i)"
              >{{ m }}</button>
            </div>
          </div>
        </div>

      </div>

      <!-- Footer: hint on its own line, then shortcuts -->
      <div class="border-t border-slypn-100 px-4 py-2.5">
        <p class="mb-2 text-xs text-slypn-400">{{ hint }}</p>
        <div class="flex justify-end gap-2">
          <button
            type="button"
            :disabled="phase !== 'end'"
            class="rounded-md bg-slypn-50 px-2.5 py-1 text-xs font-medium text-slypn-600 hover:bg-slypn-100 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-slypn-50"
            @click="pickNoEnd"
          >{{ phase === 'end' ? 'No end date' : 'Select end date' }}</button>
          <button
            type="button"
            class="rounded-md bg-slypn-50 px-2.5 py-1 text-xs font-medium text-slypn-600 hover:bg-slypn-100"
            @click="selectThisMonth"
          >This month</button>
        </div>
      </div>
    </div>
  </div>
</template>
