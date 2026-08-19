<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
const route = useRoute()
</script>

<template>
  <main data-testid="not-found" class="min-h-[60vh] px-6 py-20">
    <div class="mx-auto max-w-3xl text-center">
      <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-slypn-500">
        Coming soon
      </p>
      <h1 class="mt-3 text-4xl font-extrabold text-slypn-700">
        We&rsquo;re still building this page
      </h1>
      <p class="mt-4 text-slypn-900/80">
        The page at <code class="rounded bg-slypn-100 px-2 py-0.5 text-slypn-800">{{ route.fullPath }}</code>
        isn&rsquo;t live yet. Progress is tracked on the
        <a
          href="https://github.com/users/sinclapa/projects/2"
          class="text-slypn-600 underline underline-offset-4 hover:text-slypn-700"
          rel="noopener"
          target="_blank"
        >project board</a>.
      </p>
      <RouterLink
        to="/"
        class="mt-8 inline-block rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-slypn-700"
      >
        Back to home
      </RouterLink>
    </div>
  </main>
</template>
