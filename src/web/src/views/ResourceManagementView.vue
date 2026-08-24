<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import { apiFetch, apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'

interface Resource {
  id: string
  title: string
  description: string
  url: string
  category: string
  _etag?: string
}

const { data: resources, loading, error, refresh } = useAsyncData(
  () => apiJson<Resource[]>('/resources'),
)

const grouped = computed(() => {
  const map = new Map<string, Resource[]>()
  for (const r of resources.value ?? []) {
    const key = r.category || 'Uncategorised'
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push(r)
  }
  return Array.from(map.entries())
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([category, items]) => ({ category, items }))
})

const categoryHints = computed(() => {
  const set = new Set<string>()
  for (const r of resources.value ?? []) if (r.category) set.add(r.category)
  return [...set].sort((a, b) => a.localeCompare(b))
})

// ── Add / edit dialog ────────────────────────────────────────────────────────
const showForm  = ref(false)
const editing   = ref<Resource | null>(null)
const form      = ref({ title: '', description: '', url: '', category: '' })
const saving    = ref(false)
const formError = ref<string | null>(null)

function openAdd() {
  editing.value = null
  form.value = { title: '', description: '', url: '', category: '' }
  formError.value = null
  showForm.value = true
}

function openEdit(r: Resource) {
  editing.value = r
  form.value = { title: r.title, description: r.description, url: r.url, category: r.category }
  formError.value = null
  showForm.value = true
}

const canSave = computed(() =>
  form.value.title.trim() && form.value.description.trim() &&
  form.value.url.trim() && form.value.category.trim())

async function save() {
  if (!canSave.value || saving.value) return
  saving.value = true
  formError.value = null
  try {
    const body = JSON.stringify({
      title:       form.value.title.trim(),
      description: form.value.description.trim(),
      url:         form.value.url.trim(),
      category:    form.value.category.trim(),
    })
    const resp = editing.value
      ? await apiFetch(`/resources/${editing.value.id}`, {
          method: 'PUT',
          body,
          headers: editing.value._etag ? { 'If-Match': editing.value._etag } : {},
        })
      : await apiFetch('/resources', { method: 'POST', body })
    if (!resp.ok) throw new Error(`${resp.status} ${resp.statusText} — ${await resp.text().catch(() => '')}`)
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

async function remove(r: Resource) {
  if (!confirm(`Delete "${r.title}"? This cannot be undone.`)) return
  deletingId.value = r.id
  listError.value = null
  try {
    const resp = await apiFetch(`/resources/${r.id}?category=${encodeURIComponent(r.category)}`, {
      method: 'DELETE',
      headers: r._etag ? { 'If-Match': r._etag } : {},
    })
    if (!resp.ok) throw new Error(`${resp.status} ${await resp.text().catch(() => '')}`)
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
    title="Resources"
    subtitle="Add, edit, and remove the links shown on the public Resources page."
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
      <h2 class="font-display text-xl font-bold text-slypn-700">All resources</h2>
      <button
        type="button"
        data-testid="resource-add"
        class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
        @click="openAdd"
      >+ Add resource</button>
    </div>

    <p v-if="loading && !resources" class="text-sm text-slypn-900/60">Loading resources…</p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load resources: {{ error }}.
      <button type="button" class="ml-2 underline" @click="refresh">Retry</button>
    </div>

    <p v-if="listError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      {{ listError }}
      <button type="button" class="ml-2 underline" @click="listError = null">Dismiss</button>
    </p>

    <div v-for="group in grouped" :key="group.category" class="space-y-3">
      <h3 class="font-display text-sm font-semibold uppercase tracking-widest text-slypn-500">
        {{ group.category }}
      </h3>
      <div class="divide-y divide-slypn-100 rounded-xl border border-slypn-100 bg-white shadow-sm">
        <div
          v-for="r in group.items"
          :key="r.id"
          data-testid="resource-row"
          :data-id="r.id"
          class="flex items-start gap-4 px-5 py-4"
        >
          <div class="min-w-0 flex-1">
            <p class="font-semibold text-slypn-800">{{ r.title }}</p>
            <p class="mt-0.5 text-sm text-slypn-900/75">{{ r.description }}</p>
            <a :href="r.url" target="_blank" rel="noopener"
               class="mt-1 block truncate text-xs text-slypn-500 underline underline-offset-2 hover:text-slypn-700">
              {{ r.url }}
            </a>
          </div>
          <div class="flex shrink-0 gap-2">
            <button
              type="button"
              data-testid="resource-edit"
              class="rounded-md border border-slypn-200 bg-white px-3 py-1.5 text-xs font-semibold text-slypn-700 hover:bg-slypn-50"
              @click="openEdit(r)"
            >Edit</button>
            <button
              type="button"
              data-testid="resource-delete"
              class="rounded-md border border-rose-200 bg-white px-3 py-1.5 text-xs font-semibold text-rose-700 hover:bg-rose-50 disabled:opacity-50"
              :disabled="deletingId === r.id"
              @click="remove(r)"
            >{{ deletingId === r.id ? '…' : 'Delete' }}</button>
          </div>
        </div>
      </div>
    </div>

    <p v-if="resources && !grouped.length" class="text-sm text-slypn-900/60">
      No resources yet. Add the first one.
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
          {{ editing ? 'Edit resource' : 'Add resource' }}
        </h3>
        <form data-testid="resource-dialog" class="mt-4 space-y-4" @submit.prevent="save">
          <div>
            <label for="resource-title" class="block text-sm font-medium text-slypn-800">Title</label>
            <input
              id="resource-title"
              v-model="form.title"
              type="text"
              maxlength="200"
              required
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
          </div>
          <div>
            <label for="resource-description" class="block text-sm font-medium text-slypn-800">Description</label>
            <textarea
              id="resource-description"
              v-model="form.description"
              rows="3"
              maxlength="500"
              required
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
          </div>
          <div>
            <label for="resource-url" class="block text-sm font-medium text-slypn-800">URL</label>
            <input
              id="resource-url"
              v-model="form.url"
              type="url"
              maxlength="500"
              required
              placeholder="https://…"
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
          </div>
          <div>
            <label for="resource-category" class="block text-sm font-medium text-slypn-800">Category</label>
            <input
              id="resource-category"
              v-model="form.category"
              type="text"
              maxlength="60"
              list="resource-category-hints"
              autocomplete="off"
              required
              placeholder="Pick an existing one or type a new category"
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
            <datalist id="resource-category-hints">
              <option v-for="c in categoryHints" :key="c" :value="c" />
            </datalist>
          </div>

          <p v-if="formError" data-testid="resource-error" class="rounded-md bg-rose-50 px-3 py-2 text-xs text-rose-700">{{ formError }}</p>

          <div class="flex justify-end gap-2 pt-2">
            <button
              type="button"
              class="rounded-md border border-slypn-200 px-4 py-2 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
              @click="showForm = false"
            >Cancel</button>
            <button
              type="submit"
              data-testid="resource-save"
              class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
              :disabled="!canSave || saving"
            >{{ saving ? 'Saving…' : (editing ? 'Save changes' : 'Add resource') }}</button>
          </div>
        </form>
      </div>
    </div>
  </Teleport>
</template>
