import { onBeforeUnmount, watchEffect, type Ref } from 'vue'

/**
 * Wires a `beforeunload` warning that fires while `shouldWarn` is true.
 * Modern browsers ignore the message string and show their own copy, but
 * setting `returnValue` is still required to trigger the prompt.
 */
export function useBeforeUnload(shouldWarn: Ref<boolean>) {
  function handler(event: BeforeUnloadEvent) {
    if (!shouldWarn.value) return
    event.preventDefault()
    event.returnValue = '' // NOSONAR - deprecated but still required by some browsers to trigger the prompt
  }

  watchEffect(() => {
    if (shouldWarn.value) window.addEventListener('beforeunload', handler)
    else window.removeEventListener('beforeunload', handler)
  })

  onBeforeUnmount(() => window.removeEventListener('beforeunload', handler))
}
