import { request, type APIRequestContext, type APIResponse } from '@playwright/test'
import type { DevPersonaKey } from '../../src/lib/devPersonas'
import { API_ORIGIN } from './backend'

/**
 * A direct client for the Functions API, used for test setup, teardown, and
 * for asserting server state after a UI action.
 *
 * It is a standalone APIRequestContext rather than `page.request` so setup
 * traffic never inherits page cookies or storage, and so the acting persona is
 * always explicit in the call site. Authentication is the same mechanism the
 * browser uses in dev-skip mode: the X-Slypn-Dev-User header, which
 * JwtMiddleware turns into a real principal with real role enforcement.
 *
 * Pass `persona: null` for a genuinely anonymous caller (public endpoints,
 * 401 assertions).
 */
export interface ApiClient {
  readonly persona: DevPersonaKey | null
  /** Escape hatch for calls the wrappers don't cover (multipart uploads). */
  raw: APIRequestContext
  /** Turn `/drafts/x` into the absolute-path form `raw` needs. */
  resolve(path: string): string
  get(path: string): Promise<APIResponse>
  post(path: string, data?: unknown): Promise<APIResponse>
  put(path: string, data?: unknown): Promise<APIResponse>
  patch(path: string, data?: unknown): Promise<APIResponse>
  del(path: string, headers?: Record<string, string>): Promise<APIResponse>
  /** GET and parse, failing the call if the status is not 2xx. */
  json<T>(path: string): Promise<T>
  dispose(): Promise<void>
}

export async function createApiClient(persona: DevPersonaKey | null): Promise<ApiClient> {
  const raw = await request.newContext({
    // Origin only. Playwright resolves request paths with URL semantics, so a
    // leading-slash path would discard a `/api` suffix on the base URL — the
    // prefix has to be added per call instead.
    baseURL: API_ORIGIN,
    extraHTTPHeaders: persona ? { 'X-Slypn-Dev-User': persona } : {},
  })

  const resolve = (path: string) => `/api${path.startsWith('/') ? path : `/${path}`}`

  const client: ApiClient = {
    persona,
    raw,
    resolve,
    get: (path) => raw.get(resolve(path), { failOnStatusCode: false }),
    post: (path, data) => raw.post(resolve(path), { data: data ?? {}, failOnStatusCode: false }),
    put: (path, data) => raw.put(resolve(path), { data: data ?? {}, failOnStatusCode: false }),
    patch: (path, data) => raw.patch(resolve(path), { data: data ?? {}, failOnStatusCode: false }),
    del: (path, headers) => raw.delete(resolve(path), { headers, failOnStatusCode: false }),
    async json<T>(path: string): Promise<T> {
      const resp = await raw.get(resolve(path), { failOnStatusCode: false })
      if (!resp.ok()) {
        throw new Error(`GET ${path} -> ${resp.status()} ${resp.statusText()}: ${await resp.text()}`)
      }
      return await resp.json() as T
    },
    dispose: () => raw.dispose(),
  }
  return client
}

/** Assert a 2xx and return the parsed body — for setup steps that must succeed. */
export async function expectOk<T>(resp: APIResponse, what: string): Promise<T> {
  if (!resp.ok()) {
    throw new Error(`${what} -> ${resp.status()} ${resp.statusText()}: ${await resp.text()}`)
  }
  return await resp.json() as T
}
