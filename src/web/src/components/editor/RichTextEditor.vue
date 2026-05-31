<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { Editor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import Link from '@tiptap/extension-link'
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
function setLink() {
  const existing = editor.getAttributes('link').href as string | undefined
  const url = window.prompt('URL', existing ?? 'https://')
  if (url === null) return
  if (url === '') {
    editor.chain().focus().extendMarkRange('link').unsetLink().run()
    return
  }
  editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
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
</template>

<style>
.prose-slypn { color: rgb(12 25 41 / 0.85); }
.prose-slypn h1, .prose-slypn h2, .prose-slypn h3 { color: rgb(30 58 95); font-family: Montserrat, Inter, system-ui, sans-serif; }
.prose-slypn a { color: rgb(21 101 192); text-decoration: underline; }
.prose-slypn blockquote { border-left: 4px solid rgb(168 207 255); padding-left: 0.75rem; color: rgb(30 58 95); }
.prose-slypn img { border-radius: 0.5rem; }
</style>
