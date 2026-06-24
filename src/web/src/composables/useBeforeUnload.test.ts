import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, ref, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useBeforeUnload } from './useBeforeUnload'

describe('useBeforeUnload', () => {
  let addSpy: ReturnType<typeof vi.spyOn>
  let removeSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    addSpy = vi.spyOn(window, 'addEventListener')
    removeSpy = vi.spyOn(window, 'removeEventListener')
  })
  afterEach(() => vi.restoreAllMocks())

  function mountWith(initial: boolean) {
    const shouldWarn = ref(initial)
    const Comp = defineComponent({
      setup() {
        useBeforeUnload(shouldWarn)
        return {}
      },
      template: '<div />',
    })
    return { shouldWarn, wrapper: mount(Comp) }
  }

  it('registers a beforeunload listener while shouldWarn is true', () => {
    mountWith(true)
    expect(addSpy).toHaveBeenCalledWith('beforeunload', expect.any(Function))
  })

  it('adds the listener when shouldWarn flips on and removes it when off', async () => {
    const { shouldWarn } = mountWith(false)
    addSpy.mockClear()
    shouldWarn.value = true
    await nextTick()
    expect(addSpy).toHaveBeenCalledWith('beforeunload', expect.any(Function))

    shouldWarn.value = false
    await nextTick()
    expect(removeSpy).toHaveBeenCalledWith('beforeunload', expect.any(Function))
  })

  it('removes the listener on unmount', () => {
    const { wrapper } = mountWith(true)
    removeSpy.mockClear()
    wrapper.unmount()
    expect(removeSpy).toHaveBeenCalledWith('beforeunload', expect.any(Function))
  })

  it('the handler sets returnValue only while warning', () => {
    const { shouldWarn } = mountWith(true)
    const handler = addSpy.mock.calls.find((c: unknown[]) => c[0] === 'beforeunload')![1] as (e: BeforeUnloadEvent) => void
    const event = { preventDefault: vi.fn(), returnValue: undefined } as unknown as BeforeUnloadEvent
    handler(event)
    expect(event.preventDefault).toHaveBeenCalled()
    expect(event.returnValue).toBe('')

    shouldWarn.value = false
    const event2 = { preventDefault: vi.fn(), returnValue: undefined } as unknown as BeforeUnloadEvent
    handler(event2)
    expect(event2.preventDefault).not.toHaveBeenCalled()
  })
})
