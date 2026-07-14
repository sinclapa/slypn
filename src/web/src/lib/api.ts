import { useAuthStore } from '@/stores/auth'
import { isDevSkipAuth } from '@/lib/msal'
import { getActivePersonaKey } from '@/lib/devPersonas'

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
  // Dev-skip mode issues no token; tell the API which test persona to assume so
  // its synthesised principal (roles, OID) matches the frontend.
  if (isDevSkipAuth) {
    headers.set('X-Slypn-Dev-User', getActivePersonaKey())
  }
  if (init.body && !headers.has('Content-Type') && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  return fetch(`/api${path}`, { ...init, headers })
}

/** Formats a failed Response as `"<status> <statusText> — <body>"` (body omitted if empty). */
export async function apiErrorMessage(resp: Response): Promise<string> {
  const body = await resp.text().catch(() => '')
  const suffix = body ? ` — ${body}` : ''
  return `${resp.status} ${resp.statusText}${suffix}`
}

export async function apiJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const resp = await apiFetch(path, init)
  if (!resp.ok) throw new Error(await apiErrorMessage(resp))
  return resp.json() as Promise<T>
}
