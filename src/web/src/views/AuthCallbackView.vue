<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

onMounted(async () => {
  await auth.initialize()
  // MSAL.handleRedirectPromise already consumed the hash. Send the user home.
  await router.replace({ name: 'home' })
})
</script>

<template>
  <main class="flex min-h-[40vh] items-center justify-center px-6 py-20">
    <p class="font-display text-slypn-700">Signing you in&hellip;</p>
  </main>
</template>
