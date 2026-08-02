<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { renderAsync } from 'docx-preview'
import { apiFetch, apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Newsletter } from '@/types/content'

const route = useRoute()
const router = useRouter()

// Reachable from the public list, the admin list, and Home's "latest issue"
// link — recognise all three as "came from here" origins for the back
// button, and label it to match wherever it'll actually land.
const knownBackOrigins: Record<string, string> = {
  '/newsletter': 'Newsletters',
  '/admin/newsletters': 'Newsletters',
  '/': 'Home',
}

const backLabel = computed(() => {
  const back = router.options.history.state.back
  const path = typeof back === 'string' ? back.split('?')[0] : undefined
  return (path && knownBackOrigins[path]) || 'Newsletters'
})

function backToNewsletters() {
  const back = router.options.history.state.back
  if (typeof back === 'string' && back.split('?')[0] in knownBackOrigins) {
    router.back()
    return
  }
  router.push('/newsletter')
}

// No single-newsletter endpoint exists — find it in the same list the list
// pages already fetch. lazy: true because this view has two chained async
// steps (metadata, then the file) rather than the sibling views' one.
const { data: newsletter, loading, error, refresh: refreshMeta } = useAsyncData(async () => {
  const list = await apiJson<Newsletter[]>('/newsletters')
  return list.find(n => n.id === route.params.id) ?? null
}, { lazy: true })

type FileKind = 'pdf' | 'docx' | 'unsupported'

const fileLoading = ref(false)
const fileError = ref<string | null>(null)
const fileKind = ref<FileKind | null>(null)
const objectUrl = ref<string | null>(null)
const docxContainer = ref<HTMLElement | null>(null)

function revokeObjectUrl() {
  if (objectUrl.value) {
    URL.revokeObjectURL(objectUrl.value)
    objectUrl.value = null
  }
}

// Content-Disposition: attachment on the file endpoint only affects direct
// browser navigation, not fetch() — reading the bytes here and rendering
// from a client-side Blob/ArrayBuffer sidesteps it entirely.
async function loadFile() {
  revokeObjectUrl()
  fileKind.value = null
  fileError.value = null
  if (!newsletter.value?.fileName) return

  fileLoading.value = true
  try {
    const resp = await apiFetch(`/newsletters/${newsletter.value.id}/file`)
    if (!resp.ok) throw new Error(`${resp.status} ${resp.statusText}`)
    const contentType = (resp.headers.get('Content-Type') ?? '').toLowerCase()

    if (contentType.startsWith('application/pdf')) {
      objectUrl.value = URL.createObjectURL(await resp.blob())
      fileKind.value = 'pdf'
    } else if (contentType.startsWith('application/vnd.openxmlformats-officedocument.wordprocessingml.document')) {
      fileKind.value = 'docx'
      if (docxContainer.value) {
        docxContainer.value.innerHTML = ''
        // useBase64URL avoids docx-preview's own un-revoked internal object
        // URLs for embedded images.
        await renderAsync(await resp.arrayBuffer(), docxContainer.value, docxContainer.value, { useBase64URL: true })
      }
    } else {
      // Legacy .doc (application/msword) or anything unexpected — no viable
      // client-side renderer exists; fall back to download.
      fileKind.value = 'unsupported'
    }
  } catch (err) {
    fileError.value = err instanceof Error ? err.message : String(err)
  } finally {
    fileLoading.value = false
  }
}

async function load() {
  await refreshMeta()
  await loadFile()
}

onMounted(load)
watch(() => route.params.id, load)
onUnmounted(revokeObjectUrl)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })
</script>

<template>
  <div class="page-container py-12">
    <button type="button" class="mb-8 flex items-center gap-1.5 text-sm text-slypn-500 hover:text-slypn-700" @click="backToNewsletters">
      &larr; {{ backLabel }}
    </button>

    <p v-if="loading" class="text-center text-slypn-900/60">Loading&hellip;</p>

    <div v-else-if="error" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
      Couldn&rsquo;t load this issue: {{ error }}
    </div>

    <div v-else-if="!newsletter" class="text-center">
      <h1 class="font-display text-2xl font-bold text-slypn-700">Newsletter not found</h1>
      <RouterLink to="/newsletter" class="mt-6 inline-block text-slypn-600 underline underline-offset-4 hover:text-slypn-700">
        Back to newsletters
      </RouterLink>
    </div>

    <div v-else>
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p class="font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
            {{ formatDate(newsletter.issueDate) }}
          </p>
          <h1 class="mt-2 text-3xl font-extrabold text-slypn-700 sm:text-4xl">{{ newsletter.title }}</h1>
          <p class="mt-3 max-w-2xl text-sm text-slypn-900/75">{{ newsletter.summary }}</p>
        </div>
        <a
          v-if="newsletter.fileName"
          :href="`/api/newsletters/${newsletter.id}/file`"
          class="inline-flex shrink-0 items-center gap-1.5 text-sm font-semibold text-slypn-600 hover:text-slypn-700 hover:underline"
          download
        >
          <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
            <path d="M10 2a1 1 0 0 1 1 1v7.586l2.293-2.293a1 1 0 1 1 1.414 1.414l-4 4a1 1 0 0 1-1.414 0l-4-4a1 1 0 1 1 1.414-1.414L9 10.586V3a1 1 0 0 1 1-1Z" />
            <path d="M3 14a1 1 0 0 1 1 1v1a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1v-1a1 1 0 1 1 2 0v1a3 3 0 0 1-3 3H5a3 3 0 0 1-3-3v-1a1 1 0 0 1 1-1Z" />
          </svg>
          Download issue
        </a>
      </div>

      <div class="mt-8">
        <p v-if="!newsletter.fileName" class="text-slypn-900/70">
          No file has been attached to this issue yet.
        </p>

        <template v-else>
          <p v-if="fileLoading" class="text-slypn-900/60">Loading preview&hellip;</p>

          <div v-else-if="fileError" class="rounded-md bg-rose-50 px-4 py-3 text-sm text-rose-700">
            Couldn&rsquo;t load the preview: {{ fileError }}. You can still
            <a :href="`/api/newsletters/${newsletter.id}/file`" class="underline underline-offset-2" download>download the file</a>.
          </div>

          <div v-else-if="fileKind === 'unsupported'" class="rounded-md bg-slypn-50 px-4 py-3 text-sm text-slypn-700">
            This issue can&rsquo;t be previewed &mdash;
            <a :href="`/api/newsletters/${newsletter.id}/file`" class="underline underline-offset-2" download>download it instead</a>.
          </div>

          <iframe
            v-if="fileKind === 'pdf' && objectUrl"
            :src="objectUrl"
            :title="`${newsletter.title} — PDF preview`"
            class="h-[80vh] w-full rounded-xl border border-slypn-100"
          />
        </template>

        <!-- Always mounted once a newsletter is resolved (visibility only via
             v-show) so the ref exists before renderAsync needs it. -->
        <div v-show="fileKind === 'docx'" ref="docxContainer" class="docx-container rounded-xl border border-slypn-100 bg-white p-6" />
      </div>
    </div>
  </div>
</template>
