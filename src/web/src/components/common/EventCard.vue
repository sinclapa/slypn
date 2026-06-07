<script setup lang="ts">
import type { CommunityEvent } from '@/types/content'

const props = defineProps<{ event: CommunityEvent; past?: boolean }>()

const currentYear = new Date().getFullYear()

const fmtDate = (iso: string) => {
  const d = new Date(iso)
  const opts: Intl.DateTimeFormatOptions = { weekday: 'short', day: 'numeric', month: 'short' }
  if (d.getFullYear() !== currentYear) opts.year = 'numeric'
  return d.toLocaleDateString('en-GB', opts)
}

const fmtTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false })

const isSameDay = (() => {
  const s = new Date(props.event.startsAt)
  const e = new Date(props.event.endsAt)
  return s.getFullYear() === e.getFullYear()
    && s.getMonth()    === e.getMonth()
    && s.getDate()     === e.getDate()
})()
</script>

<template>
  <RouterLink
    :to="{ name: 'event-detail', params: { id: event.id } }"
    :class="[
      'relative flex gap-5 rounded-xl border p-5 shadow-sm transition-shadow hover:shadow-md',
      past ? 'border-slypn-100 bg-slypn-50 opacity-70' : 'border-slypn-100 bg-white',
    ]"
  >
    <div
      :class="[
        'flex w-20 flex-shrink-0 flex-col items-center justify-center rounded-lg text-center',
        past ? 'bg-slypn-100/60' : 'bg-slypn-50',
      ]"
    >
      <span class="text-xs font-semibold uppercase tracking-wider text-slypn-500">
        {{ new Date(event.startsAt).toLocaleDateString('en-GB', { weekday: 'short' }) }}
      </span>
      <span class="font-display text-2xl font-extrabold text-slypn-700">
        {{ new Date(event.startsAt).getDate() }}
      </span>
      <span class="text-xs font-semibold uppercase tracking-wider text-slypn-500">
        {{ new Date(event.startsAt).toLocaleDateString('en-GB', { month: 'short' }) }}
      </span>
    </div>

    <div class="min-w-0 flex-1">
      <div class="flex items-center gap-2">
        <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
          {{ event.type }}
        </p>
        <span
          v-if="past"
          class="rounded-full bg-slypn-200 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-slypn-500"
        >Past</span>
      </div>
      <h3 class="mt-1 truncate text-lg font-bold text-slypn-700">{{ event.title }}</h3>
      <p class="mt-1 text-sm text-slypn-900/75">{{ event.description }}</p>
      <div class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slypn-900/60">
        <span v-if="isSameDay">{{ fmtDate(event.startsAt) }}, {{ fmtTime(event.startsAt) }}&ndash;{{ fmtTime(event.endsAt) }}</span>
        <span v-else>{{ fmtDate(event.startsAt) }}, {{ fmtTime(event.startsAt) }}&nbsp;&ndash;&nbsp;{{ fmtDate(event.endsAt) }}, {{ fmtTime(event.endsAt) }}</span>
        <span>{{ event.location }}</span>
        <a
          v-if="event.signupUrl"
          :href="event.signupUrl"
          target="_blank"
          rel="noopener"
          class="text-slypn-600 underline underline-offset-2 hover:text-slypn-700"
        >Sign up</a>
      </div>
    </div>
  </RouterLink>
</template>
