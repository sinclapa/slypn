import { describe, it, expect, beforeEach } from 'vitest'
import {
  isAuthConfigured,
  isDevSkipAuth,
  msalInstance,
  ensureMsalInitialized,
  buildSilentRequest,
  clearMsalInteractionState,
  apiScope,
} from './msal'
import type { AccountInfo } from '@azure/msal-browser'

// Test env runs in dev-skip mode (VITE_DEV_SKIP_AUTH=true). MSAL config presence
// depends on .env, so these assertions stay config-agnostic.
describe('msal config', () => {
  it('runs in dev-skip mode and exposes a boolean config flag', () => {
    expect(isDevSkipAuth).toBe(true)
    expect(typeof isAuthConfigured).toBe('boolean')
    // msalInstance is null iff MSAL isn't configured.
    expect(isAuthConfigured ? msalInstance !== null : msalInstance === null).toBe(true)
  })

  it('ensureMsalInitialized resolves to null when no instance exists', async () => {
    if (msalInstance === null) {
      await expect(ensureMsalInitialized()).resolves.toBeNull()
    } else {
      expect(typeof ensureMsalInitialized).toBe('function')
    }
  })

  it('buildSilentRequest pairs the account with the api scope', () => {
    const account = { username: 'a@b.com' } as AccountInfo
    expect(buildSilentRequest(account)).toEqual({ account, scopes: [apiScope] })
  })

  describe('clearMsalInteractionState', () => {
    beforeEach(() => sessionStorage.clear())

    it('removes stale interaction-status keys and leaves others', () => {
      sessionStorage.setItem('msal.account.interaction.status', 'in_progress')
      sessionStorage.setItem('msal.token', 'keep-me')
      clearMsalInteractionState()
      expect(sessionStorage.getItem('msal.account.interaction.status')).toBeNull()
      expect(sessionStorage.getItem('msal.token')).toBe('keep-me')
    })
  })
})
