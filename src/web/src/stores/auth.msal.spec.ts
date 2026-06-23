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

  it('falls back to id-token claim roles when token acquisition fails', async () => {
    msal.instance.getAllAccounts.mockReturnValue([account])
    msal.instance.acquireTokenSilent.mockRejectedValue(new Error('login required'))
    const auth = useAuthStore()
    await auth.initialize()
    // apiRoles stays empty, so the computed falls back to idTokenClaims.roles.
    expect(auth.roles).toEqual(['Member'])
  })
})
