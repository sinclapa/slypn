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
        :to="{ name: 'admin' }"
        class="text-sm font-semibold text-white/80 hover:text-white"
      >&larr; Admin</RouterLink>
    </template>
  </HeroBanner>

  <section class="mx-auto max-w-3xl px-6 py-16">
    <ApprovalsQueue />
  </section>
</template>
