import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  BrowserAuthError,
  InteractionRequiredAuthError,
  type AccountInfo,
} from '@azure/msal-browser'
import {
  apiScope,
  buildSilentRequest,
  clearMsalInteractionState,
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
function decodeJwtPayload(token: string): { roles: string[]; oid: string | null } {
  try {
    const segment = token.split('.')[1]
    if (!segment) return { roles: [], oid: null }
    const padded  = segment.replace(/-/g, '+').replace(/_/g, '/')
    const padding = padded.length % 4 === 0 ? '' : '='.repeat(4 - (padded.length % 4))
    const payload = JSON.parse(atob(padded + padding)) as { roles?: unknown; oid?: unknown }
    return {
      roles: Array.isArray(payload.roles)
        ? payload.roles.filter((r): r is string => typeof r === 'string')
        : [],
      oid: typeof payload.oid === 'string' ? payload.oid : null,
    }
  } catch {
    return { roles: [], oid: null }
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
  const apiOid   = ref<string | null>(null)
  const initializing = ref(false)
  const initialized = ref(false)

  // Shared promise so concurrent callers (e.g. router guard + component mount)
  // all await the same in-flight initialization instead of getting an instant
  // no-op return that leaves isAuthenticated false.
  let _initPromise: Promise<void> | null = null

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

  // OID from the API access token — matches what the API stores in CreatedBy
  const oid = computed<string | null>(() => {
    if (isDevSkipAuth) return '00000000-0000-0000-0000-000000000000'
    return apiOid.value ?? ((account.value?.idTokenClaims as Record<string, unknown>)?.oid as string | undefined) ?? null
  })

  /**
   * Drive MSAL through initialize + handleRedirectPromise and pick up an
   * already-cached account if any. In dev-skip mode, synthesises an Admin
   * principal so route guards open up immediately. Safe to call multiple times.
   */
  async function initialize(): Promise<void> {
    if (initialized.value) return
    if (_initPromise) return _initPromise

    _initPromise = (async () => {
      initializing.value = true
      try {
        if (isDevSkipAuth) {
          account.value = makeDevAccount()
          initialized.value = true
          return
        }

        // handleRedirectPromise can throw (expired code, state mismatch, etc.).
        // On error, clear the stale interaction state and fall through to the
        // cached-account lookup so existing sessions still work.
        let redirectResult = null
        try {
          redirectResult = await ensureMsalInitialized()
        } catch {
          clearMsalInteractionState()
        }

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
        _initPromise = null
      }
    })()

    return _initPromise
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
    try {
      await msalInstance.loginRedirect({
        scopes: [apiScope],
        redirectStartPage: returnTo ?? window.location.href,
      })
    } catch (err) {
      if (err instanceof BrowserAuthError && err.errorCode === 'interaction_in_progress') {
        // Stale interaction state from an interrupted redirect — clear it and retry once.
        clearMsalInteractionState()
        await ensureMsalInitialized()
        await msalInstance.loginRedirect({
          scopes: [apiScope],
          redirectStartPage: returnTo ?? window.location.href,
        })
        return
      }
      throw err
    }
  }

  async function logout() {
    if (isDevSkipAuth) {
      account.value  = null
      apiRoles.value = []
      apiOid.value   = null
      window.location.href = window.location.origin
      return
    }
    if (!msalInstance || !account.value) {
      account.value  = null
      apiRoles.value = []
      apiOid.value   = null
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
      const result  = await msalInstance.acquireTokenSilent(buildSilentRequest(account.value))
      const decoded = decodeJwtPayload(result.accessToken)
      if (decoded.oid) apiOid.value = decoded.oid

      // Roles are managed in Cosmos, not in Entra app roles — call /me to link
      // the OID on first login and retrieve the member's actual role list.
      try {
        const meResp = await fetch('/api/me', {
          headers: {
            Authorization:  `Bearer ${result.accessToken}`,
            'X-Slypn-Token': `Bearer ${result.accessToken}`,
          },
        })
        if (meResp.ok) {
          const me = (await meResp.json()) as { roles?: unknown }
          apiRoles.value = Array.isArray(me.roles)
            ? (me.roles as unknown[]).filter((r): r is string => typeof r === 'string')
            : decoded.roles
        } else {
          apiRoles.value = decoded.roles
        }
      } catch {
        apiRoles.value = decoded.roles
      }
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
      const result  = await msalInstance.acquireTokenSilent(buildSilentRequest(account.value))
      const decoded = decodeJwtPayload(result.accessToken)
      if (decoded.oid) apiOid.value = decoded.oid
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
    oid,
    initialize,
    login,
    logout,
    acquireToken,
  }
})
