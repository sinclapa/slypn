<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import { apiFetch, apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'

interface Subscriber {
  id: string
  email: string
  displayName: string
  subscribedAt: string
  _etag?: string
}

// ── Subscriber list ────────────────────────────────────────────────────────

const { data: subscribers, loading, error, refresh } = useAsyncData(
  () => apiJson<Subscriber[]>('/subscribers'),
)

// Newest first, matching the order the API returns them in.
const sortedSubscribers = computed(() =>
  [...(subscribers.value ?? [])].sort((a, b) =>
    b.subscribedAt.localeCompare(a.subscribedAt)))

// ── Remove ─────────────────────────────────────────────────────────────────

const deletingId = ref<string | null>(null)
const saveError = ref<string | null>(null)

async function removeSubscriber(subscriber: Subscriber) {
  if (!confirm(`Remove ${subscriber.email} from the newsletter list? This cannot be undone.`)) return
  if (deletingId.value) return
  saveError.value = null
  deletingId.value = subscriber.id
  try {
    const resp = await apiFetch(`/subscribers/${subscriber.id}`, {
      method: 'DELETE',
      headers: subscriber._etag ? { 'If-Match': subscriber._etag } : {},
    })
    if (!resp.ok) throw new Error(`${resp.status} ${await resp.text()}`)
    await refresh()
  } catch (err) {
    saveError.value = err instanceof Error ? err.message : String(err)
  } finally {
    deletingId.value = null
  }
}

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="Newsletter subscribers"
    subtitle="Everyone who signed up for the newsletter. Subscribers are not members and cannot sign in."
  >
    <template #actions>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="text-sm font-semibold text-white/80 hover:text-white"
      >&larr; Dashboard</RouterLink>
    </template>
  </HeroBanner>

  <section class="page-container space-y-6 py-16">
    <article class="rounded-xl border border-slypn-100 bg-white shadow-sm">
      <div class="border-b border-slypn-100 px-6 py-4">
        <h2 class="font-display text-xl font-bold text-slypn-700">
          All subscribers
          <span v-if="subscribers?.length" class="ml-1 text-sm font-medium text-slypn-500">
            ({{ subscribers.length }})
          </span>
        </h2>
      </div>

      <p v-if="loading && !subscribers" class="px-6 py-8 text-center text-sm text-slypn-900/60">
        Loading subscribers…
      </p>

      <div v-else-if="error" class="px-6 py-4 text-sm text-rose-700">
        Couldn't load subscribers: {{ error }}.
        <button class="ml-2 underline" @click="refresh">Retry</button>
      </div>

      <p v-else-if="saveError" data-testid="subscriber-save-error" class="bg-rose-50 px-6 py-3 text-sm text-rose-700">
        {{ saveError }}
        <button class="ml-2 underline" @click="saveError = null">Dismiss</button>
      </p>

      <div v-if="subscribers?.length" class="divide-y divide-slypn-100">
        <div
          v-for="s in sortedSubscribers"
          :key="s.id"
          data-testid="subscriber-row"
          :data-id="s.id"
          class="flex flex-col gap-3 px-6 py-4 sm:flex-row sm:items-center sm:gap-6"
        >
          <div class="min-w-0 flex-1">
            <p class="truncate font-semibold text-slypn-800">{{ s.email }}</p>
            <p v-if="s.displayName !== s.email" class="mt-0.5 truncate text-sm text-slypn-500">
              {{ s.displayName }}
            </p>
            <p class="mt-0.5 text-xs text-slypn-400">Subscribed {{ fmtDate(s.subscribedAt) }}</p>
          </div>

          <button
            type="button"
            data-testid="subscriber-remove"
            :disabled="deletingId === s.id"
            class="shrink-0 self-start rounded-md border border-rose-200 px-3 py-1.5 text-xs font-medium text-rose-600 hover:bg-rose-50 disabled:opacity-50 sm:self-auto"
            @click="removeSubscriber(s)"
          >{{ deletingId === s.id ? 'Removing…' : 'Remove' }}</button>
        </div>
      </div>

      <p v-else-if="subscribers" class="px-6 py-8 text-center text-sm text-slypn-900/60">
        No subscribers yet.
      </p>
    </article>
  </section>
</template>
