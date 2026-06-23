import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'

const choice = ref<'accepted' | 'declined' | null>(null)
const accept = vi.fn(() => { choice.value = 'accepted' })
const decline = vi.fn(() => { choice.value = 'declined' })

vi.mock('@/composables/useCookieConsent', () => ({
  useCookieConsent: () => ({ choice, accept, decline }),
}))

import CookieBanner from './CookieBanner.vue'

function mountBanner() {
  return mount(CookieBanner, { global: { stubs: { teleport: true } } })
}

describe('CookieBanner', () => {
  beforeEach(() => {
    choice.value = null
    accept.mockClear()
    decline.mockClear()
  })

  it('shows the dialog while no choice has been made', () => {
    const w = mountBanner()
    expect(w.find('[role="dialog"]').exists()).toBe(true)
    expect(w.text()).toContain('We’d like to use cookies')
  })

  it('calls accept when Accept all is clicked', async () => {
    const w = mountBanner()
    await w.findAll('button').find(b => b.text() === 'Accept all')!.trigger('click')
    expect(accept).toHaveBeenCalled()
  })

  it('calls decline when Decline is clicked', async () => {
    const w = mountBanner()
    await w.findAll('button').find(b => b.text() === 'Decline')!.trigger('click')
    expect(decline).toHaveBeenCalled()
  })

  it('hides the dialog once a choice exists', () => {
    choice.value = 'accepted'
    const w = mountBanner()
    expect(w.find('[role="dialog"]').exists()).toBe(false)
  })
})
