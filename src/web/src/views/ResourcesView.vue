<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ResourceCard from '@/components/common/ResourceCard.vue'
import { apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Resource, ResourceCategory } from '@/types/content'

const categories: Array<'All' | ResourceCategory> = [
  'All',
  "Parkinson's UK",
  'NHS',
  'Local',
  'Benefits',
  'Carers',
  'Research',
]
const selected = ref<typeof categories[number]>('All')

const { data: resources, loading, error, refresh } = useAsyncData(
  () => apiJson<Resource[]>('/resources'),
)

const visible = computed(() => {
  const list = resources.value ?? []
  return selected.value === 'All'
    ? list
    : list.filter(r => r.category === selected.value)
})
</script>

<template>
  <HeroBanner
    eyebrow="Resources"
    title="Where to read more &mdash; or get help today"
    subtitle="A curated list of links our members come back to most often: the Parkinson's UK helpline and information pages, NHS overviews, local clinics, benefits, and research."
  />

  <section class="mx-auto max-w-6xl px-6 py-16">
    <div class="flex flex-wrap gap-2">
      <button
        v-for="cat in categories"
        :key="cat"
        type="button"
        :class="[
          'rounded-full border px-4 py-1.5 text-sm font-medium transition-colors',
          selected === cat
            ? 'border-slypn-600 bg-slypn-600 text-white'
            : 'border-slypn-200 bg-white text-slypn-700 hover:bg-slypn-50',
        ]"
        @click="selected = cat"
      >
        {{ cat }}
      </button>
    </div>

    <p v-if="loading && !resources" class="mt-12 text-center text-slypn-900/70">
      Loading resources&hellip;
    </p>

    <div v-else-if="error" class="mt-12 rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load resources: {{ error }}.
      <button class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <div v-else class="mt-8 grid gap-5 md:grid-cols-2 lg:grid-cols-3">
      <ResourceCard v-for="resource in visible" :key="resource.id" :resource="resource" />
    </div>

    <p v-if="!loading && !error && !visible.length" class="mt-12 text-center text-slypn-900/70">
      No resources in this category yet.
    </p>
  </section>
</template>
