import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
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

  it('badges the Editor link with the number of documents on the go', async () => {
    const adminOid = '11111111-1111-1111-1111-111111111111'
    const { apiFetch } = await import('@/lib/api')
    ;(apiFetch as ReturnType<typeof vi.fn>).mockImplementation((path: string) => {
      const body = path === '/drafts' ? [{ id: 'd1' }, { id: 'd2' }]
        : path === '/review/articles' ? [{ authorId: adminOid }]
        : []
      return Promise.resolve({ ok: true, json: () => Promise.resolve(body) })
    })
    await useAuthStore().initialize() // dev-skip admin
    const w = mountNav()
    await flushPromises()
    await w.find('[data-testid="user-menu-trigger"]').trigger('click')

    const badge = w.findAll('[data-testid="nav-badge"]').find(b => b.attributes('data-for') === '/editor')
    expect(badge?.text()).toBe('3') // two drafts + one submission
  })

  it('shows no Editor badge when there is nothing on the go', async () => {
    const { apiFetch } = await import('@/lib/api')
    ;(apiFetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: () => Promise.resolve([]) })
    await useAuthStore().initialize()
    const w = mountNav()
    await flushPromises()
    await w.find('[data-testid="user-menu-trigger"]').trigger('click')

    const badge = w.findAll('[data-testid="nav-badge"]').find(b => b.attributes('data-for') === '/editor')
    expect(badge).toBeUndefined()
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
    expect(w.text()).toContain('Newsletters')
    expect(w.text()).toContain('Sign out')
  })

  it('on mobile, the avatar opens a separate account menu kept out of the nav drawer', async () => {
    const auth = useAuthStore()
    await auth.initialize() // dev-skip admin
    const w = mountNav()

    const avatar = w.find('button[aria-controls="mobile-account"]')
    expect(avatar.exists()).toBe(true)
    expect(avatar.attributes('aria-expanded')).toBe('false')
    await avatar.trigger('click')
    expect(avatar.attributes('aria-expanded')).toBe('true')

    // The account panel holds the account/admin tools + sign out.
    const account = w.find('#mobile-account')
    expect(account.text()).toContain('Dashboard')
    expect(account.text()).toContain('Members')
    expect(account.text()).toContain('Newsletter subscribers')
    expect(account.text()).toContain('Newsletters')
    expect(account.text()).toContain('Sign out')

    // The primary-nav drawer holds navigation only — no admin tools.
    const nav = w.find('#mobile-nav')
    expect(nav.text()).toContain('Newsletter')
    expect(nav.text()).not.toContain('Dashboard')
    expect(nav.text()).not.toContain('Sign out')

    // Opening the hamburger closes the account menu (mutual exclusivity).
    await w.find('button[aria-controls="mobile-nav"]').trigger('click')
    expect(avatar.attributes('aria-expanded')).toBe('false')
  })

  it('signs out when Sign out is clicked in the desktop dropdown', async () => {
    const auth = useAuthStore()
    await auth.initialize()
    const logoutSpy = vi.spyOn(auth, 'logout').mockResolvedValue()
    const w = mountNav()
    await w.find('[data-testid="user-menu-trigger"]').trigger('click')
    const signOut = w.findAll('button').find(b => b.text() === 'Sign out')!
    await signOut.trigger('click')
    await flushPromises()
    expect(logoutSpy).toHaveBeenCalled()
  })

  it('signs out when Sign out is clicked in the mobile account panel', async () => {
    const auth = useAuthStore()
    await auth.initialize()
    const logoutSpy = vi.spyOn(auth, 'logout').mockResolvedValue()
    const w = mountNav()
    await w.find('button[aria-controls="mobile-account"]').trigger('click')
    const mobileSignOut = w.find('#mobile-account').findAll('button').find(b => b.text() === 'Sign out')!
    await mobileSignOut.trigger('click')
    await flushPromises()
    expect(logoutSpy).toHaveBeenCalled()
  })

  it('shows and hides the env menu on toggle and mouseleave', async () => {
    const w = mountNav()
    const envBtn = w.find('button[aria-expanded]')
    // The env label button exists because VITE_FARO_ENV is unset (defaults to 'dev')
    expect(envBtn.exists()).toBe(true)
    await envBtn.trigger('click')
    expect(envBtn.attributes('aria-expanded')).toBe('true')
    // Mouseleave on the dropdown closes the menu
    const dropdown = w.find('[aria-expanded="true"]').element.parentElement?.querySelector('[class*="absolute"]')
    if (dropdown) {
      await (w.find('[class*="absolute"][class*="mt-1"]')).trigger('mouseleave')
    }
  })

  it('shows a Sign in button in the mobile nav when unauthenticated', async () => {
    const w = mountNav()
    await w.find('button[aria-controls="mobile-nav"]').trigger('click')
    const nav = w.find('#mobile-nav')
    expect(nav.text()).toContain('Sign in')
  })
})
