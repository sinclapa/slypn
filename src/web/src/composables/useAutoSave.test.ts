import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, ref, nextTick, type Ref } from 'vue'
import { mount } from '@vue/test-utils'
import { useAutoSave, type AutoSaveStatus } from './useAutoSave'

interface Harness {
  status: Ref<AutoSaveStatus>
  errorMessage: Ref<string | null>
  lastSavedAt: Ref<Date | null>
}

function mountAutoSave<T>(state: Ref<T>, saveFn: (v: T) => Promise<void>, options = {}) {
  let api!: Harness
  const Comp = defineComponent({
    setup() {
      api = useAutoSave(state, saveFn, options) as Harness
      return {}
    },
    template: '<div />',
  })
  const wrapper = mount(Comp)
  return { api, wrapper }
}

describe('useAutoSave', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('skips the initial change by default', async () => {
    const state = ref('a')
    const saveFn = vi.fn().mockResolvedValue(undefined)
    const { api } = mountAutoSave(state, saveFn)

    state.value = 'b' // first change is skipped
    await nextTick()
    await vi.runAllTimersAsync()
    expect(saveFn).not.toHaveBeenCalled()
    expect(api.status.value).toBe('idle')
  })

  it('debounces and saves after quiet period, moving through statuses', async () => {
    const state = ref('a')
    const saveFn = vi.fn().mockResolvedValue(undefined)
    const { api } = mountAutoSave(state, saveFn, { skipInitial: false, debounce: 1000 })

    state.value = 'b'
    await nextTick()
    expect(api.status.value).toBe('pending')
    await vi.advanceTimersByTimeAsync(1000)

    expect(saveFn).toHaveBeenCalledWith('b')
    expect(api.status.value).toBe('saved')
    expect(api.lastSavedAt.value).toBeInstanceOf(Date)
  })

  it('coalesces rapid edits into a single save', async () => {
    const state = ref(0)
    const saveFn = vi.fn().mockResolvedValue(undefined)
    mountAutoSave(state, saveFn, { skipInitial: false, debounce: 500 })

    state.value = 1
    await nextTick()
    await vi.advanceTimersByTimeAsync(200)
    state.value = 2
    await nextTick()
    await vi.advanceTimersByTimeAsync(200)
    state.value = 3
    await nextTick()
    await vi.advanceTimersByTimeAsync(500)

    expect(saveFn).toHaveBeenCalledTimes(1)
    expect(saveFn).toHaveBeenCalledWith(3)
  })

  it('captures save failures into error status', async () => {
    const state = ref('a')
    const saveFn = vi.fn().mockRejectedValue(new Error('save failed'))
    const { api } = mountAutoSave(state, saveFn, { skipInitial: false, debounce: 100 })

    state.value = 'b'
    await nextTick()
    await vi.advanceTimersByTimeAsync(100)

    expect(api.status.value).toBe('error')
    expect(api.errorMessage.value).toBe('save failed')
  })
})
