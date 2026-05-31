<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import RichTextEditor from '@/components/editor/RichTextEditor.vue'
import SaveIndicator from '@/components/editor/SaveIndicator.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { apiFetch } from '@/lib/api'

interface DraftPayload {
  type: 'article' | 'blog'
  title: string
  slug: string
  summary: string
  body: string
  category: string
  tags: string[]
  readingMinutes: number
}

const DRAFT_ID_KEY = 'slypn:editor:draft-id'

function makeId() {
  if ('randomUUID' in crypto) return crypto.randomUUID().replace(/-/g, '')
  return Math.random().toString(16).slice(2).padEnd(32, '0')
}

const draftId = ref<string>(localStorage.getItem(DRAFT_ID_KEY) ?? makeId())
localStorage.setItem(DRAFT_ID_KEY, draftId.value)

const draft = ref<DraftPayload>({
  type: 'article',
  title: '',
  slug: '',
  summary: '',
  body: '<p>Start writing&hellip;</p>',
  category: '',
  tags: [],
  readingMinutes: 0,
})
const uploadError = ref<string | null>(null)

async function loadExisting() {
  try {
    const resp = await apiFetch(`/drafts/${draftId.value}`)
    if (resp.status === 404) return
    if (!resp.ok) return
    const fetched = await resp.json() as DraftPayload
    draft.value = { ...draft.value, ...fetched }
  } catch {
    // Network errors are fine on first load — we'll create on first autosave.
  }
}

async function save(value: DraftPayload) {
  const resp = await apiFetch(`/drafts/${draftId.value}`, {
    method: 'PUT',
    body: JSON.stringify(value),
  })
  if (!resp.ok) {
    const body = await resp.text().catch(() => '')
    throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
  }
}

function startFresh() {
  draftId.value = makeId()
  localStorage.setItem(DRAFT_ID_KEY, draftId.value)
  draft.value = {
    type: 'article',
    title: '',
    slug: '',
    summary: '',
    body: '<p>Start writing&hellip;</p>',
    category: '',
    tags: [],
    readingMinutes: 0,
  }
}

onMounted(loadExisting)

const { status, lastSavedAt, errorMessage } = useAutoSave(draft, save, { debounce: 1500 })

const tagsCsv = computed({
  get: () => draft.value.tags.join(', '),
  set: (v: string) => { draft.value.tags = v.split(',').map(s => s.trim()).filter(Boolean) },
})
</script>

<template>
  <HeroBanner
    eyebrow="Editor"
    title="Write something for the community"
    subtitle="Edits autosave 1.5s after you stop typing. Submitting for admin review lands in #28."
  >
    <template #actions>
      <button
        type="button"
        class="rounded-md border border-slypn-200 bg-white px-4 py-2 text-sm font-semibold text-slypn-700 hover:bg-slypn-50"
        @click="startFresh"
      >
        Start a new draft
      </button>
    </template>
  </HeroBanner>

  <section class="mx-auto max-w-3xl space-y-6 px-6 py-12">
    <div class="flex items-center justify-between">
      <p class="font-display text-xs uppercase tracking-widest text-slypn-500">
        Draft <code class="text-slypn-700">{{ draftId }}</code>
      </p>
      <SaveIndicator :status="status" :last-saved-at="lastSavedAt" :error="errorMessage" />
    </div>

    <div class="space-y-4 rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <div>
        <label class="block text-sm font-medium text-slypn-800">Title</label>
        <input
          v-model="draft.title"
          type="text"
          maxlength="200"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="block text-sm font-medium text-slypn-800">Slug</label>
          <input
            v-model="draft.slug"
            type="text"
            maxlength="120"
            placeholder="my-article-title"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-slypn-800">Category</label>
          <input
            v-model="draft.category"
            type="text"
            maxlength="60"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
      </div>
      <div>
        <label class="block text-sm font-medium text-slypn-800">Summary</label>
        <textarea
          v-model="draft.summary"
          maxlength="500"
          rows="2"
          class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
        />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="block text-sm font-medium text-slypn-800">Tags (comma-separated)</label>
          <input
            v-model="tagsCsv"
            type="text"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-slypn-800">Reading minutes</label>
          <input
            v-model.number="draft.readingMinutes"
            type="number"
            min="0"
            max="60"
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
      </div>
    </div>

    <RichTextEditor
      v-model="draft.body"
      @upload-error="(msg) => uploadError = msg"
    />

    <p v-if="uploadError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Image upload failed: {{ uploadError }}
    </p>
  </section>
</template>
