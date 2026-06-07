<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { Editor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import Link from '@tiptap/extension-link'
import Placeholder from '@tiptap/extension-placeholder'
import { apiFetch } from '@/lib/api'

const props = defineProps<{
  modelValue: string
  placeholder?: string
}>()
const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'uploadError', message: string): void
}>()

const fileInput = ref<HTMLInputElement | null>(null)
const uploading = ref(false)

const editor = new Editor({
  content: props.modelValue,
  extensions: [
    StarterKit.configure({
      heading: { levels: [1, 2, 3] },
    }),
    Image.configure({ inline: false, allowBase64: false }),
    Link.configure({
      openOnClick: false,
      autolink: true,
      HTMLAttributes: { rel: 'noopener', target: '_blank' },
    }),
    Placeholder.configure({
      placeholder: props.placeholder ?? 'Start writing…',
    }),
  ],
  editorProps: {
    attributes: {
      class: 'prose prose-slypn focus:outline-none max-w-none min-h-[20rem] px-4 py-3',
    },
  },
  onUpdate: ({ editor }) => emit('update:modelValue', editor.getHTML()),
})

// Sync external value changes back into TipTap (e.g. when loading a draft).
watch(() => props.modelValue, (value) => {
  if (value === editor.getHTML()) return
  editor.commands.setContent(value, false)
})

onBeforeUnmount(() => editor.destroy())

function toggleBold()      { editor.chain().focus().toggleBold().run() }
function toggleItalic()    { editor.chain().focus().toggleItalic().run() }
function toggleBulletList(){ editor.chain().focus().toggleBulletList().run() }
function toggleOrderedList(){ editor.chain().focus().toggleOrderedList().run() }
function toggleBlockquote(){ editor.chain().focus().toggleBlockquote().run() }
function toggleHeading(level: 1 | 2 | 3) {
  editor.chain().focus().toggleHeading({ level }).run()
}
// ── Link dialog ─────────────────────────────────────────────────────────────
const linkDialog = ref({ show: false, text: '', url: '', hasExisting: false })
const savedSel   = ref<{ from: number; to: number } | null>(null)

function setLink() {
  const { from, to, empty } = editor.state.selection
  savedSel.value = { from, to }
  linkDialog.value = {
    show:        true,
    text:        empty ? '' : editor.state.doc.textBetween(from, to),
    url:         (editor.getAttributes('link').href as string | undefined) ?? '',
    hasExisting: editor.isActive('link'),
  }
}

function confirmLink() {
  const { url, text } = linkDialog.value
  linkDialog.value.show = false
  if (savedSel.value) editor.commands.setTextSelection(savedSel.value)
  savedSel.value = null

  const cleanUrl  = url.trim()
  const cleanText = text.trim()

  if (!cleanUrl) {
    editor.chain().focus().extendMarkRange('link').unsetLink().run()
    return
  }
  if (cleanText) {
    editor.chain().focus().extendMarkRange('link')
      .insertContent({ type: 'text', text: cleanText, marks: [{ type: 'link', attrs: { href: cleanUrl } }] })
      .run()
  } else {
    editor.chain().focus().extendMarkRange('link').setLink({ href: cleanUrl }).run()
  }
}

function removeLink() {
  linkDialog.value.show = false
  if (savedSel.value) editor.commands.setTextSelection(savedSel.value)
  savedSel.value = null
  editor.chain().focus().extendMarkRange('link').unsetLink().run()
}
function pickImage() {
  fileInput.value?.click()
}

async function onFileChosen(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  target.value = ''
  if (!file) return

  uploading.value = true
  try {
    const fd = new FormData()
    fd.append('file', file)
    const resp = await apiFetch('/media', { method: 'POST', body: fd })
    if (!resp.ok) {
      const body = await resp.text().catch(() => '')
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    const { url } = await resp.json() as { name: string; url: string }
    editor.chain().focus().setImage({ src: url, alt: file.name }).run()
  } catch (err) {
    emit('uploadError', err instanceof Error ? err.message : String(err))
  } finally {
    uploading.value = false
  }
}

interface ToolbarButton {
  key: string
  label: string
  title: string
  action: () => void
  isActive: () => boolean
}

const buttons: ToolbarButton[] = [
  { key: 'h1',     label: 'H1',  title: 'Heading 1',     action: () => toggleHeading(1), isActive: () => editor.isActive('heading', { level: 1 }) },
  { key: 'h2',     label: 'H2',  title: 'Heading 2',     action: () => toggleHeading(2), isActive: () => editor.isActive('heading', { level: 2 }) },
  { key: 'h3',     label: 'H3',  title: 'Heading 3',     action: () => toggleHeading(3), isActive: () => editor.isActive('heading', { level: 3 }) },
  { key: 'bold',   label: 'B',   title: 'Bold (Cmd/Ctrl+B)',    action: toggleBold,        isActive: () => editor.isActive('bold') },
  { key: 'italic', label: 'I',   title: 'Italic (Cmd/Ctrl+I)',  action: toggleItalic,      isActive: () => editor.isActive('italic') },
  { key: 'ul',     label: '•',   title: 'Bullet list',          action: toggleBulletList,  isActive: () => editor.isActive('bulletList') },
  { key: 'ol',     label: '1.',  title: 'Numbered list',        action: toggleOrderedList, isActive: () => editor.isActive('orderedList') },
  { key: 'quote',  label: '“ ”', title: 'Blockquote',           action: toggleBlockquote,  isActive: () => editor.isActive('blockquote') },
  { key: 'link',   label: '🔗',  title: 'Link',                 action: setLink,           isActive: () => editor.isActive('link') },
]
</script>

<template>
  <div class="rounded-xl border border-slypn-100 bg-white shadow-sm">
    <div class="flex flex-wrap items-center gap-1 border-b border-slypn-100 px-2 py-2">
      <button
        v-for="b in buttons"
        :key="b.key"
        type="button"
        :title="b.title"
        :class="[
          'min-w-[2.25rem] rounded px-2 py-1 text-sm font-semibold transition-colors',
          b.isActive()
            ? 'bg-slypn-600 text-white'
            : 'text-slypn-700 hover:bg-slypn-50',
        ]"
        @click="b.action"
      >
        {{ b.label }}
      </button>
      <button
        type="button"
        title="Insert image"
        class="min-w-[2.25rem] rounded px-2 py-1 text-sm font-semibold text-slypn-700 hover:bg-slypn-50 disabled:opacity-50"
        :disabled="uploading"
        @click="pickImage"
      >
        {{ uploading ? '…' : '📷' }}
      </button>
      <input
        ref="fileInput"
        type="file"
        accept="image/png,image/jpeg,image/webp"
        class="hidden"
        @change="onFileChosen"
      />
    </div>

    <EditorContent :editor="editor" />
  </div>

  <Teleport to="body">
    <div
      v-if="linkDialog.show"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      @mousedown.self="linkDialog.show = false"
    >
      <div class="w-full max-w-sm rounded-xl bg-white p-6 shadow-xl">
        <h3 class="font-display font-semibold text-slypn-700">
          {{ linkDialog.hasExisting ? 'Edit link' : 'Insert link' }}
        </h3>
        <div class="mt-4 space-y-3">
          <div>
            <label class="block text-sm font-medium text-slypn-800">Text</label>
            <input
              v-model="linkDialog.text"
              type="text"
              placeholder="Link text"
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
            />
          </div>
          <div>
            <label class="block text-sm font-medium text-slypn-800">URL</label>
            <input
              v-model="linkDialog.url"
              type="url"
              placeholder="https://"
              class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              @keydown.enter.prevent="confirmLink"
              @keydown.esc.prevent="linkDialog.show = false"
            />
          </div>
        </div>
        <div class="mt-5 flex flex-wrap items-center gap-2">
          <button
            v-if="linkDialog.hasExisting"
            type="button"
            class="rounded-md border border-rose-200 px-3 py-1.5 text-sm font-medium text-rose-600 hover:bg-rose-50"
            @click="removeLink"
          >Remove</button>
          <div class="ml-auto flex gap-2">
            <button
              type="button"
              class="rounded-md px-3 py-1.5 text-sm font-medium text-slypn-700 hover:bg-slypn-50"
              @click="linkDialog.show = false"
            >Cancel</button>
            <button
              type="button"
              :disabled="!linkDialog.url"
              class="rounded-md bg-slypn-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-slypn-700 disabled:opacity-50"
              @click="confirmLink"
            >Apply</button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style>
/* Placeholder shown when the editor is empty */
.ProseMirror p.is-editor-empty:first-child::before {
  content: attr(data-placeholder);
  float: left;
  color: #94a3b8;
  pointer-events: none;
  height: 0;
}

/* Colour overrides on top of @tailwindcss/typography */
.prose-slypn { --tw-prose-body: rgb(12 25 41 / 0.85); --tw-prose-headings: rgb(30 58 95); --tw-prose-links: #1565C0; --tw-prose-blockquote-borders: rgb(168 207 255); }
.prose-slypn h1, .prose-slypn h2, .prose-slypn h3 { font-family: Montserrat, Inter, system-ui, sans-serif; }
.prose-slypn img { border-radius: 0.5rem; }
</style>
