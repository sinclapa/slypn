<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import { apiFetch, apiJson, apiErrorMessage } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import FieldCounter from '@/components/common/FieldCounter.vue'

interface Newsletter {
  id: string
  title: string
  issueDate: string
  summary: string
  topics: string[]
  fileName?: string
  _etag?: string
}

const { data: newsletters, loading, error, refresh } = useAsyncData(
  () => apiJson<Newsletter[]>('/newsletters'),
)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })

// ── Add / edit dialog ────────────────────────────────────────────────────────
const showForm  = ref(false)
const editing   = ref<Newsletter | null>(null)
// Field caps, mirroring NewsletterInput server-side, so the counters and the maxlength
// attributes cannot drift from it. `topics` is the raw comma-separated input; the server
// caps each topic it is split into.
const LIMITS = { title: 200, summary: 1_000, topic: 60, topics: 600 } as const

const form      = ref({ title: '', issueDate: '', summary: '', topics: '' })
const file      = ref<File | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const saving    = ref(false)
const formError = ref<string | null>(null)

function openAdd() {
  editing.value = null
  form.value = { title: '', issueDate: '', summary: '', topics: '' }
  file.value = null
  formError.value = null
  showForm.value = true
}

function openEdit(n: Newsletter) {
  editing.value = n
  form.value = {
    title:     n.title,
    issueDate: n.issueDate.slice(0, 10),
    summary:   n.summary,
    topics:    n.topics.join(', '),
  }
  file.value = null
  formError.value = null
  showForm.value = true
}

function onFileChosen(event: Event) {
  const target = event.target as HTMLInputElement
  file.value = target.files?.[0] ?? null
}

const canSave = computed(() =>
  form.value.title.trim() && form.value.issueDate.trim() && form.value.summary.trim())

async function save() {
  if (!canSave.value || saving.value) return
  saving.value = true
  formError.value = null
  try {
    const body = JSON.stringify({
      title:     form.value.title.trim(),
      issueDate: form.value.issueDate,
      summary:   form.value.summary.trim(),
      topics:    form.value.topics.split(',').map(t => t.trim()).filter(Boolean),
    })
    const resp = editing.value
      ? await apiFetch(`/newsletters/${editing.value.id}`, {
          method: 'PUT',
          body,
          headers: editing.value._etag ? { 'If-Match': editing.value._etag } : {},
        })
      : await apiFetch('/newsletters', { method: 'POST', body })
    if (!resp.ok) throw new Error(await apiErrorMessage(resp))
    const saved = await resp.json() as Newsletter

    if (file.value) {
      const fd = new FormData()
      fd.append('file', file.value)
      const fileResp = await apiFetch(`/newsletters/${saved.id}/file`, {
        method: 'PUT',
        body: fd,
        headers: saved._etag ? { 'If-Match': saved._etag } : {},
      })
      if (!fileResp.ok) throw new Error(await apiErrorMessage(fileResp))
    }

    showForm.value = false
    await refresh()
  } catch (err) {
    formError.value = err instanceof Error ? err.message : String(err)
  } finally {
    saving.value = false
  }
}

// ── Delete ───────────────────────────────────────────────────────────────────
const deletingId = ref<string | null>(null)
const listError  = ref<string | null>(null)

async function remove(n: Newsletter) {
  if (!confirm(`Delete "${n.title}"? This cannot be undone.`)) return
  deletingId.value = n.id
  listError.value = null
  try {
    const resp = await apiFetch(`/newsletters/${n.id}`, {
      method: 'DELETE',
      headers: n._etag ? { 'If-Match': n._etag } : {},
    })
    if (!resp.ok) throw new Error(await apiErrorMessage(resp))
    await refresh()
  } catch (err) {
    listError.value = err instanceof Error ? err.message : String(err)
  } finally {
    deletingId.value = null
  }
}
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="Newsletters"
    subtitle="Add, edit, and remove newsletter issues, and attach their PDF/DOCX files."
  >
    <template #actions>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="text-sm font-semibold text-white/80 hover:text-white"
      >&larr; Dashboard</RouterLink>
    </template>
  </HeroBanner>

  <section class="page-container space-y-6 py-16">
    <div class="flex items-center justify-between">
      <h2 class="font-display text-xl font-bold text-slypn-700">All newsletters</h2>
      <button
        type="button"
        class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
        data-testid="newsletter-add"
        @click="openAdd"
      >+ Add newsletter</button>
    </div>

    <p v-if="loading && !newsletters" class="text-sm text-slypn-900/60">Loading newsletters…</p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load newsletters: {{ error }}.
      <button type="button" class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <p v-if="listError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      {{ listError }}
      <button type="button" class="ml-2 underline" @click="listError = null">Dismiss</button>
    </p>

    <div class="divide-y divide-slypn-100 rounded-xl border border-slypn-100 bg-white shadow-sm">
      <div
        v-for="n in newsletters"
        :key="n.id"
        data-testid="newsletter-row"
        :data-id="n.id"
        class="flex items-start gap-4 px-5 py-4"
      >
        <div class="min-w-0 flex-1">
          <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
            {{ formatDate(n.issueDate) }}
          </p>
          <p class="mt-1 font-semibold text-slypn-800">{{ n.title }}</p>
          <p class="mt-0.5 text-sm text-slypn-900/75">{{ n.summary }}</p>
          <div v-if="n.fileName" class="mt-1 flex items-center gap-3">
            <RouterLink
              :to="{ name: 'newsletter-detail', params: { id: n.id } }"
              class="text-xs font-semibold text-slypn-600 underline underline-offset-2 hover:text-slypn-700"
            >View</RouterLink>
            <a
              :href="`/api/newsletters/${n.id}/file`"
              class="text-xs text-slypn-500 underline underline-offset-2 hover:text-slypn-700"
              download
            >{{ n.fileName }}</a>
          </div>
          <p v-else class="mt-1 text-xs text-slypn-900/50">No file attached</p>
        </div>
        <div class="flex shrink-0 gap-2">
          <button
            type="button"
            data-testid="newsletter-edit"
            class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-xs font-semibold text-slypn-700 hover:bg-slypn-50"
            @click="openEdit(n)"
          >Edit</button>
          <button
            type="button"
            data-testid="newsletter-delete"
            class="rounded-md border border-rose-200 bg-white px-3 py-1.5 text-xs font-semibold text-rose-700 hover:bg-rose-50 disabled:opacity-50"
            :disabled="deletingId === n.id"
            @click="remove(n)"
          >{{ deletingId === n.id ? '…' : 'Delete' }}</button>
        </div>
      </div>
    </div>

    <p v-if="newsletters && !newsletters.length" class="text-sm text-slypn-900/60">
      No newsletters yet. Add the first one.
    </p>
  </section>

  <!-- Add / edit dialog -->
  <Teleport to="body">
    <div
      v-if="showForm"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slypn-900/40 p-4"
      @click.self="showForm = false"
    >
      <div class="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h3 class="font-display text-lg font-bold text-slypn-700">
          {{ editing ? 'Edit newsletter' : 'Add newsletter' }}
        </h3>
        <form data-testid="newsletter-dialog" class="mt-4 space-y-4" @submit.prevent="save">
          <div>
            <label for="newsletter-title" class="block text-sm font-medium text-slypn-800">Title</label>
            <input
              id="newsletter-title"
              v-model="form.title"
              type="text"
              maxlength="200"
              required
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
            <FieldCounter :used="form.title.length" :max="LIMITS.title" testid="newsletter-title-count" />
          </div>
          <div>
            <label for="newsletter-issue-date" class="block text-sm font-medium text-slypn-800">Issue date</label>
            <input
              id="newsletter-issue-date"
              v-model="form.issueDate"
              type="date"
              required
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
          </div>
          <div>
            <label for="newsletter-summary" class="block text-sm font-medium text-slypn-800">Summary</label>
            <textarea
              id="newsletter-summary"
              v-model="form.summary"
              rows="3"
              maxlength="1000"
              required
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
            <FieldCounter :used="form.summary.length" :max="LIMITS.summary" testid="newsletter-summary-count" />
          </div>
          <div>
            <label for="newsletter-topics" class="block text-sm font-medium text-slypn-800">Topics</label>
            <input
              id="newsletter-topics"
              v-model="form.topics"
              type="text"
              :maxlength="LIMITS.topics"
              placeholder="Comma-separated, e.g. Research, Events"
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
            <FieldCounter :used="form.topics.length" :max="LIMITS.topics" testid="newsletter-topics-count" />
          </div>
          <div>
            <label for="newsletter-file" class="block text-sm font-medium text-slypn-800">
              Issue file (PDF/DOCX)
              <span v-if="editing?.fileName" class="font-normal text-slypn-900/60">— replaces &ldquo;{{ editing.fileName }}&rdquo;</span>
            </label>
            <input
              id="newsletter-file"
              ref="fileInput"
              type="file"
              accept=".pdf,.doc,.docx,application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
              class="mt-1 w-full text-sm text-slypn-700 file:mr-3 file:rounded-md file:border-0 file:bg-slypn-50 file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-slypn-700 hover:file:bg-slypn-100"
              @change="onFileChosen"
            />
          </div>

          <p v-if="formError" data-testid="newsletter-error" class="rounded-md bg-rose-50 px-3 py-2 text-xs text-rose-700">{{ formError }}</p>

          <div class="flex justify-end gap-2 pt-2">
            <button
              type="button"
              class="rounded-md border border-slypn-200 px-4 py-2 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
              @click="showForm = false"
            >Cancel</button>
            <button
              type="submit"
              data-testid="newsletter-save"
              class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
              :disabled="!canSave || saving"
            >{{ saving ? 'Saving…' : (editing ? 'Save changes' : 'Add newsletter') }}</button>
          </div>
        </form>
      </div>
    </div>
  </Teleport>
</template>
