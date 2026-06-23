import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiFetch, apiJson: vi.fn() }))

import PublishedContent from './PublishedContent.vue'
import { useAuthStore } from '@/stores/auth'
import { DEV_PERSONA_STORAGE_KEY } from '@/lib/devPersonas'

const stubs = { teleport: true, DraftEditor: { template: '<div class="draft-editor-stub" />' } }
let pinia: Pinia
const mountC = () => mount(PublishedContent, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

const item = (over = {}) => ({
  id: 'i1', slug: 's', title: 'Live article', summary: 'sum', author: 'Jo',
  authorId: 'oid1', publishedAt: '2026-05-01T00:00:00Z', category: 'Community',
  type: 'article', status: 'published', ...over,
})

function mockLoad(published: unknown[], inReview: unknown[] = []) {
  apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
    const method = init?.method ?? 'GET'
    if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok(published))
    if (method === 'GET' && url === '/blog?status=published') return Promise.resolve(ok([]))
    if (method === 'GET' && url === '/articles?status=in-review') return Promise.resolve(ok(inReview))
    if (method === 'GET' && url === '/blog?status=in-review') return Promise.resolve(ok([]))
    return Promise.resolve(ok({}))
  })
}

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
  await useAuthStore().initialize() // dev-skip admin
})

describe('PublishedContent', () => {
  it('lists published items for an admin', async () => {
    mockLoad([item(), item({ id: 'i2', title: 'Second post', type: 'blog' })])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('Live article')
    expect(w.text()).toContain('Second post')
  })

  it('filters by type', async () => {
    mockLoad([item(), item({ id: 'i2', title: 'A blog', type: 'blog' })])
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Blogs')!.trigger('click')
    expect(w.text()).toContain('A blog')
    expect(w.text()).not.toContain('Live article')
  })

  it('searches title/summary', async () => {
    mockLoad([item(), item({ id: 'i2', title: 'Unique heading' })])
    const w = mountC()
    await flushPromises()
    await w.find('input[type="search"]').setValue('Unique')
    expect(w.text()).toContain('Unique heading')
    expect(w.text()).not.toContain('Live article')
  })

  it('opens the edit dialog after creating a revision draft', async () => {
    mockLoad([item()])
    apiFetch.mockImplementationOnce(() => Promise.resolve(ok([item()]))) // first load call still works via default below
    // re-apply load + edit behaviour
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/edit') && method === 'POST') return Promise.resolve(ok({ id: 'draft-1' }))
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok([item()]))
      if (method === 'GET') return Promise.resolve(ok([]))
      return Promise.resolve(ok({}))
    })
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/i1/edit', { method: 'POST' })
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
  })

  it('deletes an item as admin after confirmation', async () => {
    mockLoad([item()])
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/i1?status=published', { method: 'DELETE' })
    expect(w.text()).not.toContain('Live article')
  })

  it('marks items with a pending revision and disables edit', async () => {
    mockLoad([item()], [{ replacesArticleId: 'i1' }])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('Revision pending')
    const editBtn = w.findAll('button').find(b => b.text() === 'Edit')!
    expect(editBtn.attributes('disabled')).toBeDefined()
  })

  it('shows the empty state', async () => {
    mockLoad([])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('Nothing published yet')
  })

  it('lets a contributor request deletion of their own item', async () => {
    const memberOid = '33333333-3333-3333-3333-333333333333'
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, 'member')
    pinia = createPinia()
    setActivePinia(pinia)
    await useAuthStore().initialize() // member persona
    mockLoad([item({ authorId: memberOid })])
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/request-deletion') && method === 'POST') return Promise.resolve(ok(item({ authorId: memberOid, deletionRequestedBy: memberOid })))
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok([item({ authorId: memberOid })]))
      if (method === 'GET') return Promise.resolve(ok([]))
      return Promise.resolve(ok({}))
    })
    const w = mountC()
    await flushPromises()
    const reqBtn = w.findAll('button').find(b => b.text() === 'Request deletion')!
    expect(reqBtn).toBeTruthy()
    await reqBtn.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/i1/request-deletion', { method: 'POST' })
  })

  it('shows a load error', async () => {
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Err', json: () => Promise.resolve([]), text: () => Promise.resolve('') } as unknown as Response)
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('500')
  })
})
