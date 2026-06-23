import { describe, it, expect } from 'vitest'
import { isFaroConfigured, setupFaro, getFaro } from './faro'

// No VITE_FARO_URL in the test env, so Faro is unconfigured and inert.
describe('faro (unconfigured)', () => {
  it('reports not configured', () => {
    expect(isFaroConfigured).toBe(false)
  })

  it('setupFaro is a no-op and never initialises an instance', () => {
    expect(() => setupFaro()).not.toThrow()
    expect(getFaro()).toBeNull()
  })
})
