import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from './index'

type ScrollFn = (to: { hash: string }, from: unknown, saved: unknown) => unknown

beforeEach(() => setActivePinia(createPinia()))

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
