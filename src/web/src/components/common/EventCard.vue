<script setup lang="ts">
import type { CommunityEvent } from '@/types/content'

defineProps<{ event: CommunityEvent }>()

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })

const formatTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false })
</script>

<template>
  <article class="flex gap-5 rounded-xl border border-slypn-100 bg-white p-5 shadow-sm">
    <div class="flex w-20 flex-shrink-0 flex-col items-center justify-center rounded-lg bg-slypn-50 text-center">
      <span class="font-display text-2xl font-extrabold text-slypn-700">
        {{ new Date(event.startsAt).getDate() }}
      </span>
      <span class="text-xs font-semibold uppercase tracking-wider text-slypn-500">
        {{ new Date(event.startsAt).toLocaleDateString('en-GB', { month: 'short' }) }}
      </span>
    </div>

    <div class="min-w-0 flex-1">
      <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
        {{ event.type }}
      </p>
      <h3 class="mt-1 truncate text-lg font-bold text-slypn-700">{{ event.title }}</h3>
      <p class="mt-1 text-sm text-slypn-900/75">{{ event.description }}</p>
      <div class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slypn-900/60">
        <span>{{ formatDate(event.startsAt) }}, {{ formatTime(event.startsAt) }}&ndash;{{ formatTime(event.endsAt) }}</span>
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
  </article>
</template>
