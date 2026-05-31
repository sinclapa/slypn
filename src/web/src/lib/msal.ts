import {
  PublicClientApplication,
  type AccountInfo,
  type AuthenticationResult,
  type Configuration,
  type SilentRequest,
} from '@azure/msal-browser'

const clientId  = import.meta.env.VITE_MSAL_CLIENT_ID ?? ''
const authority = import.meta.env.VITE_MSAL_AUTHORITY ?? ''
export const apiScope = import.meta.env.VITE_API_SCOPE ?? ''

/**
 * True when all three MSAL env vars are present. When false the app still
 * renders — Sign in is just shown as unavailable, useful for offline UI work
 * and for the period before the tenant is set up (#19/#20).
 */
export const isAuthConfigured = Boolean(clientId && authority && apiScope)

const msalConfig: Configuration = {
  auth: {
    clientId,
    authority,
    knownAuthorities: authority ? [new URL(authority).host] : [],
    redirectUri:          `${window.location.origin}/auth/callback`,
    postLogoutRedirectUri: window.location.origin,
    navigateToLoginRequestUrl: true,
  },
  cache: {
    cacheLocation: 'localStorage',
    storeAuthStateInCookie: false,
  },
}

export const msalInstance: PublicClientApplication | null = isAuthConfigured
  ? new PublicClientApplication(msalConfig)
  : null

let initPromise: Promise<AuthenticationResult | null> | null = null

/**
 * Idempotent initialise. Returns the redirect response (when the page is
 * resuming from /auth/callback) or null.
 */
export function ensureMsalInitialized(): Promise<AuthenticationResult | null> {
  if (!msalInstance) return Promise.resolve(null)
  if (!initPromise) {
    initPromise = msalInstance.initialize()
      .then(() => msalInstance!.handleRedirectPromise())
  }
  return initPromise
}

export function buildSilentRequest(account: AccountInfo): SilentRequest {
  return { account, scopes: [apiScope] }
}
