<script setup lang="ts">
import { ref } from 'vue'
import { apiFetch, apiErrorMessage } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'
import DraftEditor from '@/components/editor/DraftEditor.vue'

const props = withDefaults(defineProps<{
  /** Published article or blog post id. */
  contentId: string
  /** Server-computed permission. See Article.canEdit. */
  canEdit?: boolean
  label?: string
}>(), { label: 'Edit this page' })

const emit = defineEmits<{ (e: 'submitted'): void }>()

const auth = useAuthStore()

const editDraftId  = ref<string | null>(null)
const editorRef    = ref<InstanceType<typeof DraftEditor> | null>(null)
const busy         = ref(false)
const error        = ref<string | null>(null)

// Editing published content creates a revision draft; the live version stays up
// until an admin approves the revision. Same endpoint for articles and blog posts.
async function startEdit() {
  busy.value = true
  error.value = null
  try {
    const resp = await apiFetch(`/articles/${props.contentId}/edit`, { method: 'POST' })
    if (!resp.ok) throw new Error(await apiErrorMessage(resp))
    const draft = await resp.json() as { id: string }
    editDraftId.value = draft.id
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    busy.value = false
  }
}

// The revision draft is minted up front, so opening and closing without typing
// would otherwise leave an orphan in the author's editor queue. Keep it only if
// they actually changed something.
async function closeEdit() {
  const id = editDraftId.value
  const editor = editorRef.value
  if (id && editor) {
    if (editor.isDirty()) await editor.flush()
    else try { await apiFetch(`/drafts/${id}`, { method: 'DELETE' }) } catch { /* best-effort */ }
  }
  editDraftId.value = null
}

// Submit consumes the draft server-side (it becomes an in-review revision), so
// there is nothing to clean up.
function submittedEdit() {
  editDraftId.value = null
  emit('submitted')
}
</script>

<template>
  <!--
    canEdit is paired with isAuthenticated because in dev-skip mode the API resolves a
    caller with no persona header to the admin persona, which would otherwise light this
    up for a signed-out visitor locally. The real boundary is the API's ownership check
    on POST /articles/{id}/edit — this is only the affordance.
  -->
  <button
    v-if="canEdit && auth.isAuthenticated"
    type="button"
    data-testid="edit-content"
    :aria-label="label"
    :title="label"
    class="inline-flex shrink-0 items-center rounded border border-slypn-200 p-1 text-slypn-400 transition-colors hover:bg-slypn-50 hover:text-slypn-600 disabled:opacity-50"
    :disabled="busy"
    @click="startEdit"
  >
    <svg
      class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor"
      stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"
    >
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </svg>
  </button>

  <p v-if="error" data-testid="edit-content-error" class="mt-2 text-sm text-rose-600">{{ error }}</p>

  <!-- Edit dialog — reuses the editor control, as /admin/content does. -->
  <Teleport to="body">
    <div
      v-if="editDraftId"
      class="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slypn-900/40 p-4"
    >
      <div class="my-8 w-full max-w-3xl rounded-xl bg-white p-6 shadow-xl">
        <div class="mb-4 flex items-center justify-between">
          <h3 class="font-display text-lg font-bold text-slypn-700">Edit published content</h3>
          <p class="text-xs text-slypn-500">Submitting sends a revision for approval.</p>
        </div>
        <DraftEditor
          ref="editorRef"
          :draft-id="editDraftId"
          @close="closeEdit"
          @submitted="submittedEdit"
        />
      </div>
    </div>
  </Teleport>
</template>
