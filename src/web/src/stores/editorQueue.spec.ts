import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiFetch, apiJson: vi.fn() }
})

import { useEditorQueueStore } from './editorQueue'
import { useAuthStore } from '@/stores/auth'
import { DEV_PERSONA_STORAGE_KEY } from '@/lib/devPersonas'

const adminOid = '11111111-1111-1111-1111-111111111111'
const otherOid = '22222222-2222-2222-2222-222222222222'

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body) } as unknown as Response
}

function mockQueues(drafts: unknown[], articles: unknown[], blogs: unknown[]) {
  apiFetch.mockImplementation((path: string) => {
    if (path === '/drafts') return Promise.resolve(ok(drafts))
    if (path === '/review/articles') return Promise.resolve(ok(articles))
    if (path === '/review/blog') return Promise.resolve(ok(blogs))
    return Promise.resolve(ok([]))
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  apiFetch.mockReset()
  localStorage.clear()
})

describe('editorQueue store', () => {
  it('counts open drafts plus the caller’s own submissions', async () => {
    await useAuthStore().initialize() // dev-skip admin
    mockQueues([{ id: 'd1' }, { id: 'd2' }], [{ authorId: adminOid }], [{ authorId: adminOid }])

    const store = useEditorQueueStore()
    await store.refresh()

    expect(store.draftCount).toBe(2)
    expect(store.inReviewCount).toBe(2)
    expect(store.openCount).toBe(4)
  })

  it('ignores submissions by other authors', async () => {
    // /review/* is filtered to the caller for a Contributor, but an Admin sees
    // everyone's — counting those would tell an admin they have work open when
    // they have none.
    await useAuthStore().initialize()
    mockQueues([], [{ authorId: otherOid }, { authorId: otherOid }], [{ authorId: adminOid }])

    const store = useEditorQueueStore()
    await store.refresh()

    expect(store.inReviewCount).toBe(1)
    expect(store.openCount).toBe(1)
  })

  it('stays at zero when signed out, without calling the API', async () => {
    const store = useEditorQueueStore()
    await store.refresh()

    expect(store.openCount).toBe(0)
    expect(apiFetch).not.toHaveBeenCalled()
  })

  it('keeps the last known count when a request fails', async () => {
    await useAuthStore().initialize()
    mockQueues([{ id: 'd1' }], [], [])
    const store = useEditorQueueStore()
    await store.refresh()
    expect(store.openCount).toBe(1)

    apiFetch.mockResolvedValue({ ok: false, status: 500 } as unknown as Response)
    await store.refresh()
    expect(store.openCount).toBe(1) // a blip must not blank the badge
  })

  it('counts for a contributor persona too', async () => {
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, 'contributor')
    setActivePinia(createPinia())
    await useAuthStore().initialize()
    mockQueues([{ id: 'd1' }], [{ authorId: otherOid }], [])

    const store = useEditorQueueStore()
    await store.refresh()

    expect(store.openCount).toBe(2) // one draft + their own submission
  })
})
