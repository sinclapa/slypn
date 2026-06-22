<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
import PublishedContent from '@/components/common/PublishedContent.vue'
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="Content management"
    subtitle="Review and remove published articles and blog posts."
  >
    <template #actions>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="text-sm font-semibold text-white/80 hover:text-white"
      >&larr; Dashboard</RouterLink>
    </template>
  </HeroBanner>

  <section class="page-container space-y-6 py-16">
    <PublishedContent />
  </section>
</template>
