<script setup lang="ts">
import { ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import RichTextEditor from '@/components/editor/RichTextEditor.vue'

const body = ref('<p>Start writing&hellip;</p>')
const uploadError = ref<string | null>(null)
</script>

<template>
  <HeroBanner
    eyebrow="Editor"
    title="Write something for the community"
    subtitle="Rich-text editor with images, links, headings, and lists. Autosave + draft workflow + admin approval land in #27-#30."
  />

  <section class="mx-auto max-w-3xl px-6 py-12">
    <RichTextEditor
      v-model="body"
      @upload-error="(msg) => uploadError = msg"
    />

    <p v-if="uploadError" class="mt-3 rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">
      Image upload failed: {{ uploadError }}
    </p>

    <details class="mt-8 rounded-md border border-slypn-100 bg-slypn-50 p-4">
      <summary class="cursor-pointer text-sm font-semibold text-slypn-700">Preview the HTML the API will store</summary>
      <pre class="mt-3 overflow-x-auto whitespace-pre-wrap break-words text-xs text-slypn-900/80">{{ body }}</pre>
    </details>
  </section>
</template>
