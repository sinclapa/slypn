import { describe, it, expect, beforeEach } from 'vitest'
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
