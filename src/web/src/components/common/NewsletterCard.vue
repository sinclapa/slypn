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
  </article>
</template>
