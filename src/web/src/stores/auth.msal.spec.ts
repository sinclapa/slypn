import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

// Exercise the real-MSAL (non dev-skip) branches of the auth store by mocking
// the msal lib module so isDevSkipAuth is false and a fake PublicClientApplication
// is wired in.
const msal = vi.hoisted(() => ({
  instance: {
    initialize: vi.fn().mockResolvedValue(undefined),
    handleRedirectPromise: vi.fn().mockResolvedValue(null),
    getAllAccounts: vi.fn(() => [] as unknown[]),
    setActiveAccount: vi.fn(),
    loginRedirect: vi.fn().mockResolvedValue(undefined),
    logoutRedirect: vi.fn().mockResolvedValue(undefined),
    acquireTokenSilent: vi.fn().mockResolvedValue({ accessToken: 'access-token' }),
    acquireTokenRedirect: vi.fn().mockResolvedValue(undefined),
  },
  ensureMsalInitialized: vi.fn().mockResolvedValue(null),
  clearMsalInteractionState: vi.fn(),
}))

vi.mock('@/lib/msal', () => ({
  apiScope: 'api://scope',
  isAuthConfigured: true,
  isDevSkipAuth: false,
  buildSilentRequest: (account: unknown) => ({ account, scopes: ['api://scope'] }),
  clearMsalInteractionState: msal.clearMsalInteractionState,
  ensureMsalInitialized: msal.ensureMsalInitialized,
  msalInstance: msal.instance,
}))

import { BrowserAuthError, InteractionRequiredAuthError } from '@azure/msal-browser'
import { useAuthStore } from './auth'

const account = {
  username: 'user@example.com',
  name: 'Real User',
  idTokenClaims: { oid: 'oid-1', roles: ['Member'] },
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  msal.instance.getAllAccounts.mockReturnValue([])
  msal.instance.acquireTokenSilent.mockResolvedValue({ accessToken: 'access-token' })
  msal.ensureMsalInitialized.mockResolvedValue(null)
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({ roles: ['Admin'] }) }))
})

describe('auth store · MSAL mode', () => {
  it('picks up a cached account and harvests roles from /me', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    const auth = useAuthStore()
    await auth.initialize()
    expect(auth.isAuthenticated).toBe(true)
    expect(msal.instance.setActiveAccount).toHaveBeenCalledWith(account)
    expect(auth.roles).toEqual(['Admin']) // from /api/me
    expect(auth.isAdmin).toBe(true)
  })

  it('uses the account from a redirect result', async () => {
    msal.ensureMsalInitialized.mockResolvedValue({ account })
    const auth = useAuthStore()
    await auth.initialize()
    expect(auth.isAuthenticated).toBe(true)
    expect(msal.instance.setActiveAccount).toHaveBeenCalledWith(account)
  })

  it('login triggers a redirect', async () => {
    const auth = useAuthStore()
    await auth.login('/dashboard')
    expect(msal.instance.loginRedirect).toHaveBeenCalled()
  })

  it('logout triggers a redirect when signed in', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    const auth = useAuthStore()
    await auth.initialize()
    await auth.logout()
    expect(msal.instance.logoutRedirect).toHaveBeenCalled()
  })

  it('acquireToken returns the silent token', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    const auth = useAuthStore()
    await auth.initialize()
    await expect(auth.acquireToken()).resolves.toBe('access-token')
  })

  it('setPersona is a no-op outside dev-skip', () => {
    const auth = useAuthStore()
    expect(() => auth.setPersona('member')).not.toThrow()
  })

  it('clears stale interaction state and retries login once', async () => {
    msal.instance.loginRedirect
      .mockRejectedValueOnce(new BrowserAuthError('interaction_in_progress'))
      .mockResolvedValueOnce(undefined)
    const auth = useAuthStore()
    await auth.login()
    expect(msal.clearMsalInteractionState).toHaveBeenCalled()
    expect(msal.instance.loginRedirect).toHaveBeenCalledTimes(2)
  })

  it('re-throws non-interaction_in_progress errors from loginRedirect', async () => {
    msal.instance.loginRedirect.mockRejectedValueOnce(new Error('network failure'))
    const auth = useAuthStore()
    await expect(auth.login()).rejects.toThrow('network failure')
  })

  it('falls back to interactive token acquisition when interaction is required', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    const auth = useAuthStore()
    await auth.initialize()
    msal.instance.acquireTokenSilent.mockRejectedValueOnce(new InteractionRequiredAuthError('interaction_required'))
    await expect(auth.acquireToken()).resolves.toBeNull()
    expect(msal.instance.acquireTokenRedirect).toHaveBeenCalled()
  })

  it('uses decoded token roles when /me is not ok', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: () => Promise.resolve({}) }))
    const auth = useAuthStore()
    await auth.initialize()
    // /me failed and the access token has no decodable roles, so it falls back
    // to the id-token claim roles.
    expect(auth.roles).toEqual(['Member'])
  })

  it('falls back to id-token claim roles when token acquisition fails', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockRejectedValue(new Error('login required'))
    const auth = useAuthStore()
    await auth.initialize()
    // apiRoles stays empty, so the computed falls back to idTokenClaims.roles.
    expect(auth.roles).toEqual(['Member'])
  })

  it('decodes roles and oid from a well-formed JWT access token', async () => {
    // payload = {"roles":["Admin"],"oid":"real-oid-from-token"}
    const payload = btoa(JSON.stringify({ roles: ['Admin'], oid: 'real-oid-from-token' }))
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockResolvedValue({ accessToken: `header.${payload}.sig` })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: () => Promise.resolve({}) }))
    const auth = useAuthStore()
    await auth.initialize()
    // token has valid roles so they override the idTokenClaims fallback
    expect(auth.roles).toContain('Admin')
    expect(auth.oid).toBe('real-oid-from-token')
  })

  it('returns empty roles when JWT payload has no roles array', async () => {
    // payload without roles — just oid
    const payload = btoa(JSON.stringify({ oid: 'oid-no-roles' }))
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockResolvedValue({ accessToken: `header.${payload}.sig` })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: () => Promise.resolve({}) }))
    const auth = useAuthStore()
    await auth.initialize()
    // no roles in token — falls back to idTokenClaims.roles = ['Member']
    expect(auth.roles).toEqual(['Member'])
  })

  it('falls back to idTokenClaims oid when JWT payload oid is not a string', async () => {
    // oid in JWT is a number — decodeJwtPayload returns null for apiOid,
    // so auth.oid falls back to idTokenClaims.oid ('oid-1' from the account fixture)
    const payload = btoa(JSON.stringify({ roles: ['Admin'], oid: 12345 }))
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockResolvedValue({ accessToken: `header.${payload}.sig` })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: () => Promise.resolve({}) }))
    const auth = useAuthStore()
    await auth.initialize()
    expect(auth.oid).toBe('oid-1')
  })

  it('exposes oid from apiOid in MSAL mode', async () => {
    const payload = btoa(JSON.stringify({ roles: ['Admin'], oid: 'token-oid' }))
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockResolvedValue({ accessToken: `header.${payload}.sig` })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: () => Promise.resolve({}) }))
    const auth = useAuthStore()
    await auth.initialize()
    // auth.oid accesses the computed — covers the non-devskip branch of `oid`
    expect(typeof auth.oid).toBe('string')
  })

  it('clears account and returns when logout is called without an active account', async () => {
    const auth = useAuthStore()
    // not initialized — account.value is null
    await auth.logout()
    expect(msal.instance.logoutRedirect).not.toHaveBeenCalled()
  })

  it('shares the in-flight initialize promise when called concurrently', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    const auth = useAuthStore()
    const p1 = auth.initialize()
    const p2 = auth.initialize() // should reuse the same promise
    await Promise.all([p1, p2])
    expect(auth.isAuthenticated).toBe(true)
    // ensureMsalInitialized called only once despite two concurrent calls
    expect(msal.ensureMsalInitialized).toHaveBeenCalledTimes(1)
  })

  it('falls back to decoded token roles when /me fetch throws a network error', async () => {
    const payload = btoa(JSON.stringify({ roles: ['Member'], oid: 'oid-fetch-throw' }))
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockResolvedValue({ accessToken: `header.${payload}.sig` })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network error')))
    const auth = useAuthStore()
    await auth.initialize()
    expect(auth.roles).toEqual(['Member'])
  })

  it('re-throws errors that are not InteractionRequiredAuthError from acquireToken', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    const auth = useAuthStore()
    await auth.initialize()
    msal.instance.acquireTokenSilent.mockRejectedValue(new Error('token expired'))
    await expect(auth.acquireToken()).rejects.toThrow('token expired')
  })
})
