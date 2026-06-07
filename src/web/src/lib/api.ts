import { useAuthStore } from '@/stores/auth'

/**
 * Thin fetch wrapper for the SLYPN API.
 *
 * - Prepends `/api` so callers pass the relative path (e.g. `/articles`).
 * - Attaches `Authorization: Bearer <token>` when the user is signed in.
 * - Defaults Content-Type to application/json for requests with a body
 *   UNLESS the body is FormData (browsers set the multipart boundary).
 *
 * Public endpoints work unauthenticated; the bearer header is just absent.
 */
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const auth = useAuthStore()
  const headers = new Headers(init.headers)

  if (auth.isAuthenticated) {
    const token = await auth.acquireToken()
    if (token) {
      headers.set('Authorization', `Bearer ${token}`)
      // SWA's gateway intercepts the Authorization header and replaces it with
      // its own HS256 session token before the request reaches the Functions API.
      // X-Slypn-Token bypasses that interception — the middleware reads this first.
      headers.set('X-Slypn-Token', `Bearer ${token}`)
    }
  }
  if (init.body && !headers.has('Content-Type') && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  return fetch(`/api${path}`, { ...init, headers })
}

export async function apiJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const resp = await apiFetch(path, init)
  if (!resp.ok) {
    const body = await resp.text().catch(() => '')
    throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
  }
  return resp.json() as Promise<T>
}
