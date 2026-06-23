import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

import EditorView from './EditorView.vue'
import { useAuthStore } from '@/stores/auth'

const DraftEditorStub = {
  props: ['draftId', 'readonly', 'initialContent'],
  template: '<div class="draft-editor-stub" />',
  methods: { flush() { return Promise.resolve() }, isDirty() { return false } },
}
const stubs = { teleport: true, DraftEditor: DraftEditorStub }
let pinia: Pinia
const mountV = () => mount(EditorView, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

const draftSummary = (over = {}) => ({ id: 'd1', title: 'A draft', type: 'article', updatedAt: '2026-05-01T00:00:00Z', _etag: 'e1', ...over })

function mockLists(drafts: unknown[]) {
  apiJson.mockImplementation((path: string) => {
    if (path === '/drafts') return Promise.resolve(drafts)
    return Promise.resolve([]) // in-review article/blog
  })
}

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset(); apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
  await useAuthStore().initialize()
})

describe('EditorView', () => {
  it('lists existing drafts', async () => {
    mockLists([draftSummary(), draftSummary({ id: 'd2', title: 'Second draft' })])
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('A draft')
    expect(w.text()).toContain('Second draft')
  })

  it('shows the empty state', async () => {
    mockLists([])
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('No drafts or submissions yet')
  })

  it('creates a new draft and opens the editor', async () => {
    mockLists([])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('New draft'))!.trigger('click')
    await w.find('input[type="text"]').setValue('Fresh idea')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith(expect.stringMatching(/^\/drafts\//), expect.objectContaining({ method: 'PUT' }))
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
  })

  it('opens an existing draft when its row is clicked', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    await w.find('[role="button"]').trigger('click')
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
  })

  it('deletes a draft after confirmation', async () => {
    mockLists([draftSummary()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    await w.find('button[title="Delete draft"]').trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/drafts/d1', expect.objectContaining({ method: 'DELETE' }))
    expect(w.text()).toContain('No drafts or submissions yet')
  })

  it('shows a list load error', async () => {
    apiJson.mockImplementation((path: string) => {
      if (path === '/drafts') return Promise.reject(new Error('list boom'))
      return Promise.resolve([])
    })
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('list boom')
  })
})
