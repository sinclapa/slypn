import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const apiFetch = vi.fn()
vi.mock('@/lib/api', () => ({ apiFetch: (...args: unknown[]) => apiFetch(...args) }))

import { useApprovalsStore } from './approvals'

function res(body: unknown, ok = true) {
  return { ok, json: () => Promise.resolve(body) } as unknown as Response
}

beforeEach(() => {
  setActivePinia(createPinia())
  apiFetch.mockReset()
})

describe('approvals store', () => {
  it('counts in-review articles + blog plus pending deletions', async () => {
    apiFetch
      .mockResolvedValueOnce(res([{ id: 'a1' }, { id: 'a2' }]))           // articles in-review
      .mockResolvedValueOnce(res([{ id: 'b1' }]))                          // blog in-review
      .mockResolvedValueOnce(res([{ deletionRequestedBy: 'u1' }, {}]))     // published articles
      .mockResolvedValueOnce(res([{ deletionRequestedBy: 'u2' }]))         // published blog
    const store = useApprovalsStore()
    await store.refresh()
    // 2 + 1 in-review, plus 2 deletion requests
    expect(store.pendingCount).toBe(5)
  })

  it('bails out without updating when an in-review request fails', async () => {
    apiFetch
      .mockResolvedValueOnce(res([], false))
      .mockResolvedValueOnce(res([]))
      .mockResolvedValueOnce(res([]))
      .mockResolvedValueOnce(res([]))
    const store = useApprovalsStore()
    await store.refresh()
    expect(store.pendingCount).toBe(0)
  })

  it('swallows thrown errors and keeps the last count', async () => {
    apiFetch.mockRejectedValue(new Error('network'))
    const store = useApprovalsStore()
    await store.refresh()
    expect(store.pendingCount).toBe(0)
  })

  it('ignores deletion counts when published fetches fail', async () => {
    apiFetch
      .mockResolvedValueOnce(res([{ id: 'a1' }]))
      .mockResolvedValueOnce(res([{ id: 'b1' }]))
      .mockResolvedValueOnce(res([], false))
      .mockResolvedValueOnce(res([], false))
    const store = useApprovalsStore()
    await store.refresh()
    expect(store.pendingCount).toBe(2)
  })
})
