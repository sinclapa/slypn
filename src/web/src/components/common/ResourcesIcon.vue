<script setup lang="ts">
// Bookshelf with a mixed row of books, one leaning, outlined — resources
// icon: a curated shelf of things to read.
// Monochrome via currentColor; inherits surrounding text colour.
</script>

<template>
  <svg viewBox="0 0 280 200" fill="none" stroke="currentColor" stroke-width="6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path d="M8 182 H272" />
    <path d="M8 182 V194" />
    <path d="M272 182 V194" />

    <rect x="20" y="70" width="26" height="112" rx="3" />
    <path d="M20 88 H46" stroke-width="4" />

    <rect x="50" y="94" width="20" height="88" rx="3" />

    <rect x="74" y="40" width="48" height="142" rx="3" />
    <path d="M74 60 H122" stroke-width="4" />

    <path d="M128 182 L152 182 L164 92 L140 92 Z" />
    <path d="M143 108 L161 106" stroke-width="4" />

    <rect x="170" y="76" width="26" height="106" rx="3" />

    <rect x="200" y="54" width="40" height="128" rx="3" />
    <path d="M200 72 H240" stroke-width="4" />
    <path d="M200 96 H240" stroke-width="4" />
  </svg>
</template>
