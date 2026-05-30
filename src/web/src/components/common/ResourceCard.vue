<script setup lang="ts">
import type { Resource } from '@/types/content'

defineProps<{ resource: Resource }>()
</script>

<template>
  <a
    :href="resource.url"
    target="_blank"
    rel="noopener"
    class="block rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
  >
    <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
      {{ resource.category }}
    </p>
    <h3 class="mt-2 text-lg font-bold text-slypn-700">{{ resource.title }}</h3>
    <p class="mt-2 text-sm text-slypn-900/75">{{ resource.description }}</p>
    <p class="mt-3 text-xs text-slypn-600">{{ resource.url.replace(/^https?:\/\//, '') }} &rarr;</p>
  </a>
</template>
