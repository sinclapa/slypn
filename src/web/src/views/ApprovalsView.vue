<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
import ApprovalsQueue from '@/components/common/ApprovalsQueue.vue'
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="Approvals"
    subtitle="Review content submitted by contributors and approve or request revisions."
  >
    <template #actions>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="text-sm font-semibold text-white/80 hover:text-white"
      >&larr; Dashboard</RouterLink>
    </template>
  </HeroBanner>

  <section class="page-container py-16">
    <ApprovalsQueue />
  </section>
</template>
