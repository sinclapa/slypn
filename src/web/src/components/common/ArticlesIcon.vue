<script setup lang="ts">
// Article page with a folded corner, an image placeholder (sun + mountains),
// headline and body lines, and a pencil with a small heart — articles icon:
// a piece being written and illustrated by a member. Outlined to match the
// rest of the hero icon set; small interior glyphs (sun, mountains, heart)
// are solid accents.
// Monochrome via currentColor; inherits surrounding text colour.
</script>

<template>
  <svg viewBox="0 0 280 200" fill="none" stroke="currentColor" stroke-width="6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <!-- Page with a folded top-right corner -->
    <path d="M76 8 L175 8 L200 36 L200 182 A10 10 0 0 1 190 192 L76 192 A10 10 0 0 1 66 182 L66 18 A10 10 0 0 1 76 8 Z" />
    <path d="M175 8 V36 H200" stroke-width="4" />

    <!-- Image placeholder: frame, sun, two overlapping mountains -->
    <rect x="80" y="36" width="55" height="41" rx="3" stroke-width="4" />
    <circle cx="124" cy="47" r="4" fill="currentColor" stroke="none" />
    <path d="M95 49 L82 71 L107 71 Z" fill="currentColor" stroke="none" />
    <path d="M116 54 L107 71 L124 71 Z" fill="currentColor" stroke="none" />

    <!-- Heading lines beside the placeholder -->
    <path d="M142 53 H162" stroke-width="4" />
    <path d="M142 59 H169" stroke-width="4" />
    <path d="M142 66 H159" stroke-width="4" />

    <!-- Title bar -->
    <path d="M80 93 H184" stroke-width="9" />

    <!-- Body copy -->
    <path d="M80 118 H168" stroke-width="4" />
    <path d="M80 128 H175" stroke-width="4" />
    <path d="M80 138 H173" stroke-width="4" />
    <path d="M80 148 H161" stroke-width="4" />
    <path d="M80 158 H154" stroke-width="4" />
    <path d="M80 168 H144" stroke-width="4" />

    <!-- Pencil, tip resting on the page -->
    <path d="M182 181 L195 170 L224 111 A7 7 0 0 1 212 105 L183 165 Z" />
    <path d="M219 122 L207 116" stroke-width="4" />
    <path d="M196 150 C194 148 190 149 190 153 C190 156 196 161 196 161 C196 161 202 156 202 153 C202 149 198 148 196 150 Z" fill="currentColor" stroke="none" />
  </svg>
</template>
