import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

import SubscriberManagementView from './SubscriberManagementView.vue'
import { useAuthStore } from '@/stores/auth'

const stubs = { RouterLink: RouterLinkStub }
let pinia: Pinia
const mountC = () => mount(SubscriberManagementView, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

const subscriber = (over = {}) => ({
  id: 's1', email: 'sub@b.com', displayName: 'Subby',
  subscribedAt: '2026-04-01T00:00:00Z', _etag: 'e1', ...over,
})

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset()
  apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
  await useAuthStore().initialize()
})

describe('SubscriberManagementView', () => {
  it('lists subscribers with a count', async () => {
    apiJson.mockResolvedValue([subscriber()])
    const w = mountC()
    await flushPromises()
    expect(apiJson).toHaveBeenCalledWith('/subscribers')
    expect(w.text()).toContain('sub@b.com')
    expect(w.text()).toContain('Subby')
    expect(w.text()).toContain('(1)')
  })

  it('shows newest first', async () => {
    apiJson.mockResolvedValue([
      subscriber({ id: 'old', email: 'old@b.com', subscribedAt: '2025-01-01T00:00:00Z' }),
      subscriber({ id: 'new', email: 'new@b.com', subscribedAt: '2026-08-01T00:00:00Z' }),
    ])
    const w = mountC()
    await flushPromises()
    const rows = w.findAll('[data-testid="subscriber-row"]')
    expect(rows.map(r => r.attributes('data-id'))).toEqual(['new', 'old'])
  })

  it('removes a subscriber after confirmation, passing the etag', async () => {
    apiJson.mockResolvedValue([subscriber()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC()
    await flushPromises()
    await w.get('[data-testid="subscriber-remove"]').trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/subscribers/s1', expect.objectContaining({
      method: 'DELETE',
      headers: { 'If-Match': 'e1' },
    }))
  })

  it('does not remove when the confirmation is declined', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    apiJson.mockResolvedValue([subscriber()])
    const w = mountC()
    await flushPromises()
    await w.get('[data-testid="subscriber-remove"]').trigger('click')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalled()
  })

  it('surfaces a failed removal', async () => {
    apiJson.mockResolvedValue([subscriber()])
    apiFetch.mockResolvedValue({
      ok: false, status: 403, statusText: 'Forbidden', text: () => Promise.resolve(''),
    } as unknown as Response)
    const w = mountC()
    await flushPromises()
    await w.get('[data-testid="subscriber-remove"]').trigger('click')
    await flushPromises()
    expect(w.get('[data-testid="subscriber-save-error"]').text()).toContain('403')
  })

  describe('search', () => {
    const three = () => [
      subscriber({ id: 'a', email: 'alice@example.com', displayName: 'Alice Adams' }),
      subscriber({ id: 'b', email: 'bob@other.org', displayName: 'Bob Brown' }),
      subscriber({ id: 'c', email: 'carol@example.com', displayName: 'Carol Clark' }),
    ]

    async function mountWithSearch(term: string) {
      apiJson.mockResolvedValue(three())
      const w = mountC()
      await flushPromises()
      await w.get('[data-testid="subscriber-search"]').setValue(term)
      return w
    }

    it('filters on the email', async () => {
      const w = await mountWithSearch('example.com')
      const rows = w.findAll('[data-testid="subscriber-row"]')
      expect(rows.map(r => r.attributes('data-id'))).toEqual(['a', 'c'])
    })

    it('filters on the display name, case-insensitively', async () => {
      const w = await mountWithSearch('bOb')
      const rows = w.findAll('[data-testid="subscriber-row"]')
      expect(rows.map(r => r.attributes('data-id'))).toEqual(['b'])
    })

    it('ignores surrounding whitespace', async () => {
      const w = await mountWithSearch('   carol   ')
      expect(w.findAll('[data-testid="subscriber-row"]')).toHaveLength(1)
    })

    it('shows how many of the total matched', async () => {
      const w = await mountWithSearch('example.com')
      expect(w.text()).toContain('(2 of 3)')
    })

    it('shows a no-matches state that is distinct from having no subscribers', async () => {
      const w = await mountWithSearch('nobody')
      expect(w.findAll('[data-testid="subscriber-row"]')).toHaveLength(0)
      expect(w.get('[data-testid="subscriber-no-matches"]').text()).toContain('nobody')
      expect(w.text()).not.toContain('No subscribers yet')
    })

    it('restores the full list when cleared', async () => {
      const w = await mountWithSearch('alice')
      expect(w.findAll('[data-testid="subscriber-row"]')).toHaveLength(1)
      await w.get('[data-testid="subscriber-search-clear"]').trigger('click')
      expect(w.findAll('[data-testid="subscriber-row"]')).toHaveLength(3)
      expect(w.text()).toContain('(3)')
    })

    it('removes the subscriber the filter is showing, not the first of the unfiltered list', async () => {
      // The row list is what Remove is wired to, so a stale index here would delete the
      // wrong person — the one case where this filter could do real damage.
      apiFetch.mockResolvedValue(ok({}))
      const w = await mountWithSearch('carol')
      await w.get('[data-testid="subscriber-remove"]').trigger('click')
      await flushPromises()
      expect(apiFetch).toHaveBeenCalledWith('/subscribers/c', expect.objectContaining({ method: 'DELETE' }))
    })
  })

  it('renders an empty state', async () => {
    apiJson.mockResolvedValue([])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('No subscribers yet')
  })
})
