import { describe, it, expect, beforeEach } from 'vitest'
import { useCookieConsent } from './useCookieConsent'

const STORAGE_KEY = 'slypn:cookie-consent'

describe('useCookieConsent', () => {
  beforeEach(() => localStorage.clear())

  it('accept() persists "accepted" and updates the choice ref', () => {
    const { choice, accept } = useCookieConsent()
    accept()
    expect(choice.value).toBe('accepted')
    expect(localStorage.getItem(STORAGE_KEY)).toBe('accepted')
  })

  it('decline() persists "declined"', () => {
    const { choice, decline } = useCookieConsent()
    decline()
    expect(choice.value).toBe('declined')
    expect(localStorage.getItem(STORAGE_KEY)).toBe('declined')
  })

  it('shares one reactive choice across callers (singleton)', () => {
    const a = useCookieConsent()
    const b = useCookieConsent()
    a.accept()
    expect(b.choice.value).toBe('accepted')
  })
})
