<script setup lang="ts">
// Reusable single-select pill filter (events type, content category, …).
// "All" clears the selection; options are typically derived from the data.
defineProps<{ options: string[]; modelValue: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const base   = 'rounded-full border px-4 py-1.5 text-sm font-medium transition-colors'
const active = 'border-slypn-600 bg-slypn-600 text-white'
const idle   = 'border-slypn-200 bg-white text-slypn-700 hover:bg-slypn-50'
</script>

<template>
  <div v-if="options.length" class="flex flex-wrap gap-2">
    <button
      type="button"
      :class="[base, modelValue === 'All' ? active : idle]"
      @click="emit('update:modelValue', 'All')"
    >All</button>
    <button
      v-for="o in options"
      :key="o"
      type="button"
      :class="[base, modelValue === o ? active : idle]"
      @click="emit('update:modelValue', o)"
    >{{ o }}</button>
  </div>
</template>
