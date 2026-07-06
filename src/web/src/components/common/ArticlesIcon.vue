<script setup lang="ts">
// Open book with ruled text-line cutouts — articles icon.
// Monochrome via currentColor; inherits surrounding text colour.
</script>

<template>
  <svg viewBox="0 0 280 200" fill="currentColor" stroke="none" aria-hidden="true">
    <!-- Left page with text-line cutouts (evenodd punches through the fill) -->
    <path
      fill-rule="evenodd"
      d="M0 22 C0 9 9 0 22 0 L130 0 L130 182 Q65 192 0 180 Z
         M16 38 L114 38 L114 46 L16 46 Z
         M16 58 L114 58 L114 66 L16 66 Z
         M16 78 L114 78 L114 86 L16 86 Z
         M16 98 L114 98 L114 106 L16 106 Z
         M16 118 L114 118 L114 126 L16 126 Z
         M16 138 L114 138 L114 146 L16 146 Z
         M16 158 L72 158 L72 166 L16 166 Z"
    />
    <!-- Right page with text-line cutouts -->
    <path
      fill-rule="evenodd"
      d="M150 0 L258 0 C271 0 280 9 280 22 L280 180 Q215 192 150 182 Z
         M166 38 L264 38 L264 46 L166 46 Z
         M166 58 L264 58 L264 66 L166 66 Z
         M166 78 L264 78 L264 86 L166 86 Z
         M166 98 L264 98 L264 106 L166 106 Z
         M166 118 L264 118 L264 126 L166 126 Z
         M166 136 L264 136 L264 144 L166 144 Z
         M166 156 L220 156 L220 164 L166 164 Z"
    />
    <!-- Spine -->
    <rect x="130" y="0" width="20" height="182" rx="3" />
  </svg>
</template>
