import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiJson, apiFetch }
})
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
  author: 'Jess', publishedAt: '2026-05-01T10:00:00Z', category: 'Community',
  status: 'in-review', readingMinutes: 3, type: 'article', ...over,
})

describe('ApprovalsQueue', () => {
  function mockLoad(articles: unknown[], published: unknown[] = []) {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (method === 'GET' && url === '/review/articles') return Promise.resolve(ok(articles))
      if (method === 'GET' && url === '/review/blog') return Promise.resolve(ok([]))
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

  it('sorts pending articles by publishedAt when there are multiple', async () => {
    mockLoad([
      pending({ id: 'p1', title: 'Older piece', publishedAt: '2026-04-01T10:00:00Z' }),
      pending({ id: 'p2', title: 'Newer piece', publishedAt: '2026-05-01T10:00:00Z' }),
    ])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('Older piece')
    expect(w.text()).toContain('Newer piece')
  })

  it('shows an error under an article when publish returns non-ok', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 503, statusText: 'Service Unavailable', text: () => Promise.resolve('') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Approve')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('503')
  })

  it('shows an error when approveDeletion returns non-ok', async () => {
    mockLoad([], [pending({ id: 'd1', title: 'Old post', deletionRequestedBy: 'u9' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 422, statusText: 'Unprocessable', text: () => Promise.resolve('') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Approve deletion')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('422')
  })

  it('cancels deletion via keepArticle and removes the item', async () => {
    mockLoad([], [pending({ id: 'd1', title: 'Keep me', deletionRequestedBy: 'u9' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('Keep me')
    apiFetch.mockResolvedValue(ok({}))
    await w.findAll('button').find(b => b.text() === 'Keep')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/d1/cancel-deletion', { method: 'POST' })
    expect(w.text()).not.toContain('Keep me')
  })

  it('shows an error when keepArticle returns non-ok', async () => {
    mockLoad([], [pending({ id: 'd1', title: 'Keep fail', deletionRequestedBy: 'u9' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Error', text: () => Promise.resolve('') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Keep')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('500')
  })

  it('disables the send button when revision feedback is shorter than 5 characters', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    await w.find('textarea').setValue('Hi')
    await flushPromises()
    const sendBtn = w.findAll('button').find(b => b.text() === 'Send back for revision')!
    expect(sendBtn.attributes('disabled')).toBeDefined()
  })

  it('shows an error when confirmRevise returns non-ok', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 409, statusText: 'Conflict', text: () => Promise.resolve('') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    await w.find('textarea').setValue('Please rewrite the introduction section')
    await w.findAll('button').find(b => b.text() === 'Send back for revision')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('409')
  })

  it('groups articles with empty author under Unknown', async () => {
    mockLoad([pending({ author: '' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('Unknown')
  })

  it('shows load error as string when rejection is not an Error', async () => {
    apiFetch.mockRejectedValue('net error')
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('net error')
  })

  it('shows load error when blog returns non-ok', async () => {
    apiFetch.mockImplementation((url: string) => {
      if (url === '/review/articles') return ok([])
      if (url === '/review/blog') return Promise.resolve({ ok: false, status: 503, statusText: 'Unavailable', text: () => Promise.resolve(''), json: () => Promise.resolve([]) } as unknown as Response)
      return ok([])
    })
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).toContain('503')
  })

  it('skips loading deletions when published article list returns non-ok', async () => {
    apiFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET')
      if (method === 'GET' && url === '/review/articles') return ok([])
      if (method === 'GET' && url === '/review/blog') return ok([])
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve({ ok: false, status: 503, statusText: 'Unavailable', json: () => Promise.resolve([]) } as unknown as Response)
      if (method === 'GET' && url === '/blog?status=published') return ok([])
      return ok({})
    })
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    expect(w.text()).not.toContain('Approve deletion')
  })

  it('shows publish error body text in the error message', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 503, statusText: 'Unavailable', text: () => Promise.resolve('backend down') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Approve')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('backend down')
  })

  it('shows publish error as string when rejection is not an Error', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockRejectedValue('pub failed')
    await w.findAll('button').find(b => b.text() === 'Approve')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('pub failed')
  })

  it('shows approveDeletion error body text in the error message', async () => {
    mockLoad([], [pending({ deletionRequestedBy: 'someone' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Error', text: () => Promise.resolve('cascade error') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Approve deletion')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('cascade error')
  })

  it('shows approveDeletion error as string when rejection is not an Error', async () => {
    mockLoad([], [pending({ deletionRequestedBy: 'someone' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockRejectedValue('del failed')
    await w.findAll('button').find(b => b.text() === 'Approve deletion')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('del failed')
  })

  it('shows keepArticle error body text in the error message', async () => {
    mockLoad([], [pending({ deletionRequestedBy: 'someone' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Error', text: () => Promise.resolve('keep error body') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Keep')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('keep error body')
  })

  it('shows keepArticle error as string when rejection is not an Error', async () => {
    mockLoad([], [pending({ deletionRequestedBy: 'someone' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockRejectedValue('keep failed')
    await w.findAll('button').find(b => b.text() === 'Keep')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('keep failed')
  })

  it('shows confirmRevise error body text in the error message', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 422, statusText: 'Unprocessable', text: () => Promise.resolve('revision body') } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    await w.find('textarea').setValue('Rewrite the whole introduction please')
    await w.findAll('button').find(b => b.text() === 'Send back for revision')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('revision body')
  })

  it('shows confirmRevise error as string when rejection is not an Error', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    apiFetch.mockRejectedValue('revise failed')
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    await w.find('textarea').setValue('Rewrite the whole introduction please')
    await w.findAll('button').find(b => b.text() === 'Send back for revision')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('revise failed')
  })

  it('toggles the body of a deletion-request item when its title is clicked', async () => {
    mockLoad([], [pending({ id: 'd1', title: 'Deletion item', body: '<p>del body</p>', deletionRequestedBy: 'u9' })])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Deletion item')!.trigger('click')
    expect(w.html()).toContain('<p>del body</p>')
  })

  it('closes the revise dialog when the backdrop is clicked', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    expect(w.find('textarea').exists()).toBe(true)
    const backdrop = w.find('.fixed.inset-0')
    await backdrop.trigger('mousedown')
    expect(w.find('textarea').exists()).toBe(false)
  })

  it('closes the revise dialog when ESC is pressed in the textarea', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    expect(w.find('textarea').exists()).toBe(true)
    await w.find('textarea').trigger('keydown', { key: 'Escape' })
    expect(w.find('textarea').exists()).toBe(false)
  })

  it('closes the revise dialog when Cancel is clicked', async () => {
    mockLoad([pending()])
    const w = mountC(ApprovalsQueue)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Revise')!.trigger('click')
    expect(w.find('textarea').exists()).toBe(true)
    await w.findAll('button').find(b => b.text() === 'Cancel')!.trigger('click')
    expect(w.find('textarea').exists()).toBe(false)
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

  it('opens the edit dialog pre-filled when Edit is clicked', async () => {
    apiJson.mockResolvedValue([resource()])
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    expect(w.text()).toContain('Edit resource')
    const titleInput = w.find('input[type="text"]')
    expect((titleInput.element as HTMLInputElement).value).toBe('Helpline')
  })

  it('saves an edited resource via PUT', async () => {
    apiJson.mockResolvedValue([resource()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await w.find('input[type="text"]').setValue('Updated Title')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/resources/r1', expect.objectContaining({ method: 'PUT' }))
  })

  it('shows a form error when save returns non-ok', async () => {
    apiJson.mockResolvedValue([resource()])
    apiFetch.mockResolvedValue({ ok: false, status: 409, statusText: 'Conflict', text: () => Promise.resolve('version conflict') } as unknown as Response)
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('409')
  })

  it('shows a list error when delete returns non-ok', async () => {
    apiJson.mockResolvedValue([resource()])
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Error', text: () => Promise.resolve('') } as unknown as Response)
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('500')
  })

  it('shows the load error state when apiJson rejects', async () => {
    apiJson.mockRejectedValue(new Error('network failure'))
    const w = mountC(ResourceManagementView)
    await flushPromises()
    expect(w.text()).toContain("Couldn’t load resources")
    expect(w.text()).toContain('Retry')
  })

  it('groups a resource with no category under Uncategorised', async () => {
    apiJson.mockResolvedValue([resource({ category: '' })])
    const w = mountC(ResourceManagementView)
    await flushPromises()
    expect(w.text()).toContain('Uncategorised')
  })

  it('groups two resources in the same category under one heading', async () => {
    apiJson.mockResolvedValue([
      resource({ id: 'r1', title: 'First link', category: 'NHS' }),
      resource({ id: 'r2', title: 'Second link', category: 'NHS' }),
    ])
    const w = mountC(ResourceManagementView)
    await flushPromises()
    expect(w.text()).toContain('First link')
    expect(w.text()).toContain('Second link')
  })

  it('cancels deletion when confirm returns false', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    apiJson.mockResolvedValue([resource()])
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalledWith(expect.stringContaining('/resources/r1'), expect.objectContaining({ method: 'DELETE' }))
  })

  it('shows form save error as string when rejection is not an Error', async () => {
    apiJson.mockResolvedValue([resource()])
    apiFetch.mockRejectedValue('save failed')
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('save failed')
  })

  it('shows delete error as string when rejection is not an Error', async () => {
    apiJson.mockResolvedValue([resource()])
    apiFetch.mockRejectedValue('delete failed')
    const w = mountC(ResourceManagementView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('delete failed')
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
