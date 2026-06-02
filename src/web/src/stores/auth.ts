import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  InteractionRequiredAuthError,
  type AccountInfo,
} from '@azure/msal-browser'
import {
  apiScope,
  buildSilentRequest,
  ensureMsalInitialized,
  isAuthConfigured,
  isDevSkipAuth,
  msalInstance,
} from '@/lib/msal'

type IdTokenClaims = Record<string, unknown> & { roles?: string[] }

/**
 * App roles live on the slypn-api app registration, so the `roles` claim only
 * appears in the access token issued for that audience — not in the SPA's ID
 * token. Decode the JWT payload (no signature check — verification happens at
 * the API) to pull roles out for UI gating.
 */
function decodeJwtRoles(token: string): string[] {
  try {
    const segment = token.split('.')[1]
    if (!segment) return []
    const padded = segment.replace(/-/g, '+').replace(/_/g, '/')
    const padding = padded.length % 4 === 0 ? '' : '='.repeat(4 - (padded.length % 4))
    const payload = JSON.parse(atob(padded + padding)) as { roles?: unknown }
    return Array.isArray(payload.roles)
      ? payload.roles.filter((r): r is string => typeof r === 'string')
      : []
  } catch {
    return []
  }
}

function makeDevAccount(): AccountInfo {
  return {
    homeAccountId:  'dev-skip-auth',
    environment:    'localhost',
    tenantId:       '00000000-0000-0000-0000-000000000000',
    username:       'dev@slypn.local',
    localAccountId: 'dev-skip-auth',
    name:           'Dev Admin',
    idTokenClaims: {
      name:  'Dev Admin',
      oid:   '00000000-0000-0000-0000-000000000000',
      roles: ['Admin', 'Contributor', 'Member'],
    } as Record<string, unknown>,
  } as AccountInfo
}

export const useAuthStore = defineStore('auth', () => {
  const account = ref<AccountInfo | null>(null)
  const apiRoles = ref<string[]>([])
  const initializing = ref(false)
  const initialized = ref(false)

  const isAuthenticated = computed(() => account.value !== null)
  /** UI is "configured" if either real Entra or the dev-skip flag is wired. */
  const isConfigured = computed(() => isAuthConfigured || isDevSkipAuth)

  const roles = computed<string[]>(() => {
    if (apiRoles.value.length > 0) return apiRoles.value
    // Dev-skip mode parks roles on the synthetic account's idTokenClaims since
    // there's no real access token to decode.
    const claims = account.value?.idTokenClaims as IdTokenClaims | undefined
    return Array.isArray(claims?.roles) ? claims!.roles! : []
  })
  const isAdmin       = computed(() => roles.value.includes('Admin'))
  const isContributor = computed(() => roles.value.includes('Contributor'))
  const isMember      = computed(() => roles.value.includes('Member'))

  const displayName = computed(() =>
    (account.value?.name ?? account.value?.username ?? '').trim() || 'Member')

  /**
   * Drive MSAL through initialize + handleRedirectPromise and pick up an
   * already-cached account if any. In dev-skip mode, synthesises an Admin
   * principal so route guards open up immediately. Safe to call multiple times.
   */
  async function initialize() {
    if (initialized.value || initializing.value) return
    initializing.value = true
    try {
      if (isDevSkipAuth) {
        account.value = makeDevAccount()
        initialized.value = true
        return
      }
      const redirectResult = await ensureMsalInitialized()
      if (redirectResult?.account) {
        msalInstance!.setActiveAccount(redirectResult.account)
        account.value = redirectResult.account
      } else if (msalInstance) {
        const cached = msalInstance.getAllAccounts()[0]
        if (cached) {
          msalInstance.setActiveAccount(cached)
          account.value = cached
        }
      }
      if (account.value) {
        await refreshApiRoles()
      }
      initialized.value = true
    } finally {
      initializing.value = false
    }
  }

  async function login(returnTo?: string) {
    if (isDevSkipAuth) {
      account.value = makeDevAccount()
      if (returnTo && returnTo !== window.location.href) {
        window.location.href = returnTo
      }
      return
    }
    if (!msalInstance) {
      throw new Error('Auth not configured — set VITE_MSAL_* env vars or VITE_DEV_SKIP_AUTH=true.')
    }
    await ensureMsalInitialized()
    await msalInstance.loginRedirect({
      scopes: [apiScope],
      redirectStartPage: returnTo ?? window.location.href,
    })
  }

  async function logout() {
    if (isDevSkipAuth) {
      account.value = null
      apiRoles.value = []
      window.location.href = window.location.origin
      return
    }
    if (!msalInstance || !account.value) {
      account.value = null
      apiRoles.value = []
      return
    }
    await msalInstance.logoutRedirect({ account: account.value })
  }

  /**
   * Silently fetch an access token for the SLYPN API and harvest its `roles`
   * claim. Failures (no cached account, interaction required, etc.) leave
   * apiRoles empty rather than throw — the UI just degrades to no-role view.
   */
  async function refreshApiRoles() {
    if (isDevSkipAuth) return
    if (!msalInstance || !account.value) {
      apiRoles.value = []
      return
    }
    try {
      const result = await msalInstance.acquireTokenSilent(buildSilentRequest(account.value))
      apiRoles.value = decodeJwtRoles(result.accessToken)
    } catch {
      apiRoles.value = []
    }
  }

  /**
   * Returns a bearer access token for the SLYPN API, or null if the user
   * isn't signed in / MSAL isn't configured. Falls back to interactive
   * sign-in if silent acquisition fails for an interaction-required reason.
   * In dev-skip mode returns null — the API ignores the missing token.
   */
  async function acquireToken(): Promise<string | null> {
    if (isDevSkipAuth) return null
    if (!msalInstance || !account.value) return null
    await ensureMsalInitialized()
    try {
      const result = await msalInstance.acquireTokenSilent(buildSilentRequest(account.value))
      apiRoles.value = decodeJwtRoles(result.accessToken)
      return result.accessToken
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        await msalInstance.acquireTokenRedirect({ scopes: [apiScope] })
        return null
      }
      throw err
    }
  }

  return {
    account,
    initialized,
    isAuthenticated,
    isConfigured,
    roles,
    isAdmin,
    isContributor,
    isMember,
    displayName,
    initialize,
    login,
    logout,
    acquireToken,
  }
})
