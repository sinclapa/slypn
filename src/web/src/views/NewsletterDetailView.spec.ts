import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

const { renderAsync } = vi.hoisted(() => ({ renderAsync: vi.fn().mockResolvedValue(undefined) }))
vi.mock('docx-preview', () => ({ renderAsync }))

const route = { params: {} as Record<string, string>, query: {}, hash: '', fullPath: '/newsletter/n1' }
const router = {
  push: vi.fn(), replace: vi.fn(), back: vi.fn(),
  options: { history: { state: { back: undefined as string | undefined } } },
}
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => route, useRouter: () => router }
})

import NewsletterDetailView from './NewsletterDetailView.vue'

const stubs = { RouterLink: RouterLinkStub }
const mountView = () => mount(NewsletterDetailView, { global: { stubs } })

const newsletter = (over: Record<string, unknown> = {}) => ({
  id: 'n1', title: 'May 2026 issue', issueDate: '2026-05-01', summary: 'What happened in May',
  topics: ['Meet-ups'], fileName: 'SLYPN-Newsletter-2026-05.docx', ...over,
})

const PDF = 'application/pdf'
const DOCX = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
const DOC = 'application/msword'

function fileResponse(contentType: string, init: { ok?: boolean; status?: number; statusText?: string } = {}) {
  return {
    ok: init.ok ?? true,
    status: init.status ?? 200,
    statusText: init.statusText ?? 'OK',
    headers: { get: (name: string) => (name.toLowerCase() === 'content-type' ? contentType : null) },
    blob: () => Promise.resolve(new Blob(['dummy'], { type: contentType })),
    arrayBuffer: () => Promise.resolve(new ArrayBuffer(8)),
  } as unknown as Response
}

beforeEach(() => {
  vi.restoreAllMocks()
  apiJson.mockReset()
  apiFetch.mockReset()
  renderAsync.mockClear().mockResolvedValue(undefined)
  Object.assign(route, { params: { id: 'n1' }, fullPath: '/newsletter/n1' })
  router.push.mockClear(); router.replace.mockClear(); router.back.mockClear()
  router.options.history.state.back = undefined
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
})

describe('NewsletterDetailView', () => {
  it('shows a loading state before the metadata resolves', async () => {
    apiJson.mockReturnValue(new Promise(() => {})) // never resolves
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('Loading')
  })

  it('shows an error message when the metadata fetch fails', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load this issue');
    expect(w.text()).toContain('boom')
  })

  it('shows a not-found message when no newsletter matches the id', async () => {
    apiJson.mockResolvedValue([newsletter({ id: 'other' })])
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('Newsletter not found')
  })

  it('shows a placeholder and skips the file fetch when no file is attached', async () => {
    apiJson.mockResolvedValue([newsletter({ fileName: undefined })])
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('No file has been attached')
    expect(apiFetch).not.toHaveBeenCalled()
  })

  it('renders a PDF in an iframe using an object URL', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValue(fileResponse(PDF))
    const w = mountView()
    await flushPromises()
    const iframe = w.get('iframe')
    expect(iframe.attributes('src')).toBe('blob:mock-url')
    expect(iframe.attributes('title')).toContain('May 2026 issue')
    expect(URL.createObjectURL).toHaveBeenCalled()
  })

  it('renders a DOCX via docx-preview', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValue(fileResponse(DOCX))
    const w = mountView()
    await flushPromises()
    expect(renderAsync).toHaveBeenCalledTimes(1)
    const [, bodyContainer, styleContainer, options] = renderAsync.mock.calls[0]
    expect(bodyContainer).toBe(styleContainer)
    expect(w.element.contains(bodyContainer)).toBe(true)
    expect(options).toMatchObject({ useBase64URL: true })
  })

  it('falls back to a download prompt for legacy .doc files', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValue(fileResponse(DOC))
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('can’t be previewed')
    expect(w.find('a[download]').exists()).toBe(true)
    expect(renderAsync).not.toHaveBeenCalled()
  })

  it('shows an error and a download link when the file fetch fails', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValue(fileResponse(PDF, { ok: false, status: 500, statusText: 'Server Error' }))
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load the preview')
    expect(w.find('a[download]').exists()).toBe(true)
  })

  it('revokes the object URL on unmount', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValue(fileResponse(PDF))
    const w = mountView()
    await flushPromises()
    w.unmount()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')
  })

  it('goes back to /newsletter by default', async () => {
    apiJson.mockResolvedValue([newsletter({ fileName: undefined })])
    const w = mountView()
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.push).toHaveBeenCalledWith('/newsletter')
    expect(router.back).not.toHaveBeenCalled()
  })

  it('uses router.back() when the back state is /newsletter', async () => {
    router.options.history.state.back = '/newsletter'
    apiJson.mockResolvedValue([newsletter({ fileName: undefined })])
    const w = mountView()
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.back).toHaveBeenCalled()
    expect(router.push).not.toHaveBeenCalled()
  })

  it('uses router.back() when the back state is /admin/newsletters', async () => {
    router.options.history.state.back = '/admin/newsletters'
    apiJson.mockResolvedValue([newsletter({ fileName: undefined })])
    const w = mountView()
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.back).toHaveBeenCalled()
    expect(router.push).not.toHaveBeenCalled()
  })
})
