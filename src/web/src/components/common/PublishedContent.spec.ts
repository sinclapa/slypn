import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { defineComponent, h } from 'vue'

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiFetch, apiJson: vi.fn() }
})

import PublishedContent from './PublishedContent.vue'
import { useAuthStore } from '@/stores/auth'
import { DEV_PERSONA_STORAGE_KEY } from '@/lib/devPersonas'

const isDirtyMock = vi.fn(() => false)
const flushMock = vi.fn().mockResolvedValue(undefined)

const DraftEditorStub = defineComponent({
  name: 'DraftEditor',
  props: ['draftId'],
  emits: ['close', 'submitted'],
  setup(_, { expose }) {
    expose({ isDirty: isDirtyMock, flush: flushMock })
    return {}
  },
  render() { return h('div', { class: 'draft-editor-stub' }) },
})

const stubs = { teleport: true, DraftEditor: DraftEditorStub }
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
  localStorage.removeItem(DEV_PERSONA_STORAGE_KEY)
  apiFetch.mockReset()
  isDirtyMock.mockReturnValue(false)
  flushMock.mockResolvedValue(undefined)
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
    expect(w.text()).toContain('Everything live')
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

  // ── Load branch coverage ─────────────────────────────────────────────────────

  it('shows a load error when the blog endpoint returns non-ok', async () => {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok([]))
      if (method === 'GET' && url === '/blog?status=published')
        return Promise.resolve({ ok: false, status: 503, statusText: 'Service Unavailable', json: () => Promise.resolve([]), text: () => Promise.resolve('') } as unknown as Response)
      return Promise.resolve(ok([]))
    })
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('503')
  })

  it('does not mark revision pending for in-review items without replacesArticleId', async () => {
    mockLoad([item()], [{ id: 'r1', title: 'Draft without replaces' }])
    const w = mountC()
    await flushPromises()
    expect(w.text()).not.toContain('Revision pending')
  })

  it('shows a load error as string when fetch rejects with a non-Error value', async () => {
    apiFetch.mockRejectedValue('connection refused')
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('connection refused')
  })

  // ── Edit dialog branch coverage ──────────────────────────────────────────────

  it('shows an item error when the edit POST returns non-ok', async () => {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/edit') && method === 'POST')
        return Promise.resolve({ ok: false, status: 409, statusText: 'Conflict', text: () => Promise.resolve('etag mismatch'), json: () => Promise.resolve({}) } as unknown as Response)
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok([item()]))
      if (method === 'GET') return Promise.resolve(ok([]))
      return Promise.resolve(ok({}))
    })
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('409')
  })

  it('shows an item error as string when the edit POST throws a non-Error value', async () => {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/edit') && method === 'POST') return Promise.reject('edit string error')
      if (method === 'GET' && url === '/articles?status=published') return Promise.resolve(ok([item()]))
      if (method === 'GET') return Promise.resolve(ok([]))
      return Promise.resolve(ok({}))
    })
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('edit string error')
  })

  it('deletes the draft and reloads when the editor is closed without edits', async () => {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/edit') && method === 'POST') return Promise.resolve(ok({ id: 'draft-1' }))
      if (method === 'DELETE') return Promise.resolve(ok({}))
      if (method === 'GET') return Promise.resolve(ok([item()]))
      return Promise.resolve(ok({}))
    })
    isDirtyMock.mockReturnValue(false)
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
    await w.findComponent(DraftEditorStub).vm.$emit('close')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/drafts/draft-1', { method: 'DELETE' })
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('flushes the editor when closed with unsaved edits', async () => {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/edit') && method === 'POST') return Promise.resolve(ok({ id: 'draft-2' }))
      if (method === 'GET') return Promise.resolve(ok([item()]))
      return Promise.resolve(ok({}))
    })
    isDirtyMock.mockReturnValue(true)
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await flushPromises()
    await w.findComponent(DraftEditorStub).vm.$emit('close')
    await flushPromises()
    expect(flushMock).toHaveBeenCalled()
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('closes the dialog and reloads when the editor emits submitted', async () => {
    apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
      const method = init?.method ?? 'GET'
      if (url.endsWith('/edit') && method === 'POST') return Promise.resolve(ok({ id: 'draft-3' }))
      if (method === 'GET') return Promise.resolve(ok([item()]))
      return Promise.resolve(ok({}))
    })
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
    await w.findComponent(DraftEditorStub).vm.$emit('submitted')
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  // ── Remove branch coverage ───────────────────────────────────────────────────

  it('cancels deletion when the user declines the confirm dialog', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    mockLoad([item()])
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalledWith('/articles/i1?status=published', expect.objectContaining({ method: 'DELETE' }))
    expect(w.text()).toContain('Live article')
  })

  it('shows an item error when delete returns non-ok', async () => {
    mockLoad([item()])
    const w = mountC()
    await flushPromises()
    apiFetch.mockResolvedValue({ ok: false, status: 422, statusText: 'Unprocessable', text: () => Promise.resolve('version conflict'), json: () => Promise.resolve({}) } as unknown as Response)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('version conflict')
  })

  it('shows delete error as string when removal throws a non-Error value', async () => {
    mockLoad([item()])
    const w = mountC()
    await flushPromises()
    apiFetch.mockRejectedValue('remove string error')
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('remove string error')
  })

  it('uses "blog post" in the confirm prompt for blog-type items', async () => {
    const confirmSpy = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmSpy)
    mockLoad([item({ type: 'blog', id: 'b1', title: 'My Blog', authorId: 'oid1' })])
    const w = mountC()
    await flushPromises()
    apiFetch.mockResolvedValue(ok({}))
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(confirmSpy).toHaveBeenCalledWith(expect.stringContaining('blog post'))
  })

  // ── Pagination coverage ──────────────────────────────────────────────────────

  it('paginates and navigates pages when there are more than 10 items', async () => {
    const items = Array.from({ length: 12 }, (_, i) => item({ id: `i${i}`, title: `Article ${i}` }))
    mockLoad(items)
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('page 1 of 2')
    const prevBtn = w.findAll('button').find(b => b.text() === 'Previous')!
    expect(prevBtn.attributes('disabled')).toBeDefined()
    const nextBtn = w.findAll('button').find(b => b.text() === 'Next')!
    expect(nextBtn.attributes('disabled')).toBeUndefined()
    await nextBtn.trigger('click')
    expect(w.text()).toContain('page 2 of 2')
    expect(w.findAll('button').find(b => b.text() === 'Next')!.attributes('disabled')).toBeDefined()
    expect(w.findAll('button').find(b => b.text() === 'Previous')!.attributes('disabled')).toBeUndefined()
  })

  // ── No-match text coverage ───────────────────────────────────────────────────

  it('shows the search term in the no-match text when filtered result is empty', async () => {
    mockLoad([item()])
    const w = mountC()
    await flushPromises()
    await w.find('input[type="search"]').setValue('NOMATCH_XYZ')
    expect(w.text()).toContain('NOMATCH_XYZ')
  })

  it('uses "articles" in the no-match text when the article type filter is active', async () => {
    mockLoad([item({ type: 'blog', id: 'b1', title: 'A blog only' })])
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Articles')!.trigger('click')
    expect(w.text()).toContain('articles')
  })

  it('uses "blog posts" in the no-match text when the blog type filter is active', async () => {
    mockLoad([item()])
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Blogs')!.trigger('click')
    expect(w.text()).toContain('blog posts')
  })

  it('shows the non-admin description for a contributor', async () => {
    const memberOid = '33333333-3333-3333-3333-333333333333'
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, 'member')
    pinia = createPinia()
    setActivePinia(pinia)
    await useAuthStore().initialize()
    mockLoad([item({ authorId: memberOid })])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('Your published articles')
  })
})
