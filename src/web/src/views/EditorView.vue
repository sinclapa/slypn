<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
</script>

<template>
  <HeroBanner
    eyebrow="Editor"
    title="Write something for the community"
    subtitle="The rich-text editor with autosave and draft management lands with #26-#30. This page is the shell that the guard protects."
  />

  <section class="mx-auto max-w-3xl px-6 py-16">
    <p class="text-slypn-900/80">
      For now: see
      <a
        href="https://github.com/users/sinclapa/projects/2"
        class="text-slypn-600 underline underline-offset-4 hover:text-slypn-700"
        rel="noopener"
        target="_blank"
      >Phase 4</a> on the project board for the editor build plan.
    </p>
  </section>
</template>
