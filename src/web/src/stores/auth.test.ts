import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from './auth'
import { DEV_PERSONA_STORAGE_KEY } from '@/lib/devPersonas'

// vitest.config.ts sets VITE_DEV_SKIP_AUTH=true, so the store runs in dev-skip
// mode and signs in as the active persona without MSAL.
describe('auth store · dev-skip personas', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('signs in as the admin persona by default', async () => {
    const auth = useAuthStore()
    await auth.initialize()

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.roles).toEqual(['Admin'])
    expect(auth.isAdmin).toBe(true)
    expect(auth.isContributor).toBe(false)
    expect(auth.isMember).toBe(false)
    expect(auth.oid).toBe('11111111-1111-1111-1111-111111111111')
  })

  it('honours the stored persona (member has a single role)', async () => {
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, 'member')
    const auth = useAuthStore()
    await auth.initialize()

    expect(auth.roles).toEqual(['Member'])
    expect(auth.isAdmin).toBe(false)
    expect(auth.isMember).toBe(true)
    expect(auth.displayName).toBe('Test Member')
  })
})

describe('auth store · getters and actions (dev-skip)', () => {
  let origLocation: Location

  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    origLocation = window.location
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { href: 'http://localhost/', origin: 'http://localhost', reload: vi.fn() },
    })
  })
  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: origLocation })
  })

  it('isConfigured is true in dev-skip mode', () => {
    expect(useAuthStore().isConfigured).toBe(true)
  })

  it('displayName falls back to "Member" before sign-in', () => {
    const auth = useAuthStore()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.displayName).toBe('Member')
  })

  it('oid resolves to the active persona oid', () => {
    const auth = useAuthStore()
    expect(auth.oid).toBe('11111111-1111-1111-1111-111111111111')
  })

  it('acquireToken returns null in dev-skip mode', async () => {
    const auth = useAuthStore()
    await auth.initialize()
    await expect(auth.acquireToken()).resolves.toBeNull()
  })

  it('login signs in as the dev account', async () => {
    const auth = useAuthStore()
    await auth.login()
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.displayName).toBe('Test Admin')
  })

  it('logout clears the account', async () => {
    const auth = useAuthStore()
    await auth.initialize()
    expect(auth.isAuthenticated).toBe(true)
    await auth.logout()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.roles).toEqual([])
  })

  it('setPersona persists the key and reloads', () => {
    const auth = useAuthStore()
    auth.setPersona('contributor')
    expect(localStorage.getItem(DEV_PERSONA_STORAGE_KEY)).toBe('contributor')
    expect(window.location.reload).toHaveBeenCalled()
  })

  it('initialize is idempotent', async () => {
    const auth = useAuthStore()
    await auth.initialize()
    await auth.initialize()
    expect(auth.initialized).toBe(true)
  })
})
