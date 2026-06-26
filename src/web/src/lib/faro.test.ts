import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest'
import type { Faro } from '@grafana/faro-web-sdk'

// Re-import the module with VITE_FARO_URL stubbed to empty so the test is
// independent of whatever is in the local .env.local file.
describe('faro (unconfigured)', () => {
  let isFaroConfigured: boolean
  let setupFaro: () => void
  let getFaro: () => Faro | null

  beforeAll(async () => {
    vi.stubEnv('VITE_FARO_URL', '')
    vi.resetModules()
    const mod = await import('./faro')
    isFaroConfigured = mod.isFaroConfigured
    setupFaro = mod.setupFaro
    getFaro = mod.getFaro
  })

  afterAll(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('reports not configured', () => {
    expect(isFaroConfigured).toBe(false)
  })

  it('setupFaro is a no-op and never initialises an instance', () => {
    expect(() => setupFaro()).not.toThrow()
    expect(getFaro()).toBeNull()
  })
})
