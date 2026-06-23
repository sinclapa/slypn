import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { apiFetch, apiJson } from './api'
import { useAuthStore } from '@/stores/auth'

// vitest.config.ts sets VITE_DEV_SKIP_AUTH=true, so apiFetch always attaches
// the X-Slypn-Dev-User persona header and never a real bearer token.

function mockResponse(body: unknown, init: { ok?: boolean; status?: number; statusText?: string } = {}) {
  return {
    ok: init.ok ?? true,
    status: init.status ?? 200,
    statusText: init.statusText ?? 'OK',
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(typeof body === 'string' ? body : JSON.stringify(body)),
  } as unknown as Response
}

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  fetchMock = vi.fn().mockResolvedValue(mockResponse({ ok: true }))
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => vi.unstubAllGlobals())

describe('apiFetch', () => {
  it('prefixes /api and sends the dev persona header', async () => {
    await apiFetch('/articles')
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/articles')
    const headers = init.headers as Headers
    expect(headers.get('X-Slypn-Dev-User')).toBe('admin')
    // Dev-skip issues no token, so no Authorization header.
    expect(headers.get('Authorization')).toBeNull()
  })

  it('defaults Content-Type to JSON when a body is present', async () => {
    await apiFetch('/articles', { method: 'POST', body: JSON.stringify({ a: 1 }) })
    const headers = fetchMock.mock.calls[0][1].headers as Headers
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it('does not set Content-Type for FormData bodies', async () => {
    const fd = new FormData()
    fd.append('f', 'x')
    await apiFetch('/upload', { method: 'POST', body: fd })
    const headers = fetchMock.mock.calls[0][1].headers as Headers
    expect(headers.get('Content-Type')).toBeNull()
  })

  it('asks the auth store for a token when authenticated', async () => {
    const auth = useAuthStore()
    await auth.initialize() // dev-skip signs in; acquireToken resolves null
    const spy = vi.spyOn(auth, 'acquireToken')
    await apiFetch('/me')
    expect(spy).toHaveBeenCalled()
  })
})

describe('apiJson', () => {
  it('returns the parsed JSON body on success', async () => {
    fetchMock.mockResolvedValueOnce(mockResponse({ id: '1', title: 'Hi' }))
    const data = await apiJson<{ id: string; title: string }>('/articles/1')
    expect(data).toEqual({ id: '1', title: 'Hi' })
  })

  it('throws with status and body text on failure', async () => {
    fetchMock.mockResolvedValueOnce(mockResponse('boom', { ok: false, status: 500, statusText: 'Server Error' }))
    await expect(apiJson('/articles')).rejects.toThrow(/500 Server Error — boom/)
  })
})
