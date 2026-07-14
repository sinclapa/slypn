<script setup lang="ts">
import { useCookieConsent } from '@/composables/useCookieConsent'

const { choice, accept, decline } = useCookieConsent()
</script>

<template>
  <Teleport to="body">
    <dialog
      v-if="choice === null"
      open
      aria-labelledby="cookie-title"
      aria-describedby="cookie-desc"
      class="fixed inset-x-0 bottom-0 z-50 m-0 max-w-none border-0 border-t border-slypn-100 bg-white/95 p-0 text-slypn-900 shadow-lg backdrop-blur"
    >
      <div class="page-container flex flex-col gap-4 py-5 md:flex-row md:items-center md:justify-between">
        <div class="text-sm text-slypn-900/85">
          <p id="cookie-title" class="font-display font-semibold text-slypn-700">
            We&rsquo;d like to use cookies
          </p>
          <p id="cookie-desc" class="mt-1 max-w-2xl">
            We use a small number of cookies to understand how the site is used and to improve it.
            No tracking is loaded until you accept. You can change your mind at any time.
          </p>
        </div>
        <div class="flex shrink-0 gap-2">
          <button
            type="button"
            class="rounded-md border border-slypn-200 bg-white px-4 py-2 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
            @click="decline"
          >
            Decline
          </button>
          <button
            type="button"
            class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white hover:bg-slypn-700"
            @click="accept"
          >
            Accept all
          </button>
        </div>
      </div>
    </dialog>
  </Teleport>
</template>
