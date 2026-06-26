<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { apiFetch } from '@/lib/api'
import { EVENT_TYPES } from '@/lib/eventTypes'
import type { CommunityEvent } from '@/types/content'

const props = defineProps<{
  open: boolean
  event: CommunityEvent | null   // null = add, populated = edit
}>()

const emit = defineEmits<{
  close: []
  saved: [event: CommunityEvent]
}>()

// ── form state ────────────────────────────────────────────────────────────────

const title       = ref('')
const type        = ref<string>('Coffee meet-up')
const startsAt    = ref('')
const endsAt      = ref('')
const location    = ref('')
const description = ref('')
const signupUrl   = ref('')
const submitting  = ref(false)
const error       = ref<string | null>(null)

// Offer the event's stored type as an option even when it doesn't exactly match
// EVENT_TYPES (e.g. legacy or differently-cased values like "Coffee Meet-up"),
// so editing an existing event always shows its current type.
const typeOptions = computed(() => {
  const opts = [...EVENT_TYPES] as string[]
  if (type.value && !opts.includes(type.value)) opts.unshift(type.value)
  return opts
})

function toDatetimeLocal(iso: string): string {
  const d = new Date(iso)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

// Populate form whenever the dialog opens or the event prop changes
watch(
  () => [props.open, props.event] as const,
  ([open, ev]) => {
    if (!open) return
    error.value = null
    if (ev) {
      title.value       = ev.title
      type.value        = ev.type
      startsAt.value    = toDatetimeLocal(ev.startsAt)
      endsAt.value      = toDatetimeLocal(ev.endsAt)
      location.value    = ev.location
      description.value = ev.description
      signupUrl.value   = ev.signupUrl ?? ''
    } else {
      title.value       = ''
      type.value        = 'Coffee meet-up'
      startsAt.value    = ''
      endsAt.value      = ''
      location.value    = ''
      description.value = ''
      signupUrl.value   = ''
    }
  },
  { immediate: true },
)

// ── submit ────────────────────────────────────────────────────────────────────

async function submit() {
  if (submitting.value) return
  error.value = null
  submitting.value = true

  const body = JSON.stringify({
    title:       title.value.trim(),
    type:        type.value,
    startsAt:    new Date(startsAt.value).toISOString(),
    endsAt:      new Date(endsAt.value).toISOString(),
    location:    location.value.trim(),
    description: description.value.trim(),
    signupUrl:   signupUrl.value.trim() || undefined,
  })

  try {
    const isEdit = props.event !== null
    const url    = isEdit ? `/events/${props.event!.id}` : '/events'
    const method = isEdit ? 'PUT' : 'POST'
    const headers: Record<string, string> = {}
    if (isEdit && props.event!._etag) headers['If-Match'] = `"${props.event!._etag}"`

    const resp = await apiFetch(url, { method, body, headers })
    if (!resp.ok) {
      const text = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${text ? ` — ${text}` : ''}`)
    }
    const saved = await resp.json() as CommunityEvent
    emit('saved', saved)
    emit('close')
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div
        v-if="open"
        class="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 px-4 py-12"
        @click.self="emit('close')"
      >
        <div class="w-full max-w-xl rounded-2xl bg-white shadow-2xl">
          <!-- header -->
          <div class="flex items-center justify-between border-b border-slypn-100 px-6 py-4">
            <h2 class="font-display text-lg font-bold text-slypn-700">
              {{ event ? 'Edit event' : 'Add event' }}
            </h2>
            <button
              type="button"
              class="rounded-md p-1 text-slypn-400 hover:bg-slypn-50 hover:text-slypn-600"
              aria-label="Close"
              @click="emit('close')"
            >&times;</button>
          </div>

          <!-- form -->
          <form class="space-y-4 px-6 py-5" @submit.prevent="submit">
            <div>
              <label class="block text-sm font-medium text-slypn-800">Title</label>
              <input
                v-model="title"
                type="text"
                required
                maxlength="200"
                class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-slypn-800">Type</label>
              <select
                v-model="type"
                required
                class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              >
                <option v-for="t in typeOptions" :key="t" :value="t">{{ t }}</option>
              </select>
            </div>

            <div class="grid grid-cols-2 gap-4">
              <div>
                <label class="block text-sm font-medium text-slypn-800">Starts at</label>
                <input
                  v-model="startsAt"
                  type="datetime-local"
                  required
                  class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
                />
              </div>
              <div>
                <label class="block text-sm font-medium text-slypn-800">Ends at</label>
                <input
                  v-model="endsAt"
                  type="datetime-local"
                  required
                  class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
                />
              </div>
            </div>

            <div>
              <label class="block text-sm font-medium text-slypn-800">Location</label>
              <input
                v-model="location"
                type="text"
                required
                maxlength="200"
                class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-slypn-800">Description</label>
              <textarea
                v-model="description"
                required
                maxlength="2000"
                rows="4"
                class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-slypn-800">
                Sign-up URL <span class="font-normal text-slypn-400">(optional)</span>
              </label>
              <input
                v-model="signupUrl"
                type="url"
                class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>

            <p v-if="error" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">{{ error }}</p>

            <!-- actions -->
            <div class="flex justify-end gap-3 border-t border-slypn-100 pt-4">
              <button
                type="button"
                class="rounded-md border border-slypn-200 px-4 py-2 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
                @click="emit('close')"
              >Cancel</button>
              <button
                type="submit"
                class="rounded-md bg-slypn-600 px-5 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
                :disabled="submitting || !title || !startsAt || !endsAt || !location || !description"
              >
                {{ submitting ? 'Saving…' : (event ? 'Save changes' : 'Add event') }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.dialog-enter-active, .dialog-leave-active { transition: opacity 0.15s ease; }
.dialog-enter-from, .dialog-leave-to { opacity: 0; }
</style>
