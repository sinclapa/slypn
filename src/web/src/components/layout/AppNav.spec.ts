import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const push = vi.fn()
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRouter: () => ({ push }) }
})
vi.mock('@/lib/api', () => ({
  apiFetch: vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve([]) }),
}))

import AppNav from './AppNav.vue'
import { useAuthStore } from '@/stores/auth'

let pinia: Pinia

function mountNav() {
  return mount(AppNav, {
    global: { plugins: [pinia], stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('AppNav', () => {
  beforeEach(() => {
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    push.mockClear()
  })

  it('renders the brand logo and primary nav links', () => {
    const w = mountNav()
    expect(w.find('img[alt="SLYPN"]').exists()).toBe(true)
    const tos = w.findAllComponents(RouterLinkStub).map(l => l.props('to'))
    expect(tos).toContain('/')
    expect(tos).toContain('/events')
    expect(tos).toContain('/newsletter')
  })

  it('shows a Sign in button when unauthenticated', () => {
    const w = mountNav()
    expect(w.text()).toContain('Sign in')
  })

  it('signs in via the auth store when Sign in is clicked', async () => {
    const w = mountNav()
    const auth = useAuthStore()
    await w.find('button').trigger('click') // first button is Sign in (desktop) — but find the right one
    const signIn = w.findAll('button').find(b => b.text() === 'Sign in')
    if (signIn) await signIn.trigger('click')
    expect(auth.isAuthenticated).toBe(true)
  })

  it('toggles the mobile menu', async () => {
    const w = mountNav()
    const toggle = w.find('button[aria-controls="mobile-nav"]')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
  })

  it('shows the user menu with admin links when signed in as admin', async () => {
    const auth = useAuthStore()
    await auth.initialize() // dev-skip admin
    const w = mountNav()
    const trigger = w.find('[data-testid="user-menu-trigger"]')
    expect(trigger.exists()).toBe(true)
    expect(trigger.text()).toContain('Test Admin')

    await trigger.trigger('click')
    expect(w.text()).toContain('Dashboard')
    expect(w.text()).toContain('Approvals')
    expect(w.text()).toContain('Sign out')
  })
})
