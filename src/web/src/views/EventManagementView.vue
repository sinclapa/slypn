<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import MonthRangePicker from '@/components/common/MonthRangePicker.vue'
import EventFormDialog from '@/components/common/EventFormDialog.vue'
import { apiFetch, apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import { useAuthStore } from '@/stores/auth'
import type { CommunityEvent } from '@/types/content'

const auth = useAuthStore()

// ── event list ────────────────────────────────────────────────────────────────

const { data: events, loading, error: loadError, refresh } = useAsyncData(
  () => apiJson<CommunityEvent[]>('/events'),
)

// ── date range — default −2 / +2 months ──────────────────────────────────────

function monthOffset(n: number): Date {
  const d = new Date()
  d.setDate(1); d.setHours(0, 0, 0, 0); d.setMonth(d.getMonth() + n)
  return d
}

const rangeStart = ref(monthOffset(-2))
const rangeEnd   = ref(monthOffset(2))

function onRangeChange(start: Date, end: Date) {
  rangeStart.value = start
  rangeEnd.value   = end
}

function ym(d: Date) { return d.getFullYear() * 12 + d.getMonth() }

// ── search + filter ───────────────────────────────────────────────────────────

const searchQuery = ref('')

const currentYear = new Date().getFullYear()

const fmtDate = (iso: string) => {
  const d = new Date(iso)
  const opts: Intl.DateTimeFormatOptions = { weekday: 'short', day: 'numeric', month: 'short' }
  if (d.getFullYear() !== currentYear) opts.year = 'numeric'
  return d.toLocaleDateString('en-GB', opts)
}

const fmtTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false })

const isSameDay = (a: string, b: string) => {
  const s = new Date(a); const e = new Date(b)
  return s.getFullYear() === e.getFullYear() && s.getMonth() === e.getMonth() && s.getDate() === e.getDate()
}

const filtered = computed(() => {
  const q  = searchQuery.value.trim().toLowerCase()
  const lo = ym(rangeStart.value)
  const hi = ym(rangeEnd.value)
  return [...(events.value ?? [])]
    .filter(e => {
      const d = new Date(e.startsAt)
      if (ym(d) < lo || ym(d) > hi) return false
      if (!q) return true
      return (
        e.title.toLowerCase().includes(q)       ||
        e.type.toLowerCase().includes(q)        ||
        e.location.toLowerCase().includes(q)    ||
        e.description.toLowerCase().includes(q) ||
        fmtDate(e.startsAt).toLowerCase().includes(q)
      )
    })
    .sort((a, b) => +new Date(a.startsAt) - +new Date(b.startsAt))
})

// ── permissions ───────────────────────────────────────────────────────────────

function canEdit(event: CommunityEvent): boolean {
  return auth.isAdmin || event.createdBy === auth.oid
}

// ── dialog ────────────────────────────────────────────────────────────────────

const dialogOpen  = ref(false)
const dialogEvent = ref<CommunityEvent | null>(null)

function openAdd() {
  dialogEvent.value = null
  dialogOpen.value  = true
}

function openEdit(event: CommunityEvent) {
  dialogEvent.value = event
  dialogOpen.value  = true
}

function onSaved() {
  refresh()
}

// ── delete ────────────────────────────────────────────────────────────────────

const deleting    = ref<string | null>(null)
const deleteError = ref<string | null>(null)

async function deleteEvent(event: CommunityEvent) {
  if (!confirm(`Delete "${event.title}"?\n\nThis cannot be undone.`)) return
  deleting.value    = event.id
  deleteError.value = null
  try {
    const headers: Record<string, string> = {}
    if (event._etag) headers['If-Match'] = `"${event._etag}"`
    const resp = await apiFetch(`/events/${event.id}`, { method: 'DELETE', headers })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    refresh()
  } catch (err) {
    deleteError.value = err instanceof Error ? err.message : String(err)
  } finally {
    deleting.value = null
  }
}
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="Event management"
    subtitle="Add, edit, and remove community events. Contributors may manage their own events; admins can manage all."
  />

  <section class="mx-auto w-full max-w-3xl space-y-6 px-6 py-16">

    <!-- Manage events -->
    <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <div class="flex items-center justify-between">
        <h2 class="font-display text-xl font-bold text-slypn-700">Manage events</h2>
        <button
          type="button"
          class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
          @click="openAdd"
        >+ Add event</button>
      </div>
      <p class="mt-2 text-sm text-slypn-900/75">
        Admins can edit or delete any event. Contributors can only edit or delete events they created.
      </p>

      <!-- Search + date range -->
      <div class="mt-5 space-y-3">
        <input
          v-model="searchQuery"
          type="search"
          placeholder="Search by title, location, type, or date (e.g. Jun, 2025)…"
          class="w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
        <div class="flex items-center gap-2">
          <div class="flex-1">
            <MonthRangePicker :start="rangeStart" :end="rangeEnd" @change="onRangeChange" />
          </div>
          <span class="shrink-0 text-xs text-slypn-400">{{ filtered.length }} event{{ filtered.length === 1 ? '' : 's' }}</span>
        </div>
      </div>

      <!-- Event list -->
      <p v-if="loading" class="mt-4 text-sm text-slypn-900/60">Loading events…</p>
      <p v-else-if="loadError" class="mt-4 rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
        Couldn&rsquo;t load events: {{ loadError }}.
        <button class="ml-1 underline" @click="refresh">Retry</button>
      </p>
      <p v-else-if="!filtered.length" class="mt-4 text-sm text-slypn-900/60">No events match.</p>

      <ul v-else class="mt-3 divide-y divide-slypn-100">
        <li
          v-for="event in filtered"
          :key="event.id"
          class="flex items-center justify-between gap-4 py-3"
        >
          <div class="min-w-0 flex-1">
            <RouterLink
              :to="{ name: 'event-detail', params: { id: event.id } }"
              class="block truncate text-sm font-medium text-slypn-800 hover:text-slypn-600 hover:underline"
            >{{ event.title }}</RouterLink>
            <p class="mt-0.5 text-xs text-slypn-500">
              <template v-if="isSameDay(event.startsAt, event.endsAt)">
                {{ fmtDate(event.startsAt) }}, {{ fmtTime(event.startsAt) }}&ndash;{{ fmtTime(event.endsAt) }}
              </template>
              <template v-else>
                {{ fmtDate(event.startsAt) }}, {{ fmtTime(event.startsAt) }}&nbsp;&ndash;&nbsp;{{ fmtDate(event.endsAt) }}, {{ fmtTime(event.endsAt) }}
              </template>
              <span v-if="event.createdByName" class="ml-2 text-slypn-400">· {{ event.createdByName }}</span>
            </p>
          </div>

          <div v-if="canEdit(event)" class="flex shrink-0 gap-2">
            <button
              type="button"
              class="rounded-md border border-slypn-200 px-3 py-1.5 text-xs font-medium text-slypn-600 hover:bg-slypn-50"
              @click="openEdit(event)"
            >Edit</button>
            <button
              type="button"
              class="rounded-md border border-rose-200 px-3 py-1.5 text-xs font-semibold text-rose-600 hover:bg-rose-50 disabled:opacity-40"
              :disabled="deleting === event.id"
              @click="deleteEvent(event)"
            >{{ deleting === event.id ? 'Deleting…' : 'Delete' }}</button>
          </div>
        </li>
      </ul>

      <p v-if="deleteError" class="mt-3 rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">{{ deleteError }}</p>
    </article>

  </section>

  <!-- Add/edit dialog -->
  <EventFormDialog
    :open="dialogOpen"
    :event="dialogEvent"
    @close="dialogOpen = false"
    @saved="onSaved"
  />
</template>
