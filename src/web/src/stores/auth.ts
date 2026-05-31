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
  msalInstance,
} from '@/lib/msal'

type IdTokenClaims = Record<string, unknown> & { roles?: string[] }

export const useAuthStore = defineStore('auth', () => {
  const account = ref<AccountInfo | null>(null)
  const initializing = ref(false)
  const initialized = ref(false)

  const isAuthenticated = computed(() => account.value !== null)
  const isConfigured = computed(() => isAuthConfigured)

  const roles = computed<string[]>(() => {
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
   * already-cached account if any. Safe to call multiple times.
   */
  async function initialize() {
    if (initialized.value || initializing.value) return
    initializing.value = true
    try {
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
      initialized.value = true
    } finally {
      initializing.value = false
    }
  }

  async function login(returnTo?: string) {
    if (!msalInstance) {
      throw new Error('Auth not configured — set VITE_MSAL_* env vars.')
    }
    await ensureMsalInitialized()
    await msalInstance.loginRedirect({
      scopes: [apiScope],
      redirectStartPage: returnTo ?? window.location.href,
    })
  }

  async function logout() {
    if (!msalInstance || !account.value) {
      account.value = null
      return
    }
    await msalInstance.logoutRedirect({ account: account.value })
  }

  /**
   * Returns a bearer access token for the SLYPN API, or null if the user
   * isn't signed in / MSAL isn't configured. Falls back to interactive
   * sign-in if silent acquisition fails for an interaction-required reason.
   */
  async function acquireToken(): Promise<string | null> {
    if (!msalInstance || !account.value) return null
    await ensureMsalInitialized()
    try {
      const result = await msalInstance.acquireTokenSilent(buildSilentRequest(account.value))
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
