<script setup lang="ts">
import { RouterLink } from 'vue-router'
import HeroBanner from '@/components/common/HeroBanner.vue'
</script>

<template>
  <HeroBanner
    eyebrow="South London"
    title="Younger Parkinson's Network"
    subtitle="A community for working-age people living with Parkinson's in South London — coffee meet-ups, drinks, activities, and fundraising events. Affiliated with Parkinson's UK."
  >
    <template #actions>
      <RouterLink
        to="/about"
        class="rounded-md bg-slypn-600 px-5 py-3 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
      >
        About SLYPN
      </RouterLink>
      <RouterLink
        to="/events"
        class="rounded-md border border-slypn-200 bg-white px-5 py-3 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
      >
        Upcoming events
      </RouterLink>
    </template>
  </HeroBanner>

  <section class="mx-auto max-w-6xl px-6 py-16">
    <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-slypn-500">
      Site under construction
    </p>
    <h2 class="mt-3 max-w-2xl text-3xl font-extrabold text-slypn-700">
      We're building this in the open
    </h2>
    <p class="mt-4 max-w-2xl text-slypn-900/80">
      The site is in early development. Track each phase &mdash; scaffold, persistence,
      auth, authoring, observability, deployment &mdash; on the
      <a
        href="https://github.com/users/sinclapa/projects/2"
        class="text-slypn-600 underline underline-offset-4 hover:text-slypn-700"
        rel="noopener"
        target="_blank"
      >GitHub project board</a>.
    </p>
  </section>
</template>
