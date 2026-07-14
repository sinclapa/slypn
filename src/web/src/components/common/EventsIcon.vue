<script setup lang="ts">
// Wall calendar with binder loops, a full date grid, and a highlighted
// meet-up date, outlined — events icon.
// Monochrome via currentColor; inherits surrounding text colour.
</script>

<template>
  <svg viewBox="0 0 280 200" fill="none" stroke="currentColor" stroke-width="6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path d="M56 34 V18 A8 8 0 0 1 72 18 V34" />
    <path d="M208 34 V18 A8 8 0 0 1 224 18 V34" />
    <rect x="20" y="24" width="240" height="164" rx="18" />
    <path d="M20 66 H260" />
    <path d="M80 66 V188" stroke-width="3" />
    <path d="M140 66 V188" stroke-width="3" />
    <path d="M200 66 V188" stroke-width="3" />
    <path d="M20 106 H260" stroke-width="3" />
    <path d="M20 146 H260" stroke-width="3" />
    <circle cx="170" cy="126" r="16" />
    <circle cx="170" cy="126" r="4" fill="currentColor" stroke="none" />
  </svg>
</template>
