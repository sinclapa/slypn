<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import TulipIcon from '@/components/common/TulipIcon.vue'

const navItems = [
  { to: '/',           label: 'Home' },
  { to: '/about',      label: 'About' },
  { to: '/articles',   label: 'Articles' },
  { to: '/blog',       label: 'Blog' },
  { to: '/events',     label: 'Events' },
  { to: '/resources',  label: 'Resources' },
  { to: '/newsletter', label: 'Newsletter' },
]

const mobileOpen = ref(false)
</script>

<template>
  <header class="sticky top-0 z-40 border-b border-slypn-100 bg-white/85 backdrop-blur">
    <div class="mx-auto flex max-w-6xl items-center justify-between gap-6 px-6 py-4">
      <RouterLink
        to="/"
        class="flex items-center gap-2 font-display text-lg font-extrabold text-slypn-700"
        @click="mobileOpen = false"
      >
        <TulipIcon class="h-7 w-7 text-slypn-500" />
        <span>SLYPN</span>
      </RouterLink>

      <nav class="hidden items-center gap-1 md:flex" aria-label="Primary">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="rounded-md px-3 py-2 text-sm font-medium text-slypn-700 transition-colors hover:bg-slypn-50 hover:text-slypn-900"
          active-class="bg-slypn-50 text-slypn-900"
          exact-active-class="bg-slypn-100 text-slypn-900"
        >
          {{ item.label }}
        </RouterLink>
      </nav>

      <div class="hidden md:block">
        <RouterLink
          to="/login"
          class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-slypn-700"
        >
          Sign in
        </RouterLink>
      </div>

      <button
        type="button"
        class="inline-flex items-center justify-center rounded-md p-2 text-slypn-700 hover:bg-slypn-50 md:hidden"
        :aria-expanded="mobileOpen"
        aria-controls="mobile-nav"
        aria-label="Toggle navigation menu"
        @click="mobileOpen = !mobileOpen"
      >
        <svg v-if="!mobileOpen" class="h-6 w-6" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" d="M4 6h16M4 12h16M4 18h16" />
        </svg>
        <svg v-else class="h-6 w-6" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" d="M6 6l12 12M6 18L18 6" />
        </svg>
      </button>
    </div>

    <nav
      v-show="mobileOpen"
      id="mobile-nav"
      class="border-t border-slypn-100 bg-white md:hidden"
      aria-label="Mobile primary"
    >
      <div class="mx-auto flex max-w-6xl flex-col gap-1 px-4 py-3">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="rounded-md px-3 py-2 text-base font-medium text-slypn-800 hover:bg-slypn-50"
          active-class="bg-slypn-50"
          @click="mobileOpen = false"
        >
          {{ item.label }}
        </RouterLink>
        <RouterLink
          to="/login"
          class="mt-1 rounded-md bg-slypn-600 px-3 py-2 text-center text-base font-semibold text-white hover:bg-slypn-700"
          @click="mobileOpen = false"
        >
          Sign in
        </RouterLink>
      </div>
    </nav>
  </header>
</template>
