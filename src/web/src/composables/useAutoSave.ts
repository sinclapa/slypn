import { onBeforeUnmount, ref, watch, type Ref } from 'vue'

export type AutoSaveStatus = 'idle' | 'pending' | 'saving' | 'saved' | 'error'

export interface UseAutoSaveOptions {
  /** Milliseconds of inactivity before the save fires. Default 1500. */
  debounce?: number
  /** Skip the initial save when the watched value first becomes truthy. Default true. */
  skipInitial?: boolean
}

/**
 * Watches a reactive value and calls saveFn after `debounce` ms of quiet.
 * Returns reactive { status, lastSavedAt, errorMessage }.
 *
 * Designed for the editor: bind to the draft object, pass a saveFn that
 * does the PUT, and surface status via the SaveIndicator.
 */
export function useAutoSave<T>(
  state: Ref<T>,
  saveFn: (value: T) => Promise<void>,
  options: UseAutoSaveOptions = {},
) {
  const debounce    = options.debounce ?? 1500
  const skipInitial = options.skipInitial ?? true

  const status        = ref<AutoSaveStatus>('idle')
  const lastSavedAt   = ref<Date | null>(null)
  const errorMessage  = ref<string | null>(null)

  let timer: ReturnType<typeof setTimeout> | null = null
  let firstFire = true

  watch(state, (value) => {
    if (firstFire) {
      firstFire = false
      if (skipInitial) return
    }
    if (timer) clearTimeout(timer)
    status.value = 'pending'
    timer = setTimeout(async () => {
      status.value = 'saving'
      try {
        await saveFn(value)
        status.value = 'saved'
        lastSavedAt.value = new Date()
        errorMessage.value = null
      } catch (err) {
        status.value = 'error'
        errorMessage.value = err instanceof Error ? err.message : String(err)
      }
    }, debounce)
  }, { deep: true })

  onBeforeUnmount(() => {
    if (timer) clearTimeout(timer)
  })

  return { status, lastSavedAt, errorMessage }
}
