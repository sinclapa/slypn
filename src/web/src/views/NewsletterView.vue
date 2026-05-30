<script setup lang="ts">
import { ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import NewsletterCard from '@/components/common/NewsletterCard.vue'
import { mockNewsletters } from '@/mock/newsletters'

const email = ref('')
const submitted = ref(false)

function subscribe() {
  if (!email.value) return
  submitted.value = true
  email.value = ''
}
</script>

<template>
  <HeroBanner
    eyebrow="Newsletter"
    title="A monthly note from the SLYPN team"
    subtitle="Meet-up dates, a featured article, fundraising progress, and the odd member story. About five minutes to read. Free, no tracking."
  >
    <template #actions>
      <form
        class="flex w-full max-w-md flex-col gap-2 sm:flex-row"
        @submit.prevent="subscribe"
      >
        <input
          v-model="email"
          type="email"
          required
          placeholder="you@example.com"
          aria-label="Email address"
          class="flex-1 rounded-md border border-slypn-200 bg-white px-4 py-2.5 text-sm text-slypn-900 shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
        <button
          type="submit"
          class="rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-slypn-700"
        >
          Subscribe
        </button>
      </form>
      <p v-if="submitted" class="mt-3 text-sm text-slypn-600">
        Thank you &mdash; we&rsquo;ll add you to the next issue. (This is a mock form for now.)
      </p>
    </template>
  </HeroBanner>

  <section class="mx-auto max-w-4xl px-6 py-16">
    <h2 class="font-display text-2xl font-bold text-slypn-700">Past issues</h2>
    <div class="mt-6 grid gap-5 sm:grid-cols-2">
      <NewsletterCard
        v-for="n in mockNewsletters"
        :key="n.id"
        :newsletter="n"
      />
    </div>
  </section>
</template>
