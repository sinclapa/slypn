<script setup lang="ts">
import type { Newsletter } from '@/types/content'

defineProps<{ newsletter: Newsletter }>()

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })
</script>

<template>
  <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
    <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
      {{ formatDate(newsletter.issueDate) }}
    </p>
    <h3 class="mt-2 text-xl font-bold text-slypn-700">{{ newsletter.title }}</h3>
    <p class="mt-3 text-sm text-slypn-900/75">{{ newsletter.summary }}</p>
    <ul class="mt-4 flex flex-wrap gap-2">
      <li
        v-for="topic in newsletter.topics"
        :key="topic"
        class="rounded-full bg-slypn-50 px-3 py-1 text-xs font-medium text-slypn-700"
      >
        {{ topic }}
      </li>
    </ul>
    <a
      v-if="newsletter.fileName"
      :href="`/api/newsletters/${newsletter.id}/file`"
      class="mt-4 inline-flex items-center gap-1.5 text-sm font-semibold text-slypn-600 hover:text-slypn-700 hover:underline"
      download
    >
      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path d="M10 2a1 1 0 0 1 1 1v7.586l2.293-2.293a1 1 0 1 1 1.414 1.414l-4 4a1 1 0 0 1-1.414 0l-4-4a1 1 0 1 1 1.414-1.414L9 10.586V3a1 1 0 0 1 1-1Z" />
        <path d="M3 14a1 1 0 0 1 1 1v1a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1v-1a1 1 0 1 1 2 0v1a3 3 0 0 1-3 3H5a3 3 0 0 1-3-3v-1a1 1 0 0 1 1-1Z" />
      </svg>
      Download issue
    </a>
  </article>
</template>
