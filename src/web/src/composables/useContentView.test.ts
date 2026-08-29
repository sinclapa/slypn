import { describe, it, expect, beforeEach, vi } from 'vitest'
import { defineComponent, ref, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'

const { getFaro } = vi.hoisted(() => ({ getFaro: vi.fn() }))
vi.mock('@/lib/faro', () => ({ getFaro }))

import { useContentView, type TrackedContent } from './useContentView'

const pushEvent = vi.fn()

/** Mount a component that tracks the ref it is handed, so watchers run in a real scope. */
function track(item: ReturnType<typeof ref<TrackedContent | null>>) {
  return mount(defineComponent({
    setup() {
      useContentView(item, 'article')
      return () => h('div')
    },
  }))
}

beforeEach(() => {
  pushEvent.mockReset()
  getFaro.mockReturnValue({ api: { pushEvent } })
})

describe('useContentView', () => {
  it('reports the slug, title and category of the item on screen', async () => {
    track(ref<TrackedContent | null>({ slug: 'a-slug', title: 'A Title', category: 'Community' }))
    await nextTick()

    expect(pushEvent).toHaveBeenCalledWith('content_viewed', expect.objectContaining({
      content_slug: 'a-slug',
      content_title: 'A Title',
      content_category: 'Community',
    }))
  })

  it('reports a new view when prev/next swaps the item in place', async () => {
    // The detail views refresh without remounting, so a mount-time hook would report the
    // first article of a series and nothing after it.
    const item = ref<TrackedContent | null>({ slug: 'first' })
    track(item)
    await nextTick()

    item.value = { slug: 'second' }
    await nextTick()

    expect(pushEvent).toHaveBeenCalledTimes(2)
    expect(pushEvent.mock.calls[1][1]).toMatchObject({ content_slug: 'second' })
  })

  it('does not report the same item twice when it is re-fetched', async () => {
    const item = ref<TrackedContent | null>({ slug: 'same', title: 'Same' })
    track(item)
    await nextTick()

    item.value = { slug: 'same', title: 'Same' }   // a retry or manual refresh
    await nextTick()

    expect(pushEvent).toHaveBeenCalledTimes(1)
  })

  it('says nothing while the item is still loading', async () => {
    track(ref<TrackedContent | null>(null))
    await nextTick()
    expect(pushEvent).not.toHaveBeenCalled()
  })

  it('prefers the item’s own type, so a mirrored blog post is not filed as an article', async () => {
    track(ref<TrackedContent | null>({ slug: 's', type: 'blog' }))
    await nextTick()

    expect(pushEvent.mock.calls[0][1]).toMatchObject({ content_kind: 'blog' })
  })

  it('is inert without telemetry consent', async () => {
    // getFaro() stays null until the cookie banner is accepted.
    getFaro.mockReturnValue(null)
    track(ref<TrackedContent | null>({ slug: 'a-slug' }))
    await nextTick()

    expect(pushEvent).not.toHaveBeenCalled()
  })
})
