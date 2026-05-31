<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import HeroBanner from '@/components/common/HeroBanner.vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const error = ref<string | null>(null)

const returnTo = computed(() => {
  const q = route.query.returnTo
  return typeof q === 'string' && q.startsWith('/') ? q : '/'
})

async function signIn() {
  error.value = null
  try {
    await auth.login(window.location.origin + returnTo.value)
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  }
}
</script>

<template>
  <HeroBanner
    eyebrow="Sign in"
    title="Sign in to SLYPN"
    subtitle="Sign-in uses Entra External ID with Google or Facebook as social options. Public content is available without signing in; signing in unlocks members-only features, drafts, and admin."
  />

  <section class="mx-auto max-w-2xl px-6 py-16">
    <div v-if="auth.isAuthenticated" class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <p class="font-display text-lg font-semibold text-slypn-700">
        You&rsquo;re already signed in as {{ auth.displayName }}.
      </p>
      <p class="mt-2 text-sm text-slypn-900/75">
        Use the menu in the top right to view your editor or admin tools, or to sign out.
      </p>
    </div>

    <div v-else-if="auth.isConfigured">
      <button
        type="button"
        class="inline-flex items-center gap-2 rounded-md bg-slypn-600 px-6 py-3 text-base font-semibold text-white shadow-sm hover:bg-slypn-700"
        @click="signIn"
      >
        Continue with Entra External ID
      </button>
      <p class="mt-3 text-sm text-slypn-900/65">
        You&rsquo;ll be redirected to the SLYPN sign-in page where you can use email + password, Google, or Facebook.
      </p>
      <p v-if="error" class="mt-4 rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">{{ error }}</p>
    </div>

    <div v-else class="rounded-xl border border-amber-200 bg-amber-50 p-6 text-sm text-amber-900">
      <p class="font-display font-semibold">Sign-in is not configured in this environment.</p>
      <p class="mt-2">
        Set <code class="rounded bg-amber-100 px-1.5 py-0.5">VITE_MSAL_CLIENT_ID</code>,
        <code class="rounded bg-amber-100 px-1.5 py-0.5">VITE_MSAL_AUTHORITY</code>, and
        <code class="rounded bg-amber-100 px-1.5 py-0.5">VITE_API_SCOPE</code> in <code>src/web/.env.local</code>.
        See <a class="underline" href="https://github.com/sinclapa/slypn/blob/main/docs/auth-setup.md" rel="noopener" target="_blank">docs/auth-setup.md</a>.
      </p>
    </div>
  </section>
</template>
