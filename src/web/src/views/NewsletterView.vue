<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import NewsletterIcon from '@/components/common/NewsletterIcon.vue'
import NewsletterCard from '@/components/common/NewsletterCard.vue'
import { apiErrorMessage, apiFetch, apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Newsletter } from '@/types/content'

const email = ref('')
const submitting = ref(false)
const submitted = ref(false)
const submitError = ref<string | null>(null)

// API already returns newsletters newest-first (OrderByDescending IssueDate).
const { data: newsletters, loading, error, refresh } = useAsyncData(
  () => apiJson<Newsletter[]>('/newsletters'),
)

const latestIssue = computed(() => newsletters.value?.[0] ?? null)
const pastIssues = computed(() => (newsletters.value ?? []).slice(1))

async function subscribe() {
  if (!email.value || submitting.value) return
  submitting.value = true
  submitError.value = null
  try {
    const resp = await apiFetch('/newsletter/subscribe', {
      method: 'POST',
      body: JSON.stringify({ email: email.value.trim() }),
    })
    if (!resp.ok) throw new Error(await apiErrorMessage(resp))
    submitted.value = true
    email.value = ''
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <HeroBanner
    eyebrow="Newsletter"
    title="A monthly note from the SLYPN team"
    subtitle="Meet-up dates, a featured article, fundraising progress, and the odd member story. About five minutes to read. Free, no tracking."
  >
    <template #brand>
      <NewsletterIcon class="w-36 text-slypn-700 sm:w-64 md:w-72" />
    </template>

    <template #actions>
      <form
        data-testid="subscribe-form"
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
          data-testid="subscribe-submit"
          class="rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-slypn-700 disabled:opacity-50"
          :disabled="submitting || !email"
        >
          {{ submitting ? 'Subscribing…' : 'Subscribe' }}
        </button>
      </form>
      <p v-if="submitted" data-testid="subscribe-result" class="mt-3 text-sm text-emerald-700">
        Thank you &mdash; you&rsquo;re on the list. The next issue will land in your inbox.
      </p>
      <p v-else-if="submitError" data-testid="subscribe-error" class="mt-3 text-sm text-rose-700">
        Couldn&rsquo;t subscribe: {{ submitError }}
      </p>
    </template>
  </HeroBanner>

  <section class="page-container py-16">
    <p v-if="loading && !newsletters" class="text-slypn-900/70">Loading&hellip;</p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load newsletters: {{ error }}.
      <button type="button" class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <p v-else-if="!newsletters?.length" class="text-slypn-900/70">
      No newsletters yet.
    </p>

    <template v-else>
      <div>
        <h2 class="font-display text-2xl font-bold text-slypn-700">Latest issue</h2>
        <div class="mt-6 max-w-xl">
          <NewsletterCard v-if="latestIssue" :newsletter="latestIssue" />
        </div>
      </div>

      <div v-if="pastIssues.length" class="mt-16">
        <h2 class="font-display text-2xl font-bold text-slypn-700">Past issues</h2>
        <div class="mt-6 grid gap-5 sm:grid-cols-2">
          <NewsletterCard
            v-for="n in pastIssues"
            :key="n.id"
            :newsletter="n"
          />
        </div>
      </div>
    </template>
  </section>
</template>
