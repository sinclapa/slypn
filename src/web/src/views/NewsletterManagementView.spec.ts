import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({
  apiJson,
  apiFetch,
  apiErrorMessage: async (resp: Response) => `${resp.status} ${resp.statusText}`,
}))

import NewsletterManagementView from './NewsletterManagementView.vue'
import { useAuthStore } from '@/stores/auth'

const stubs = { RouterLink: RouterLinkStub, teleport: true }
let pinia: Pinia
const mountC = () => mount(NewsletterManagementView, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

function fail(status = 400, statusText = 'Bad Request') {
  return { ok: false, status, statusText, json: () => Promise.resolve({}), text: () => Promise.resolve('') } as unknown as Response
}

const newsletter = (over = {}) => ({
  id: 'n1', title: 'May 2026', issueDate: '2026-05-01', summary: 'Summary text', topics: ['t'], _etag: 'e1', ...over,
})

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset()
  apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
  await useAuthStore().initialize()
})

describe('NewsletterManagementView', () => {
  it('lists newsletters', async () => {
    apiJson.mockResolvedValue([newsletter()])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('May 2026')
    expect(w.text()).toContain('Summary text')
  })

  it('shows a download link when a file is attached, and a placeholder when not', async () => {
    apiJson.mockResolvedValue([newsletter({ fileName: 'issue.pdf' }), newsletter({ id: 'n2', title: 'June 2026' })])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('issue.pdf')
    expect(w.text()).toContain('No file attached')
  })

  it('shows the empty state', async () => {
    apiJson.mockResolvedValue([])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('No newsletters yet')
  })

  it('shows a load error', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain("Couldn’t load newsletters")
  })

  it('creates a newsletter, then uploads its file as a second call', async () => {
    apiJson.mockResolvedValue([])
    apiFetch
      .mockResolvedValueOnce(ok(newsletter({ _etag: 'e2' })))
      .mockResolvedValueOnce(ok(newsletter({ _etag: 'e3', fileName: 'issue.pdf' })))
    const w = mountC()
    await flushPromises()

    await w.findAll('button').find(b => b.text().includes('Add newsletter'))!.trigger('click')
    await w.find('#newsletter-title').setValue('May 2026')
    await w.find('#newsletter-issue-date').setValue('2026-05-01')
    await w.find('#newsletter-summary').setValue('A long enough summary.')

    const file = new File(['bytes'], 'issue.pdf', { type: 'application/pdf' })
    const fileInput = w.find('#newsletter-file')
    Object.defineProperty(fileInput.element, 'files', { value: [file] })
    await fileInput.trigger('change')

    await w.find('form').trigger('submit')
    await flushPromises()

    expect(apiFetch).toHaveBeenNthCalledWith(1, '/newsletters', expect.objectContaining({ method: 'POST' }))
    expect(apiFetch).toHaveBeenNthCalledWith(2, '/newsletters/n1/file', expect.objectContaining({
      method: 'PUT',
      headers: { 'If-Match': 'e2' },
    }))
    const secondCallBody = apiFetch.mock.calls[1][1].body as FormData
    expect(secondCallBody.get('file')).toStrictEqual(file)
  })

  it('surfaces an error from the file upload call without swallowing it', async () => {
    apiJson.mockResolvedValue([])
    apiFetch
      .mockResolvedValueOnce(ok(newsletter({ _etag: 'e2' })))
      .mockResolvedValueOnce(fail(415, 'Unsupported Media Type'))
    const w = mountC()
    await flushPromises()

    await w.findAll('button').find(b => b.text().includes('Add newsletter'))!.trigger('click')
    await w.find('#newsletter-title').setValue('May 2026')
    await w.find('#newsletter-issue-date').setValue('2026-05-01')
    await w.find('#newsletter-summary').setValue('A long enough summary.')

    const file = new File(['bytes'], 'issue.exe', { type: 'application/x-msdownload' })
    const fileInput = w.find('#newsletter-file')
    Object.defineProperty(fileInput.element, 'files', { value: [file] })
    await fileInput.trigger('change')

    await w.find('form').trigger('submit')
    await flushPromises()

    expect(w.text()).toContain('415')
  })

  it('creates a newsletter without a file in a single call', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockResolvedValueOnce(ok(newsletter()))
    const w = mountC()
    await flushPromises()

    await w.findAll('button').find(b => b.text().includes('Add newsletter'))!.trigger('click')
    await w.find('#newsletter-title').setValue('May 2026')
    await w.find('#newsletter-issue-date').setValue('2026-05-01')
    await w.find('#newsletter-summary').setValue('A long enough summary.')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(apiFetch).toHaveBeenCalledTimes(1)
    expect(apiFetch).toHaveBeenCalledWith('/newsletters', expect.objectContaining({ method: 'POST' }))
  })

  it('replaces an existing newsletter via PUT with If-Match', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValueOnce(ok(newsletter({ title: 'May 2026 (updated)' })))
    const w = mountC()
    await flushPromises()

    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(apiFetch).toHaveBeenCalledWith('/newsletters/n1', expect.objectContaining({
      method: 'PUT',
      headers: { 'If-Match': 'e1' },
    }))
  })

  it('removes a newsletter after confirmation', async () => {
    apiJson.mockResolvedValue([newsletter()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/newsletters/n1', expect.objectContaining({
      method: 'DELETE',
      headers: { 'If-Match': 'e1' },
    }))
  })
})
