<script setup lang="ts">
defineProps<{
  eyebrow?: string
  title: string
  subtitle?: string
}>()
</script>

<template>
  <section class="relative isolate overflow-hidden bg-gradient-to-br from-slypn-50 via-white to-slypn-100">
    <div class="absolute inset-y-0 right-0 -z-10 hidden w-1/2 bg-[radial-gradient(circle_at_top_right,_var(--tw-gradient-stops))] from-slypn-200/60 to-transparent md:block" />

    <div class="mx-auto max-w-6xl px-6 py-20 sm:py-28">
      <p
        v-if="eyebrow"
        class="font-display text-sm font-semibold uppercase tracking-[0.25em] text-slypn-500"
      >
        {{ eyebrow }}
      </p>
      <h1 class="mt-3 max-w-3xl text-4xl font-extrabold text-slypn-700 sm:text-5xl md:text-6xl">
        {{ title }}
      </h1>
      <p
        v-if="subtitle"
        class="mt-6 max-w-2xl text-lg text-slypn-900/80"
      >
        {{ subtitle }}
      </p>

      <div v-if="$slots.actions" class="mt-10 flex flex-wrap gap-3">
        <slot name="actions" />
      </div>

      <div v-if="$slots.default" class="mt-10">
        <slot />
      </div>
    </div>
  </section>
</template>
