<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ResourceCard from '@/components/common/ResourceCard.vue'
import { mockResources } from '@/mock/resources'
import type { ResourceCategory } from '@/types/content'

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

const visible = computed(() =>
  selected.value === 'All'
    ? mockResources
    : mockResources.filter(r => r.category === selected.value),
)
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

    <div class="mt-8 grid gap-5 md:grid-cols-2 lg:grid-cols-3">
      <ResourceCard v-for="resource in visible" :key="resource.id" :resource="resource" />
    </div>
  </section>
</template>
