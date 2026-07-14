import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { setActivePersonaKey } from '@/lib/devPersonas'

const { mockSetView } = vi.hoisted(() => ({ mockSetView: vi.fn() }))
vi.mock('@/lib/faro', () => ({
  getFaro: () => ({ api: { setView: mockSetView } }),
  setupFaro: vi.fn(),
  isFaroConfigured: false,
}))

import router from './index'

type ScrollFn = (to: { hash: string }, from: unknown, saved: unknown) => unknown

beforeEach(() => {
  setActivePinia(createPinia())
  vi.restoreAllMocks()
  mockSetView.mockClear()
})

afterEach(() => {
  setActivePersonaKey('admin')
})

describe('router scrollBehavior', () => {
  const scroll = router.options.scrollBehavior as unknown as ScrollFn

  it('returns the saved position when navigating back/forward', () => {
    const saved = { left: 0, top: 250 }
    expect(scroll({ hash: '' }, {}, saved)).toBe(saved)
  })

  it('scrolls to a hash target with a header offset', () => {
    expect(scroll({ hash: '#post-1' }, {}, null)).toEqual({ el: '#post-1', top: 96, behavior: 'smooth' })
  })

  it('scrolls to top by default', () => {
    expect(scroll({ hash: '' }, {}, null)).toEqual({ top: 0 })
  })
})

describe('router auth guard (dev-skip admin)', () => {
  it('allows a public route', async () => {
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('home')
  })

  it('allows an auth-gated route when signed in', async () => {
    await router.push('/dashboard')
    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('allows a role-gated route for the admin persona', async () => {
    await router.push('/admin/members')
    expect(router.currentRoute.value.name).toBe('admin-members')
  })
})

describe('router lazy routes', () => {
  it('resolves /events/previous as events-previous', async () => {
    await router.push('/events/previous')
    expect(router.currentRoute.value.name).toBe('events-previous')
  })

  it('resolves /events/:id as event-detail', async () => {
    await router.push('/events/e1')
    expect(router.currentRoute.value.name).toBe('event-detail')
  })

  it('resolves /admin/content as admin-content', async () => {
    await router.push('/admin/content')
    expect(router.currentRoute.value.name).toBe('admin-content')
  })

  it('resolves /admin/resources as admin-resources', async () => {
    await router.push('/admin/resources')
    expect(router.currentRoute.value.name).toBe('admin-resources')
  })

  it('resolves /admin/approvals as admin-approvals', async () => {
    await router.push('/admin/approvals')
    expect(router.currentRoute.value.name).toBe('admin-approvals')
  })
})

describe('router auth guard edge cases', () => {
  it('redirects to login when the user is not authenticated', async () => {
    const auth = useAuthStore()
    vi.spyOn(auth, 'initialize').mockResolvedValue(undefined)
    await router.push('/editor')
    expect(router.currentRoute.value.name).toBe('login')
  })

  it('redirects home when the user lacks the required role', async () => {
    setActivePersonaKey('member')
    await router.push('/admin/members')
    expect(router.currentRoute.value.name).toBe('home')
  })
})

describe('router afterEach — faro view tracking', () => {
  it('calls faro setView with the route name after navigation', async () => {
    await router.push('/events')
    expect(mockSetView).toHaveBeenCalledWith({ name: 'events' })
  })
})
