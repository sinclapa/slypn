import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiFetch }
})

import DraftEditor from './DraftEditor.vue'

function resp(body: unknown, init: { ok?: boolean; status?: number; etag?: string } = {}) {
  return {
    ok: init.ok ?? true,
    status: init.status ?? 200,
    statusText: 'OK',
    headers: { get: () => init.etag ?? 'etag-1' },
    json: () => Promise.resolve(body),
    // A string body is the response text — plain-text refusals arrive that way.
    text: () => Promise.resolve(typeof body === 'string' ? body : ''),
  } as unknown as Response
}

const draftPayload = {
  type: 'article', title: 'My draft', slug: '', summary: 'A summary',
  body: '<p>Real content here</p>', category: 'Community', readingMinutes: 1,
}

function mockApi(over: (url: string, method: string) => Response | undefined = () => undefined) {
  apiFetch.mockImplementation((url: string, init?: { method?: string }) => {
    const method = init?.method ?? 'GET'
    if (typeof url !== 'string') return Promise.resolve(resp({}))
    const custom = over(url, method)
    if (custom) return Promise.resolve(custom)
    if (url.startsWith('/drafts/') && method === 'GET') return Promise.resolve(resp(draftPayload))
    if (url === '/articles?status=published') return Promise.resolve(resp([{ category: 'Community' }]))
    if (url === '/blog?status=published') return Promise.resolve(resp([{ category: 'News' }]))
    if (url.startsWith('/drafts/') && method === 'PUT') return Promise.resolve(resp({}))
    if (url.endsWith('/submit') && method === 'POST') return Promise.resolve(resp({}))
    return Promise.resolve(resp({}))
  })
}

const mountEditor = (props: Record<string, unknown> = {}) =>
  mount(DraftEditor, { props: { draftId: 'd1', ...props }, global: { stubs: { teleport: true } } })

beforeEach(() => apiFetch.mockReset())

describe('DraftEditor', () => {
  // ── Field limits ───────────────────────────────────────────────────────────
  // maxlength stops typing silently, which reads as the app breaking rather than a
  // limit being reached. The counters appear once the limit is close enough to matter.

  it('hides the summary counter until the limit is close', async () => {
    const w = mountEditor()
    await flushPromises()
    expect(w.find('[data-testid="draft-summary-count"]').exists()).toBe(false)

    await w.find('#draft-summary').setValue('x'.repeat(399)) // 79.8% of 500
    expect(w.find('[data-testid="draft-summary-count"]').exists()).toBe(false)
  })

  it('shows the summary counter as it fills, and says so at the limit', async () => {
    const w = mountEditor()
    await flushPromises()

    await w.find('#draft-summary').setValue('x'.repeat(450))
    const counter = w.find('[data-testid="draft-summary-count"]')
    expect(counter.text()).toContain('450 / 500')
    expect(counter.text()).not.toContain('limit reached')

    await w.find('#draft-summary').setValue('x'.repeat(500))
    expect(w.find('[data-testid="draft-summary-count"]').text()).toContain('limit reached')
  })


  it('counts the body as text the reader sees, not as markup', async () => {
    // 45,000 visible characters, but far more than that as HTML once every paragraph
    // wrapper and link href is counted. Charging for markup the reader never sees would
    // make the figure meaningless — and would stop an author well short of the limit.
    const visible = 'x'.repeat(45_000)
    const body = `<p><a href="https://example.com/a/fairly/long/url">${visible}</a></p>`
    expect(body.length).toBeGreaterThan(45_050)

    mockApi((url, method) => {
      if (url.startsWith('/drafts/') && method === 'GET') return resp({ ...draftPayload, body })
      return undefined
    })
    const w = mountEditor()
    await flushPromises()

    const counter = w.find('[data-testid="draft-body-count"]')
    expect(counter.exists()).toBe(true)
    expect(counter.text()).toContain('45000 / 50000')
    expect(counter.text()).not.toContain('limit reached')
  })

  it('counts the title and category too', async () => {
    const w = mountEditor()
    await flushPromises()
    await w.find('#draft-title').setValue('t'.repeat(180))
    await w.find('#draft-category').setValue('c'.repeat(55))
    expect(w.find('[data-testid="draft-title-count"]').text()).toContain('180 / 200')
    expect(w.find('[data-testid="draft-category-count"]').text()).toContain('55 / 60')
  })

  it('clears the category', async () => {
    const w = mountEditor()
    await flushPromises()
    await w.find('#draft-category').setValue('Community')
    expect(w.find('[data-testid="draft-category-clear"]').exists()).toBe(true)

    await w.find('[data-testid="draft-category-clear"]').trigger('click')
    expect((w.find('#draft-category').element as HTMLInputElement).value).toBe('')
    // The control goes away with nothing to clear.
    expect(w.find('[data-testid="draft-category-clear"]').exists()).toBe(false)
  })

  it('loads the draft and fills the form', async () => {
    mockApi()
    const w = mountEditor()
    await flushPromises()
    expect((w.find('input[type="text"]').element as HTMLInputElement).value).toBe('My draft')
    expect(w.find('textarea').element).toBeTruthy()
    expect(apiFetch).toHaveBeenCalledWith('/drafts/d1')
  })

  it('shows the read-only banner without fetching a draft', async () => {
    mockApi()
    const w = mountEditor({ readonly: true, initialContent: draftPayload })
    await flushPromises()
    expect(w.text()).toContain('In review · read only')
    expect(apiFetch).not.toHaveBeenCalledWith('/drafts/d1')
  })

  it('emits close when Close is clicked', async () => {
    mockApi()
    const w = mountEditor()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Close')!.trigger('click')
    expect(w.emitted('close')).toBeTruthy()
  })

  it('submits for review (save then submit) when valid', async () => {
    mockApi()
    const w = mountEditor()
    await flushPromises()
    const submit = w.findAll('button').find(b => b.text()?.includes('Submit for review'))!
    expect(submit.attributes('disabled')).toBeUndefined()
    await submit.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/drafts/d1', expect.objectContaining({ method: 'PUT' }))
    expect(apiFetch).toHaveBeenCalledWith('/drafts/d1/submit', { method: 'POST' })
    expect(w.emitted('submitted')).toBeTruthy()
  })

  it('switches the draft type', async () => {
    mockApi()
    const w = mountEditor()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Blog post')!.trigger('click')
    expect(w.findAll('button').find(b => b.text() === 'Blog post')!.classes().join(' ')).toContain('bg-slypn-600')
  })

  it('shows a conflict banner on a 412 and resolves by discarding local', async () => {
    mockApi((url, method) => {
      if (url.startsWith('/drafts/') && method === 'PUT') return resp({}, { ok: false, status: 412 })
      return undefined
    })
    const w = mountEditor()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Submit for review'))!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('This draft was updated elsewhere')
    await w.findAll('button').find(b => b.text()?.includes('Discard mine'))!.trigger('click')
    expect(w.text()).not.toContain('This draft was updated elsewhere')
  })

  it('surfaces a submit error', async () => {
    mockApi((url, method) => {
      if (url.endsWith('/submit') && method === 'POST') return resp('nope', { ok: false, status: 500 })
      return undefined
    })
    const w = mountEditor()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Submit for review'))!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Submit failed')
  })
  it('says nothing-to-review gently when the revision matches what is published', async () => {
    // 409 is only reachable from the unchanged-revision guard on this endpoint, so it
    // is safe to treat as information rather than matching on message text.
    mockApi((url, method) => {
      if (url.endsWith('/submit') && method === 'POST') {
        return resp('This revision is identical to the published version, so there is nothing to review.', { ok: false, status: 409 })
      }
      return undefined
    })
    const w = mountEditor()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Submit for review'))!.trigger('click')
    await flushPromises()

    const notice = w.find('[data-testid="draft-submit-notice"]')
    expect(notice.exists()).toBe(true)
    expect(notice.text()).toContain('nothing to review')
    // Not dressed as a failure.
    expect(w.find('[data-testid="draft-submit-error"]').exists()).toBe(false)
    expect(w.text()).not.toContain('Submit failed')
  })

  it('clears the notice on the next submit attempt', async () => {
    let status = 409
    mockApi((url, method) => {
      if (url.endsWith('/submit') && method === 'POST') {
        return status === 409 ? resp('nothing to review', { ok: false, status: 409 }) : resp('{}', { ok: true, status: 201 })
      }
      return undefined
    })
    const w = mountEditor()
    await flushPromises()
    const submit = w.findAll('button').find(b => b.text()?.includes('Submit for review'))!
    await submit.trigger('click')
    await flushPromises()
    expect(w.find('[data-testid="draft-submit-notice"]').exists()).toBe(true)

    status = 201
    await submit.trigger('click')
    await flushPromises()
    expect(w.find('[data-testid="draft-submit-notice"]').exists()).toBe(false)
  })

  // ── Content type is fixed once an item is published ────────────────────────
  // A revision keeps the article's id and slug so the URL survives, but the type picks the
  // URL prefix. Offering the toggle invited a change the API refuses on save and on submit.

  it('shows the type as fixed on a revision draft, with no toggle', async () => {
    mockApi((url, method) =>
      url.startsWith('/drafts/') && method === 'GET'
        ? resp({ ...draftPayload, type: 'blog', replacesArticleId: 'a1' })
        : undefined)
    const w = mountEditor()
    await flushPromises()

    expect(w.find('[data-testid="draft-type-article"]').exists()).toBe(false)
    expect(w.find('[data-testid="draft-type-blog"]').exists()).toBe(false)
    // Still legible: the author can see what the item is, just not change it.
    expect(w.find('[data-testid="draft-type-locked"]').text()).toContain('Blog post')
  })

  it('still offers the toggle on a draft that replaces nothing', async () => {
    mockApi()
    const w = mountEditor()
    await flushPromises()

    expect(w.find('[data-testid="draft-type-article"]').exists()).toBe(true)
    expect(w.find('[data-testid="draft-type-blog"]').exists()).toBe(true)
    expect(w.find('[data-testid="draft-type-locked"]').exists()).toBe(false)
  })

  it('lets a plain draft still switch type', async () => {
    // The lock must not leak into ordinary drafts — miscategorisation stays fixable until
    // the item is published.
    mockApi()
    const w = mountEditor()
    await flushPromises()

    await w.find('[data-testid="draft-type-blog"]').trigger('click')
    expect(w.find('[data-testid="draft-type-blog"]').classes().join(' ')).toContain('bg-slypn-600')
  })

  // ── Autosave writes only what changed ──────────────────────────────────────
  // The debounce watcher fires on any change to the draft object, which is not the same as
  // the content differing from what is stored. Loading a draft replaces the object wholesale,
  // so opening a second draft used to PUT the freshly loaded content straight back.

  const puts = () => apiFetch.mock.calls.filter(c => c[1]?.method === 'PUT')

  it('does not save after switching drafts with no typing', async () => {
    vi.useFakeTimers()
    mockApi()
    const w = mountEditor()
    await flushPromises()
    await w.setProps({ draftId: 'd2' })
    await flushPromises()
    apiFetch.mockClear()

    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()

    expect(puts()).toHaveLength(0)
    vi.useRealTimers()
  })

  it('does not save a change that was undone inside the debounce window', async () => {
    vi.useFakeTimers()
    mockApi()
    const w = mountEditor()
    await flushPromises()
    apiFetch.mockClear()

    await w.find('#draft-title').setValue('My draft edited')
    await vi.advanceTimersByTimeAsync(500)      // still within the 1.5s
    await w.find('#draft-title').setValue('My draft')  // back to what was loaded
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()

    expect(puts()).toHaveLength(0)
    vi.useRealTimers()
  })

  it('still saves a real change', async () => {
    vi.useFakeTimers()
    mockApi()
    const w = mountEditor()
    await flushPromises()
    apiFetch.mockClear()

    await w.find('#draft-title').setValue('Genuinely different')
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()

    expect(puts()).toHaveLength(1)
    vi.useRealTimers()
  })

  it('saves an undo that comes after a save, rather than leaving the server ahead', async () => {
    // The trap in comparing against what was loaded: once an edit is written, going back to
    // the original is itself a change the server has not seen. The snapshot moves on every
    // successful save so this stays dirty.
    vi.useFakeTimers()
    mockApi()
    const w = mountEditor()
    await flushPromises()
    apiFetch.mockClear()

    await w.find('#draft-title').setValue('Edited')
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()
    expect(puts()).toHaveLength(1)

    await w.find('#draft-title').setValue('My draft')  // back to the originally loaded title
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()

    expect(puts()).toHaveLength(2)   // the undo is persisted too
    vi.useRealTimers()
  })
})
