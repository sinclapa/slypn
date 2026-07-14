<script setup lang="ts">
import { computed } from 'vue'
import type { AutoSaveStatus } from '@/composables/useAutoSave'

const props = defineProps<{
  status: AutoSaveStatus
  lastSavedAt: Date | null
  error?: string | null
}>()

const label = computed(() => {
  if (props.status === 'pending') return 'Editing&hellip;'
  if (props.status === 'saving')  return 'Saving&hellip;'
  if (props.status === 'error') {
    const suffix = props.error ? `: ${props.error}` : ''
    return `Save failed${suffix}`
  }
  if (props.status === 'saved' || props.lastSavedAt) {
    return `Saved at ${formatTime(props.lastSavedAt ?? new Date())}`
  }
  return 'Not saved yet'
})

const dotClass = computed(() => {
  switch (props.status) {
    case 'saving':  return 'bg-slypn-500 animate-pulse'
    case 'saved':   return 'bg-emerald-500'
    case 'error':   return 'bg-rose-500'
    case 'pending': return 'bg-amber-400'
    default:        return 'bg-slypn-200'
  }
})

function formatTime(d: Date) {
  return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
}
</script>

<template>
  <p class="inline-flex items-center gap-2 text-xs text-slypn-900/70">
    <span :class="['inline-block h-2 w-2 rounded-full', dotClass]" />
    <span v-html="label" />
  </p>
</template>
