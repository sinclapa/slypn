import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

const route = { params: {} as Record<string, string>, query: {} as Record<string, string>, hash: '', fullPath: '/missing' }
const router = { push: vi.fn(), replace: vi.fn(), back: vi.fn() }
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => route, useRouter: () => router }
})

import EventsView from './EventsView.vue'
import EventsPreviousView from './EventsPreviousView.vue'
import EventDetailView from './EventDetailView.vue'
import ArticleDetailView from './ArticleDetailView.vue'
import DashboardView from './DashboardView.vue'
import LoginView from './LoginView.vue'
import NotFoundView from './NotFoundView.vue'
import AuthCallbackView from './AuthCallbackView.vue'
import { useAuthStore } from '@/stores/auth'

const stubs = { RouterLink: RouterLinkStub, EventCalendar: { template: '<div class="event-calendar-stub" />' } }
let pinia: Pinia
const mountView = (C: unknown) => mount(C as never, { global: { plugins: [pinia], stubs } })

const ev = (over = {}) => ({
  id: 'e1', title: 'Coffee morning', type: 'Coffee meet-up',
  startsAt: '2999-06-01T10:00:00Z', endsAt: '2999-06-01T12:00:00Z',
  location: 'Brixton', description: 'Come along', ...over,
})

function jsonResponse(body: unknown, init: { status?: number; ok?: boolean } = {}) {
  return { status: init.status ?? 200, ok: init.ok ?? true, statusText: 'OK', json: () => Promise.resolve(body) } as unknown as Response
}

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset()
  apiFetch.mockReset()
  Object.assign(route, { params: {}, query: {}, hash: '', fullPath: '/missing' })
  router.push.mockClear(); router.replace.mockClear(); router.back.mockClear()
})

describe('EventsView', () => {
  it('renders upcoming events and toggles to the calendar', async () => {
    apiJson.mockResolvedValue([ev()])
    const w = mountView(EventsView)
    await flushPromises()
    expect(w.text()).toContain('Coffee morning')

    const calBtn = w.findAll('button').find(b => b.text() === 'Calendar')!
    await calBtn.trigger('click')
    expect(w.find('.event-calendar-stub').exists()).toBe(true)
  })

  it('shows the error state', async () => {
    apiJson.mockRejectedValue(new Error('down'))
    const w = mountView(EventsView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load events')
  })
})

describe('EventsPreviousView', () => {
  it('lists past events', async () => {
    apiJson.mockResolvedValue([ev({ id: 'old', title: 'Old social', startsAt: '2020-01-01T10:00:00Z', endsAt: '2020-01-01T11:00:00Z' })])
    const w = mountView(EventsPreviousView)
    await flushPromises()
    expect(w.text()).toContain('Old social')
  })

  it('shows the empty state when there are no past events', async () => {
    apiJson.mockResolvedValue([ev()])
    const w = mountView(EventsPreviousView)
    await flushPromises()
    expect(w.text()).toContain('No previous events found')
  })
})

describe('EventDetailView', () => {
  it('renders the event and goes back on click', async () => {
    route.params = { id: 'e1' }
    apiJson.mockResolvedValue(ev())
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('Coffee morning')
    expect(w.text()).toContain('Brixton')
    await w.find('button').trigger('click')
    expect(router.push).toHaveBeenCalledWith('/events')
  })

  it('shows an error message on failure', async () => {
    route.params = { id: 'e1' }
    apiJson.mockRejectedValue(new Error('gone'))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load event')
  })
})

describe('ArticleDetailView', () => {
  const art = { id: 'a1', slug: 's', title: 'Deep dive', summary: 'sum', body: '<p>hi</p>', author: 'Jo', publishedAt: '2026-05-01T00:00:00Z', readingMinutes: 5, category: 'Community', tags: ['t'] }

  it('renders the fetched article', async () => {
    route.params = { slug: 's' }
    apiFetch.mockResolvedValue(jsonResponse(art))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    expect(w.text()).toContain('Deep dive')
    expect(w.html()).toContain('hi')
  })

  it('shows not-found when the article is 404', async () => {
    route.params = { slug: 'missing' }
    apiFetch.mockResolvedValue(jsonResponse(null, { status: 404, ok: false }))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    expect(w.text()).toContain('Article not found')
  })

  it('shows an error on non-404 failure', async () => {
    route.params = { slug: 's' }
    apiFetch.mockResolvedValue(jsonResponse(null, { status: 500, ok: false }))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load this article')
  })
})

describe('DashboardView', () => {
  it('greets the signed-in admin and shows admin tiles', async () => {
    apiFetch.mockResolvedValue(jsonResponse([]))
    const auth = useAuthStore()
    await auth.initialize()
    const w = mountView(DashboardView)
    await flushPromises()
    expect(w.text()).toContain('Welcome back, Test Admin')
    expect(w.text()).toContain('Admin')
    expect(w.text()).toContain('Approvals')
    expect(w.text()).toContain('Members')
  })
})

describe('LoginView', () => {
  it('offers sign-in and triggers login when configured', async () => {
    const w = mountView(LoginView)
    const auth = useAuthStore()
    expect(w.text()).toContain('Continue with Entra External ID')
    await w.find('button').trigger('click')
    await flushPromises()
    expect(auth.isAuthenticated).toBe(true)
  })

  it('shows the already-signed-in state', async () => {
    const auth = useAuthStore()
    await auth.initialize()
    const w = mountView(LoginView)
    expect(w.text()).toContain('already signed in')
  })
})

describe('NotFoundView', () => {
  it('shows the requested path', () => {
    route.fullPath = '/nope'
    const w = mountView(NotFoundView)
    expect(w.text()).toContain('/nope')
  })
})

describe('AuthCallbackView', () => {
  it('initialises auth and redirects home', async () => {
    const w = mountView(AuthCallbackView)
    await flushPromises()
    expect(w.text()).toContain('Signing you in')
    expect(router.replace).toHaveBeenCalledWith({ name: 'home' })
  })
})
