<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  /** Characters used. Pass a length, not the value, so callers can count what they mean —
      text rather than markup, for instance. */
  used: number
  max: number
  /** Fraction of the limit to stay hidden below. */
  showFrom?: number
  /** Overridden so each field's counter can be addressed on its own in tests. */
  testid?: string
}>(), { showFrom: 0.8, testid: 'field-counter' })

// A permanent "3 / 200" under every field is noise, and trains people to ignore it. It
// appears only once the limit is close enough to be worth knowing about.
const visible = computed(() => props.used >= props.max * props.showFrom)
const atLimit = computed(() => props.used >= props.max)
</script>

<template>
  <p
    v-if="visible"
    :data-testid="testid"
    class="mt-1 text-right text-xs"
    :class="atLimit ? 'font-semibold text-amber-600' : 'text-slypn-400'"
  >
    {{ used }} / {{ max }}<span v-if="atLimit"> — limit reached</span>
  </p>
</template>
