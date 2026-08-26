import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiFetch, apiJson: vi.fn() }
})

const push = vi.fn()
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRouter: () => ({ push }) }
})

import EditContentButton from './EditContentButton.vue'
import { useAuthStore } from '@/stores/auth'

let pinia: Pinia

// 201 = a revision was minted, so the editor opens here. 200 = one was already on the
// go, which navigates to the editor page instead.
function created(body: unknown) {
  return { ok: true, status: 201, statusText: 'Created', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

// Stands in for the real editor so the modal can be driven without TipTap.
const dirtyEditor = (dirty: boolean) => ({
  name: 'DraftEditor',
  template: '<div class="draft-editor-stub" />',
  setup(_p: unknown, { expose }: { expose: (e: unknown) => void }) {
    expose({ isDirty: () => dirty, flush: vi.fn() })
  },
})

const mountBtn = (props = {}, stubs = {}) =>
  mount(EditContentButton, {
    props: { contentId: 'a1', canEdit: true, ...props },
    global: { plugins: [pinia], stubs: { DraftEditor: { template: '<div class="draft-editor-stub" />' }, ...stubs } },
  })

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiFetch.mockReset()
  push.mockClear()
  localStorage.clear()
  await useAuthStore().initialize() // dev-skip admin
})

describe('EditContentButton', () => {
  it('goes to the editor when a revision is already on the go', async () => {
    // 200 means the API handed back an existing revision rather than minting one, so
    // opening a modal here would be a second window onto work in progress.
    apiFetch.mockResolvedValue({ ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve({ id: 'draft-7' }), text: () => Promise.resolve('') } as unknown as Response)
    const w = mountBtn()
    await w.find('[data-testid="edit-content"]').trigger('click')
    await flushPromises()

    expect(push).toHaveBeenCalledWith({ path: '/editor', query: { draft: 'draft-7' } })
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('opens the modal in place when the revision is new', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 201, statusText: 'Created', json: () => Promise.resolve({ id: 'draft-8' }), text: () => Promise.resolve('') } as unknown as Response)
    const w = mountBtn()
    await w.find('[data-testid="edit-content"]').trigger('click')
    await flushPromises()

    expect(push).not.toHaveBeenCalled()
  })

  it('renders nothing when the API says the caller may not edit', () => {
    const w = mountBtn({ canEdit: false })
    expect(w.find('[data-testid="edit-content"]').exists()).toBe(false)
  })

  it('renders nothing when signed out, even if canEdit is true', async () => {
    // Dev-skip resolves a header-less API caller to the admin persona, so canEdit
    // alone would light this up for a signed-out visitor locally.
    pinia = createPinia()
    setActivePinia(pinia) // fresh store, never initialised => not authenticated
    const w = mountBtn({ canEdit: true })
    expect(w.find('[data-testid="edit-content"]').exists()).toBe(false)
  })

  it('creates a revision draft and opens the editor', async () => {
    apiFetch.mockResolvedValue(created({ id: 'draft-9' }))
    const w = mountBtn()
    await w.find('[data-testid="edit-content"]').trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/articles/a1/edit', { method: 'POST' })
  })

  it('surfaces the error when the API refuses', async () => {
    apiFetch.mockResolvedValue({ ok: false, status: 403, statusText: 'Forbidden', text: () => Promise.resolve('You can only edit your own published content.') } as unknown as Response)
    const w = mountBtn()
    await w.find('[data-testid="edit-content"]').trigger('click')
    await flushPromises()
    expect(w.find('[data-testid="edit-content-error"]').text()).toContain('your own')
  })

  it('discards the freshly-minted draft when it was closed untouched', async () => {
    // /edit creates the draft up front, so opening and closing without typing would
    // otherwise leave an orphan in the author's editor queue.
    apiFetch.mockResolvedValue(created({ id: 'draft-9' }))
    const w = mountBtn({}, { DraftEditor: dirtyEditor(false) })
    await w.find('[data-testid="edit-content"]').trigger('click')
    await flushPromises()

    apiFetch.mockClear()
    await w.findComponent({ name: 'DraftEditor' }).vm.$emit('close')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/drafts/draft-9', { method: 'DELETE' })
  })

  it('keeps the draft when it was edited', async () => {
    apiFetch.mockResolvedValue(created({ id: 'draft-9' }))
    const w = mountBtn({}, { DraftEditor: dirtyEditor(true) })
    await w.find('[data-testid="edit-content"]').trigger('click')
    await flushPromises()

    apiFetch.mockClear()
    await w.findComponent({ name: 'DraftEditor' }).vm.$emit('close')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalledWith('/drafts/draft-9', { method: 'DELETE' })
  })
})
