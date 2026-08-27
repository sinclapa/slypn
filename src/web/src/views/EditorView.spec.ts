import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  // Partial: keep the real apiErrorMessage so error text is formatted as it is in the app.
  return { ...actual, apiJson, apiFetch }
})

// EditorView reads ?draft= to open a resumed revision directly.
const route = { query: {} as Record<string, string> }
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => route }
})

import EditorView from './EditorView.vue'
import { useAuthStore } from '@/stores/auth'
import { useEditorQueueStore } from '@/stores/editorQueue'
import { useApprovalsStore } from '@/stores/approvals'
import { DEV_PERSONA_STORAGE_KEY } from '@/lib/devPersonas'

const DraftEditorStub = {
  props: ['draftId', 'readonly', 'initialContent'],
  emits: ['close', 'saved', 'submitted'],
  template: `<div class="draft-editor-stub">
    <button @click="$emit('close')">Close editor</button>
    <button @click="$emit('saved', { id: draftId, title: 'Saved title', type: 'article', updatedAt: '2026-06-01T00:00:00Z', _etag: 'e2' })">Save draft</button>
    <button @click="$emit('submitted', draftId)">Submit draft</button>
  </div>`,
  methods: { flush() { return Promise.resolve() }, isDirty() { return false } },
}
const stubs = { teleport: true, DraftEditor: DraftEditorStub }
let pinia: Pinia
const mountV = () => mount(EditorView, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

const adminOid = '11111111-1111-1111-1111-111111111111'
const draftSummary = (over = {}) => ({ id: 'd1', title: 'A draft', type: 'article', updatedAt: '2026-05-01T00:00:00Z', _etag: 'e1', ...over })
const pendingItem = (over = {}) => ({
  id: 'r1', title: 'Pending Article', type: 'article', slug: 'pa-1',
  summary: 'sum', body: 'body', category: 'Community',
  readingMinutes: 3, publishedAt: '2026-05-01T00:00:00Z',
  authorId: adminOid, replacesArticleId: null, ...over,
})

function mockLists(drafts: unknown[]) {
  apiJson.mockImplementation((path: string) => {
    if (path === '/drafts') return Promise.resolve(drafts)
    return Promise.resolve([]) // in-review articles/blog
  })
}

function mockWithPending(drafts: unknown[], pending: unknown[]) {
  apiJson.mockImplementation((path: string) => {
    if (path === '/drafts') return Promise.resolve(drafts)
    // Only the articles queue: returning `pending` for both review endpoints served
    // every item twice, so a one-item fixture produced two rows and any assertion
    // about how many rows there are was measuring the mock.
    if (path === '/review/articles') return Promise.resolve(pending)
    return Promise.resolve([])
  })
}

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset(); apiFetch.mockReset()
  route.query = {}
  vi.stubGlobal('confirm', vi.fn(() => true))
  await useAuthStore().initialize()
})

describe('EditorView', () => {
  // ── The interrupted-submit window ──────────────────────────────────────────
  // Submitting crosses a table and a partition boundary, so it cannot be atomic.
  // The article is written before the draft is deleted, so an interruption leaves
  // the same id in both lists rather than losing the work.

  it('shows a half-submitted item once, as in-review', async () => {
    // Same id in both lists: the article reuses the draft's id on submit.
    mockWithPending([draftSummary({ id: 'x1', title: 'Caught mid-submit' })],
                    [pendingItem({ id: 'x1', title: 'Caught mid-submit' })])
    const w = mountV()
    await flushPromises()

    const rows = w.findAll('[data-testid="draft-row"]')
    expect(rows).toHaveLength(1)
    // The in-review side wins: it is the one that is true.
    expect(rows[0].attributes('data-state')).toBe('in-review')
  })

  it('still lists a draft that has not been submitted', async () => {
    mockWithPending([draftSummary({ id: 'd1' })], [pendingItem({ id: 'r1' })])
    const w = mountV()
    await flushPromises()

    const states = w.findAll('[data-testid="draft-row"]').map(r => r.attributes('data-state'))
    expect(states).toHaveLength(2)
    expect(states).toContain('draft')
    expect(states).toContain('in-review')
  })

  it('opens the draft named by ?item= on arrival', async () => {
    // Arriving from the edit affordance with a revision already in progress.
    route.query = { item: 'd1' }
    mockLists([draftSummary({ id: 'd1' })])
    const w = mountV()
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
  })

  it('opens a submission awaiting review read-only when named by ?item=', async () => {
    // Arriving from the edit affordance on an article whose revision is already in the
    // approvals queue: there is nothing to edit until an admin has dealt with it.
    route.query = { item: 'r1' }
    mockWithPending([], [pendingItem({ id: 'r1' })])
    const w = mountV()
    await flushPromises()
    const editor = w.findComponent(DraftEditorStub)
    expect(editor.exists()).toBe(true)
    expect(editor.props('readonly')).toBe(true)
  })

  it('ignores an ?item= that is not one of theirs', async () => {
    route.query = { item: 'someone-elses' }
    mockLists([draftSummary({ id: 'd1' })])
    const w = mountV()
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  // ── Withdraw from review ───────────────────────────────────────────────────

  it('offers Withdraw on an in-review row and not on a draft', async () => {
    mockWithPending([draftSummary()], [pendingItem()])
    const w = mountV()
    await flushPromises()

    const rows = w.findAll('[data-testid="draft-row"]')
    const withReview = rows.find(r => r.attributes('data-state') === 'in-review')!
    const withDraft  = rows.find(r => r.attributes('data-state') === 'draft')!
    expect(withReview.find('[data-testid="draft-row-withdraw"]').exists()).toBe(true)
    expect(withDraft.find('[data-testid="draft-row-withdraw"]').exists()).toBe(false)
  })

  it('withdraws a submission back into drafts', async () => {
    mockWithPending([], [pendingItem()])
    apiFetch.mockResolvedValue(ok(draftSummary({ id: 'r1', title: 'Pending Article' })))
    const w = mountV()
    await flushPromises()

    await w.find('[data-testid="draft-row-withdraw"]').trigger('click')
    await flushPromises()

    expect(apiFetch).toHaveBeenCalledWith('/content/r1/withdraw', { method: 'POST' })
    // It leaves review and arrives in drafts, so the row is editable rather than in-review.
    const rows = w.findAll('[data-testid="draft-row"]')
    expect(rows).toHaveLength(1)
    expect(rows[0].attributes('data-state')).toBe('draft')
    expect(w.text()).toContain('back in your drafts')
  })

  it('does not withdraw when the confirm is dismissed', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    mockWithPending([], [pendingItem()])
    const w = mountV()
    await flushPromises()

    await w.find('[data-testid="draft-row-withdraw"]').trigger('click')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalled()
  })

  it('surfaces a refusal from the API', async () => {
    mockWithPending([], [pendingItem()])
    apiFetch.mockResolvedValue({ ok: false, status: 403, statusText: 'Forbidden', text: () => Promise.resolve('You can only withdraw your own submissions.') } as unknown as Response)
    const w = mountV()
    await flushPromises()

    await w.find('[data-testid="draft-row-withdraw"]').trigger('click')
    await flushPromises()

    expect(w.find('[data-testid="withdraw-error"]').text()).toContain('your own submissions')
    // ...and it is still in review, not silently moved.
    expect(w.find('[data-testid="draft-row"]').attributes('data-state')).toBe('in-review')
  })


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
    await w.find('[data-testid="draft-row-open"]').trigger('click')
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

  it('shows in-review submissions and opens them read-only on click', async () => {
    mockWithPending([], [pendingItem()])
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('Pending Article')
    expect(w.text()).toContain('In review')
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
  })

  it('sorts pending items newest-first when there are multiple', async () => {
    mockWithPending([], [
      pendingItem({ id: 'r1', title: 'Older item', publishedAt: '2026-04-01T00:00:00Z' }),
      pendingItem({ id: 'r2', title: 'Newer item', publishedAt: '2026-06-01T00:00:00Z' }),
    ])
    const w = mountV()
    await flushPromises()
    const rows = w.findAll('[data-testid="draft-row-open"]').map(el => el.text())
    expect(rows[0]).toContain('Newer item')
  })

  it('closes the editor when the editor emits close', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
    await w.findAll('button').find(b => b.text() === 'Close editor')!.trigger('click')
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('updates the draft list when the editor emits saved', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    await w.findAll('button').find(b => b.text() === 'Save draft')!.trigger('click')
    expect(w.text()).toContain('Saved title')
  })

  it('removes draft and shows submit message when the editor emits submitted', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    await w.findAll('button').find(b => b.text() === 'Submit draft')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Submitted for admin review')
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('shows pending load error when in-review fetch fails', async () => {
    apiJson.mockImplementation((path: string) => {
      if (path === '/drafts') return Promise.resolve([])
      return Promise.reject(new Error('pending boom'))
    })
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('pending boom')
  })

  it('shows pending load error as string when rejection is not an Error', async () => {
    apiJson.mockImplementation((path: string) => {
      if (path === '/drafts') return Promise.resolve([])
      return Promise.reject('string pending error')
    })
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('string pending error')
  })

  it('shows draft list load error as string when rejection is not an Error', async () => {
    apiJson.mockImplementation((path: string) => {
      if (path === '/drafts') return Promise.reject('string list error')
      return Promise.resolve([])
    })
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('string list error')
  })

  it('shows a delete error when the API returns non-ok', async () => {
    mockLists([draftSummary()])
    apiFetch.mockResolvedValue({ ok: false, status: 409, statusText: 'Conflict', text: () => Promise.resolve('') } as unknown as Response)
    const w = mountV()
    await flushPromises()
    await w.find('button[title="Delete draft"]').trigger('click')
    await flushPromises()
    expect(w.text()).toContain('409')
  })

  it('closes the editor when the open draft is deleted', async () => {
    mockLists([draftSummary()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click') // open draft d1
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
    apiJson.mockResolvedValue([]) // refresh returns empty list
    await w.find('button[title="Delete draft"]').trigger('click')
    await flushPromises()
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('does not open the editor again when the same draft is already open', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
    const saveCount = (apiFetch as ReturnType<typeof vi.fn>).mock.calls.length
    await w.find('[data-testid="draft-row-open"]').trigger('click') // second click on same draft — should early-return
    expect((apiFetch as ReturnType<typeof vi.fn>).mock.calls).toHaveLength(saveCount)
  })

  it('does not re-open read-only when the same pending item is already open', async () => {
    mockWithPending([], [pendingItem()])
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
    await w.find('[data-testid="draft-row-open"]').trigger('click') // second click — early-return
    expect(w.find('.draft-editor-stub').exists()).toBe(true)
  })

  it('does not create a draft when the title is empty', async () => {
    mockLists([])
    const w = mountV()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('New draft'))!.trigger('click')
    await w.find('input[type="text"]').setValue('')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalled()
    expect(w.find('.draft-editor-stub').exists()).toBe(false)
  })

  it('adds new saved draft at head of list when id is not found', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    await w.find('[data-testid="draft-row-open"]').trigger('click')
    // emit saved with an id that does not exist in the current list
    await w.findAll('button').find(b => b.text() === 'Save draft')!.trigger('click')
    // The DraftEditorStub emits the same id (d1) which IS in the list — splice replaces it
    expect(w.text()).toContain('Saved title')
  })

  // ── Nav badge freshness ────────────────────────────────────────────────────
  // Both badges live in AppNav, which only refreshes them on mount. Every action
  // here changes a count, so each has to say so or the number in the admin menu
  // stays at whatever it was when the page loaded.

  function spyOnBadges() {
    const editorQueue = useEditorQueueStore(pinia)
    const approvals   = useApprovalsStore(pinia)
    const editorSpy    = vi.spyOn(editorQueue, 'refresh').mockResolvedValue(undefined)
    const approvalsSpy = vi.spyOn(approvals, 'refresh').mockResolvedValue(undefined)
    return { editorSpy, approvalsSpy }
  }

  it('refreshes the badges after deleting a draft', async () => {
    mockLists([draftSummary()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    const { editorSpy } = spyOnBadges()

    await w.find('button[title="Delete draft"]').trigger('click')
    await flushPromises()

    expect(editorSpy).toHaveBeenCalled()
  })

  it('does not refresh the badges when a delete fails', async () => {
    // A stale count is wrong; a count refreshed away from a document that is still
    // there is worse, because it tells the user the delete worked.
    mockLists([draftSummary()])
    apiFetch.mockResolvedValue({ ok: false, status: 500, text: () => Promise.resolve('boom') } as unknown as Response)
    const w = mountV()
    await flushPromises()
    const { editorSpy } = spyOnBadges()

    await w.find('button[title="Delete draft"]').trigger('click')
    await flushPromises()

    expect(editorSpy).not.toHaveBeenCalled()
  })

  it('refreshes approvals after submitting, since an admin now has one more to review', async () => {
    mockLists([draftSummary()])
    const w = mountV()
    await flushPromises()
    const { editorSpy, approvalsSpy } = spyOnBadges()

    await w.find('[data-testid="draft-row-open"]').trigger('click')
    await w.findAll('button').find(b => b.text() === 'Submit draft')!.trigger('click')
    await flushPromises()

    expect(editorSpy).toHaveBeenCalled()
    expect(approvalsSpy).toHaveBeenCalled()
  })

  it('refreshes approvals after withdrawing, since the item leaves the review queue', async () => {
    // The reported bug: withdraw refreshed only the editor count, so an admin's
    // Approvals badge kept counting a document that was no longer waiting on them.
    mockWithPending([], [pendingItem({ id: 'r1' })])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    const { editorSpy, approvalsSpy } = spyOnBadges()

    await w.find('[data-testid="draft-row-withdraw"]').trigger('click')
    await flushPromises()

    expect(editorSpy).toHaveBeenCalled()
    expect(approvalsSpy).toHaveBeenCalled()
  })

  it('leaves approvals alone for a contributor, who has no such badge', async () => {
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, 'contributor')
    pinia = createPinia()
    setActivePinia(pinia)
    await useAuthStore().initialize()
    mockLists([draftSummary()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    const { editorSpy, approvalsSpy } = spyOnBadges()

    await w.find('button[title="Delete draft"]').trigger('click')
    await flushPromises()

    expect(editorSpy).toHaveBeenCalled()
    expect(approvalsSpy).not.toHaveBeenCalled()
    localStorage.clear()
  })
})
