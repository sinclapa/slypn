import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => ({ params: {}, query: {} }), useRouter: () => ({ push: vi.fn() }) }
})

import ApprovalsQueue from '@/components/common/ApprovalsQueue.vue'
import ResourceManagementView from './ResourceManagementView.vue'
import ApprovalsView from './ApprovalsView.vue'
import ManageContentView from './ManageContentView.vue'

const stubs = { RouterLink: RouterLinkStub, teleport: true }
let pinia: Pinia
const mountC = (C: unknown) => mount(C as never, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset()
  apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
})

const pending = (over = {}) => ({
  id: 'p1', slug: 's', title: 'Pending piece', summary: 'sum', body: '<p>body</p>',
  author: 'Jess', publishedAt: '2026-05-01T10:00:00Z', category: 'Community', tags: [],
  status: 'in-review', readingMinutes: 3, type: 'article', ...over,
})

describe('ApprovalsQueue', () => {
  function mockLoad(articles: unknown[], published: unknown[] = []) {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (method === 'GET' && url === '/articles?status=in-review') return Promise.resolve(ok(articles))
      if (method === 'GET' && url === '/blog?status=in-review') return Promise.resolve(ok([]))
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok(published))
      if (method === 'GET' && url === '/blog?status=published') return Promise.resolve(ok([]))
      return Promise.resolve(ok({}))
    })
  }

  it('renders pending articles grouped by author', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('Jess')
    expect(w.text()).toContain('Pending piece')
  })

  it('shows the empty state', async () => {
    mockLoad([])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('No submissions waiting')
  })

  it('approves (publishes) an article and removes it', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Approve')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/p1/publish', { method: 'POST' })
    expect(w.text()).not.toContain('Pending piece')
  })

  it('toggles the article body open', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Pending piece')!.trigger('click')
    expect(w.html()).toContain('<p>body</p>')
  })

  it('requests a revision with feedback', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    const textarea = w.find('textarea')
    await textarea.setValue('Please tighten the intro')
    await w.findAll('button').find(b => b.text() === 'Send back for revision')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/p1/revise', expect.objectContaining({ method: 'POST' }))
    expect(w.text()).not.toContain('Pending piece')
  })

  it('lists deletion requests and approves a deletion', async () => {
    mockLoad([], [pending({ id: 'd1', title: 'Old post', deletionRequestedBy: 'u9' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('Deletion requests')
    expect(w.text()).toContain('Old post')
    await w.findAll('button').find(b => b.text() === 'Approve deletion')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/d1?status=published', { method: 'DELETE' })
  })

  it('shows a load error', async () => {
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Server Error', json: () => Promise.resolve([]), text: () => Promise.resolve('') } as unknown as Response)
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('500')
  })
})

describe('ResourceManagementView', () => {
  const resource = (over = {}) => ({ id: 'r1', title: 'Helpline', description: 'd', url: 'https://x.org/a', category: 'NHS', _etag: 'e1', ...over })

  it('renders resources grouped by category', async () => {
    apiJson.mockResolvedValue([resource()])
    const w = mountC(ResourceManagementView)
    await flushPromises()
    expect(w.text()).toContain('Helpline')
    expect(w.text()).toContain('NHS')
  })

  it('adds a resource via the dialog', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Add resource'))!.trigger('click')
    await w.find('input[type="text"]').setValue('New link')
    await w.find('textarea').setValue('desc')
    await w.find('input[type="url"]').setValue('https://new.example')
    const catInput = w.findAll('input').find(i => i.attributes('list') === 'resource-category-hints')!
    await catInput.setValue('Local')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/resources', expect.objectContaining({ method: 'POST' }))
  })

  it('deletes a resource after confirmation', async () => {
    apiJson.mockResolvedValue([resource()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith(expect.stringContaining('/resources/r1?category=NHS'), expect.objectContaining({ method: 'DELETE' }))
  })

  it('shows the empty state', async () => {
    apiJson.mockResolvedValue([])
    const w = mountC(ResourceManagementView)
    await flushPromises()
    expect(w.text()).toContain('No resources yet')
  })
})

describe('admin wrapper views', () => {
  it('ApprovalsView mounts its queue', async () => {
    apiFetch.mockResolvedValue(ok([]))
    const w = mountC(ApprovalsView)
    await flushPromises()
    expect(w.text()).toContain('Approvals')
  })

  it('ManageContentView mounts published content', async () => {
    apiFetch.mockResolvedValue(ok([]))
    apiJson.mockResolvedValue([])
    const w = mountC(ManageContentView)
    await flushPromises()
    expect(w.text()).toContain('Content management')
  })
})
