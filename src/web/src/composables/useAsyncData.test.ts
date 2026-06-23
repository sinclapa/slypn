import { describe, it, expect, vi } from 'vitest'
import { defineComponent, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useAsyncData } from './useAsyncData'

describe('useAsyncData', () => {
  it('refresh() populates data and toggles loading', async () => {
    const fetcher = vi.fn().mockResolvedValue([1, 2, 3])
    const { data, loading, error, refresh } = useAsyncData(fetcher, { lazy: true })

    expect(data.value).toBeNull()
    expect(loading.value).toBe(false)

    const p = refresh()
    expect(loading.value).toBe(true)
    await p

    expect(data.value).toEqual([1, 2, 3])
    expect(loading.value).toBe(false)
    expect(error.value).toBeNull()
  })

  it('captures an Error message into error', async () => {
    const { error, data, refresh } = useAsyncData(() => Promise.reject(new Error('nope')), { lazy: true })
    await refresh()
    expect(error.value).toBe('nope')
    expect(data.value).toBeNull()
  })

  it('stringifies non-Error rejections', async () => {
    const { error, refresh } = useAsyncData(() => Promise.reject('weird'), { lazy: true })
    await refresh()
    expect(error.value).toBe('weird')
  })

  it('runs automatically on mount when not lazy', async () => {
    const fetcher = vi.fn().mockResolvedValue('ok')
    const Comp = defineComponent({
      setup() {
        return useAsyncData(fetcher)
      },
      template: '<div>{{ data }}</div>',
    })
    mount(Comp)
    await nextTick()
    await nextTick()
    expect(fetcher).toHaveBeenCalledTimes(1)
  })
})
