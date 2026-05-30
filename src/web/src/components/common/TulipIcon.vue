<script setup lang="ts">
defineProps<{ class?: string }>()
</script>

<template>
  <svg
    viewBox="0 0 32 32"
    aria-hidden="true"
    fill="currentColor"
    :class="$attrs.class as string"
  >
    <path d="M16 4c-3.2 2.4-5 5.4-5 8.5 0 1.6.6 3 1.7 4H10c-.7 0-1 .5-.6 1.1l5.7 7.6c.4.5 1.1.5 1.5 0l5.8-7.6c.4-.6.1-1.1-.6-1.1h-2.7c1-1 1.7-2.4 1.7-4 0-3-1.8-6.1-5-8.5z" />
  </svg>
</template>
