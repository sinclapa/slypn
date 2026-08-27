<script setup lang="ts">
withDefaults(defineProps<{
  /** Names the field in the accessible label, e.g. "category". */
  field: string
  testid?: string
}>(), { testid: 'field-clear' })

defineEmits<{ (e: 'clear'): void }>()
</script>

<template>
  <!--
    Sits inside a `relative` wrapper, over the right-hand end of the input — so the input
    needs padding there (pr-9) or the text runs underneath. Rendered by the caller only
    when there is something to clear, so it never offers a no-op.
  -->
  <button
    type="button"
    :data-testid="testid"
    :aria-label="`Clear ${field}`"
    :title="`Clear ${field}`"
    class="absolute inset-y-0 right-0 mt-1 flex items-center rounded-r-md px-2.5 text-slypn-400 transition-colors hover:text-slypn-700"
    @click="$emit('clear')"
  >
    <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" aria-hidden="true">
      <path d="M6 6l12 12M18 6L6 18" />
    </svg>
  </button>
</template>
