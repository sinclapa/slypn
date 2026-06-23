<script setup lang="ts">
import AppNav from '@/components/layout/AppNav.vue'
import AppFooter from '@/components/layout/AppFooter.vue'
import CookieBanner from '@/components/common/CookieBanner.vue'
import DevPersonaSwitcher from '@/components/common/DevPersonaSwitcher.vue'
</script>

<template>
  <div class="flex min-h-screen flex-col bg-slypn-50">
    <AppNav />
    <RouterView class="flex-1" />
    <AppFooter />
    <CookieBanner />
    <DevPersonaSwitcher />
  </div>
</template>
