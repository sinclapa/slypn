<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { CommunityEvent } from '@/types/content'

const route  = useRoute()
const router = useRouter()

const { data: event, loading, error } = useAsyncData(
  () => apiJson<CommunityEvent>(`/events/${route.params.id}`),
)

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })

const fmtTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false })
</script>

<template>
  <div class="mx-auto w-full max-w-3xl px-6 py-12">

    <button
      type="button"
      class="mb-8 flex items-center gap-1.5 text-sm text-slypn-500 hover:text-slypn-700"
      @click="router.back()"
    >
      &larr; Back
    </button>

    <p v-if="loading" class="text-center text-slypn-900/60">Loading…</p>

    <p v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load event: {{ error }}
    </p>

    <article v-else-if="event" class="space-y-6">
      <!-- type badge -->
      <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
        {{ event.type }}
      </p>

      <h1 class="text-3xl font-extrabold text-slypn-700 sm:text-4xl">{{ event.title }}</h1>

      <!-- meta -->
      <dl class="grid gap-3 rounded-xl border border-slypn-100 bg-slypn-50 p-5 sm:grid-cols-2">
        <div>
          <dt class="text-xs font-semibold uppercase tracking-wider text-slypn-400">Date</dt>
          <dd class="mt-1 text-sm font-medium text-slypn-800">{{ fmtDate(event.startsAt) }}</dd>
        </div>
        <div>
          <dt class="text-xs font-semibold uppercase tracking-wider text-slypn-400">Time</dt>
          <dd class="mt-1 text-sm font-medium text-slypn-800">
            {{ fmtTime(event.startsAt) }}&ndash;{{ fmtTime(event.endsAt) }}
          </dd>
        </div>
        <div class="sm:col-span-2">
          <dt class="text-xs font-semibold uppercase tracking-wider text-slypn-400">Location</dt>
          <dd class="mt-1 text-sm font-medium text-slypn-800">{{ event.location }}</dd>
        </div>
      </dl>

      <!-- description -->
      <p class="text-base leading-relaxed text-slypn-900/80">{{ event.description }}</p>

      <!-- sign up -->
      <a
        v-if="event.signupUrl"
        :href="event.signupUrl"
        target="_blank"
        rel="noopener"
        class="inline-block rounded-md bg-slypn-600 px-6 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
      >
        Sign up &rarr;
      </a>
    </article>

  </div>
</template>
