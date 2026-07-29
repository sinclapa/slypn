import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

const route = { params: {} as Record<string, string>, query: {} as Record<string, string>, hash: '', fullPath: '/missing' }
const router = {
  push: vi.fn(), replace: vi.fn(), back: vi.fn(),
  options: { history: { state: { back: undefined as string | undefined } } },
}
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => route, useRouter: () => router }
})

import { nextTick } from 'vue'
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
  router.options.history.state.back = undefined
})

afterEach(() => {
  vi.stubGlobal('IntersectionObserver', class {
    observe = vi.fn(); unobserve = vi.fn(); disconnect = vi.fn(); takeRecords = vi.fn(() => [])
    root = null; rootMargin = ''; thresholds = []
  })
})

describe('EventsView', () => {
  it('renders upcoming events and toggles to the calendar', async () => {
    apiJson.mockResolvedValue([ev(), ev({ id: 'e2', title: 'Typeless', type: '' })])
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

  it('filters events by type when a pill is clicked', async () => {
    apiJson.mockResolvedValue([
      ev({ id: 'e1', title: 'Coffee morning', type: 'Coffee meet-up' }),
      ev({ id: 'e2', title: 'Quiz night', type: 'Quiz' }),
    ])
    const w = mountView(EventsView)
    await flushPromises()
    const quizPill = w.findAll('button').find(b => b.text() === 'Quiz')!
    await quizPill.trigger('click')
    expect(w.text()).toContain('Quiz night')
    expect(w.text()).not.toContain('Coffee morning')
  })

  it('shows the previous-events link when there are old events', async () => {
    const oldDate = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString()
    apiJson.mockResolvedValue([
      ev({ id: 'old', title: 'Past social', startsAt: oldDate, endsAt: oldDate }),
      ev(),
    ])
    const w = mountView(EventsView)
    await flushPromises()
    expect(w.text()).toContain('Previous events')
  })

  it('shows the sentinel when there are more events than the initial window', async () => {
    const events = Array.from({ length: 8 }, (_, i) =>
      ev({ id: `e${i}`, title: `Event ${i}`, startsAt: `2999-0${(i % 9) + 1}-01T10:00:00Z` }))
    apiJson.mockResolvedValue(events)
    const w = mountView(EventsView)
    await flushPromises()
    expect(w.text()).toContain('Loading more events')
  })

  it('clicks the List button to return from calendar view', async () => {
    apiJson.mockResolvedValue([ev()])
    const w = mountView(EventsView)
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Calendar')!.trigger('click')
    expect(w.find('.event-calendar-stub').exists()).toBe(true)
    await w.findAll('button').find(b => b.text() === 'List')!.trigger('click')
    expect(w.text()).toContain('Coffee morning')
  })

  it('clicking Retry refreshes events after an error', async () => {
    apiJson.mockRejectedValue(new Error('down'))
    const w = mountView(EventsView)
    await flushPromises()
    apiJson.mockResolvedValue([ev()])
    await w.findAll('button').find(b => b.text() === 'Retry')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Coffee morning')
  })

  it('loads more events when the IntersectionObserver callback fires', async () => {
    // PAGE=6; need >6 events so hasMore is true after load
    const events = Array.from({ length: 8 }, (_, i) =>
      ev({ id: `e${i}`, title: `Event ${i}`, startsAt: `2999-0${(i % 9) + 1}-01T10:00:00Z`, endsAt: `2999-0${(i % 9) + 1}-01T12:00:00Z` }))
    let ioCallback: ((entries: { isIntersecting: boolean }[]) => void) | undefined
    class CapturingObserver {
      constructor(cb: (entries: { isIntersecting: boolean }[]) => void) { ioCallback = cb }
      observe = vi.fn(); unobserve = vi.fn(); disconnect = vi.fn()
    }
    vi.stubGlobal('IntersectionObserver', CapturingObserver)
    apiJson.mockResolvedValue(events)
    const w = mountView(EventsView)
    // Fire before events load — hasMore is false → covers if(hasMore) false branch
    ioCallback?.([{ isIntersecting: true }])
    await flushPromises()
    expect(w.text()).toContain('Loading more events')
    // Fire with false → covers if(isIntersecting) false branch
    ioCallback?.([{ isIntersecting: false }])
    await nextTick()
    ioCallback?.([{ isIntersecting: true }])
    await nextTick()
    expect(w.text()).toContain('Event 7')
  })
})

describe('EventsPreviousView', () => {
  it('lists past events', async () => {
    apiJson.mockResolvedValue([
      ev({ id: 'old', title: 'Old social', startsAt: '2020-01-01T10:00:00Z', endsAt: '2020-01-01T11:00:00Z' }),
      ev({ id: 'no-type', title: 'Typeless past', type: '', startsAt: '2020-02-01T10:00:00Z', endsAt: '2020-02-01T11:00:00Z' }),
    ])
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

  it('shows the error state', async () => {
    apiJson.mockRejectedValue(new Error('server error'))
    const w = mountView(EventsPreviousView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load events')
    expect(w.text()).toContain('Retry')
  })

  it('sorts past events newest-first when there are multiple', async () => {
    apiJson.mockResolvedValue([
      ev({ id: 'e1', title: 'Earlier social', startsAt: '2020-01-01T10:00:00Z', endsAt: '2020-01-01T11:00:00Z' }),
      ev({ id: 'e2', title: 'Later social', startsAt: '2021-06-01T10:00:00Z', endsAt: '2021-06-01T11:00:00Z' }),
    ])
    const w = mountView(EventsPreviousView)
    await flushPromises()
    const text = w.text()
    expect(text.indexOf('Later social')).toBeLessThan(text.indexOf('Earlier social'))
  })

  it('navigates to upcoming events when the back button is clicked', async () => {
    apiJson.mockResolvedValue([])
    const w = mountView(EventsPreviousView)
    await flushPromises()
    await w.findAll('button').find(b => b.text().includes('Upcoming events'))!.trigger('click')
    expect(router.push).toHaveBeenCalledWith({ name: 'events' })
  })

  it('clicking Retry in the error state reloads past events', async () => {
    apiJson.mockRejectedValue(new Error('server error'))
    const w = mountView(EventsPreviousView)
    await flushPromises()
    apiJson.mockResolvedValue([])
    await w.findAll('button').find(b => b.text() === 'Retry')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('No previous events found')
  })

  it('filters past events by type when a type pill is clicked', async () => {
    apiJson.mockResolvedValue([
      ev({ id: 'e1', title: 'Old coffee', type: 'Coffee meet-up', startsAt: '2020-01-01T10:00:00Z', endsAt: '2020-01-01T11:00:00Z' }),
      ev({ id: 'e2', title: 'Old quiz', type: 'Quiz', startsAt: '2020-02-01T18:00:00Z', endsAt: '2020-02-01T20:00:00Z' }),
    ])
    const w = mountView(EventsPreviousView)
    await flushPromises()
    const quizPill = w.findAll('button').find(b => b.text() === 'Quiz')!
    await quizPill.trigger('click')
    expect(w.text()).toContain('Old quiz')
    expect(w.text()).not.toContain('Old coffee')
  })

  it('loads more past events when the IntersectionObserver callback fires', async () => {
    // PAGE=10; need >10 past events so hasMore is true after load
    const pastEvents = Array.from({ length: 12 }, (_, i) =>
      ev({ id: `e${i}`, title: `Past Event ${i}`, startsAt: `2020-0${(i % 9) + 1}-01T10:00:00Z`, endsAt: `2020-0${(i % 9) + 1}-01T11:00:00Z` }))
    let ioCallback: ((entries: { isIntersecting: boolean }[]) => void) | undefined
    class CapturingObserver {
      constructor(cb: (entries: { isIntersecting: boolean }[]) => void) { ioCallback = cb }
      observe = vi.fn(); unobserve = vi.fn(); disconnect = vi.fn()
    }
    vi.stubGlobal('IntersectionObserver', CapturingObserver)
    apiJson.mockResolvedValue(pastEvents)
    const w = mountView(EventsPreviousView)
    // Fire before events load — hasMore is false → covers if(hasMore) false branch
    ioCallback?.([{ isIntersecting: true }])
    await flushPromises()
    expect(w.text()).toContain('Loading more events')
    // Fire with false → covers if(isIntersecting) false branch
    ioCallback?.([{ isIntersecting: false }])
    await nextTick()
    ioCallback?.([{ isIntersecting: true }])
    await nextTick()
    expect(w.text()).toContain('Past Event 11')
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

  it('uses router.back() when the back state is /events', async () => {
    route.params = { id: 'e1' }
    router.options.history.state.back = '/events'
    apiJson.mockResolvedValue(ev())
    const w = mountView(EventDetailView)
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.back).toHaveBeenCalled()
    expect(router.push).not.toHaveBeenCalled()
  })

  it('uses router.back() when the back state is /events/previous', async () => {
    route.params = { id: 'e1' }
    router.options.history.state.back = '/events/previous?page=2'
    apiJson.mockResolvedValue(ev())
    const w = mountView(EventDetailView)
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.back).toHaveBeenCalled()
  })

  it('renders prev/next navigation when the event has adjacent events', async () => {
    route.params = { id: 'e1' }
    apiJson.mockResolvedValue(ev({
      prev: { id: 'ep', title: 'Previous Coffee', startsAt: '2999-05-01T10:00:00Z' },
      next: { id: 'en', title: 'Next Coffee', startsAt: '2999-07-01T10:00:00Z' },
    }))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('Previous event')
    expect(w.text()).toContain('Next event')
    expect(w.text()).toContain('Previous Coffee')
    expect(w.text()).toContain('Next Coffee')
  })

  it('shows an error message on failure', async () => {
    route.params = { id: 'e1' }
    apiJson.mockRejectedValue(new Error('gone'))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load event')
  })

  it('shows the multi-day date range when the event spans more than one day', async () => {
    route.params = { id: 'e1' }
    apiJson.mockResolvedValue(ev({ startsAt: '2999-06-01T10:00:00Z', endsAt: '2999-06-02T12:00:00Z' }))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('To')
  })

  it('shows the sign-up link when signupUrl is provided', async () => {
    route.params = { id: 'e1' }
    apiJson.mockResolvedValue(ev({ signupUrl: 'https://example.com/signup' }))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.html()).toContain('https://example.com/signup')
  })

  it('renders only the next card when there is no previous event', async () => {
    route.params = { id: 'e1' }
    apiJson.mockResolvedValue(ev({
      next: { id: 'en', title: 'Next Coffee', startsAt: '2999-07-01T10:00:00Z' },
    }))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('Next event')
    expect(w.text()).not.toContain('Previous event')
  })

  it('renders only the prev card when there is no next event', async () => {
    route.params = { id: 'e1' }
    apiJson.mockResolvedValue(ev({
      prev: { id: 'ep', title: 'Previous Coffee', startsAt: '2999-05-01T10:00:00Z' },
    }))
    const w = mountView(EventDetailView)
    await flushPromises()
    expect(w.text()).toContain('Previous event')
    expect(w.text()).not.toContain('Next event')
  })
})

describe('ArticleDetailView', () => {
  const art = (over = {}) => ({ id: 'a1', slug: 's', title: 'Deep dive', summary: 'sum', body: '<p>hi</p>', author: 'Jo', publishedAt: '2026-05-01T00:00:00Z', readingMinutes: 5, category: 'Community', ...over })

  it('renders the fetched article', async () => {
    route.params = { slug: 's' }
    apiFetch.mockResolvedValue(jsonResponse(art()))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    expect(w.text()).toContain('Deep dive')
    expect(w.html()).toContain('hi')
  })

  it('uses router.back() when the back state is /articles', async () => {
    route.params = { slug: 's' }
    router.options.history.state.back = '/articles'
    apiFetch.mockResolvedValue(jsonResponse(art()))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.back).toHaveBeenCalled()
    expect(router.push).not.toHaveBeenCalled()
  })

  it('pushes to /articles when back state is not /articles', async () => {
    route.params = { slug: 's' }
    router.options.history.state.back = '/dashboard'
    apiFetch.mockResolvedValue(jsonResponse(art()))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    await w.find('button').trigger('click')
    expect(router.push).toHaveBeenCalledWith('/articles')
  })

  it('renders prev/next navigation when the article has adjacent articles', async () => {
    route.params = { slug: 's' }
    apiFetch.mockResolvedValue(jsonResponse(art({
      prev: { slug: 'prev-art', title: 'Previous Article' },
      next: { slug: 'next-art', title: 'Next Article' },
    })))
    const w = mountView(ArticleDetailView)
    await flushPromises()
    expect(w.text()).toContain('Previous')
    expect(w.text()).toContain('Next')
    expect(w.text()).toContain('Previous Article')
    expect(w.text()).toContain('Next Article')
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

  it('shows the no-roles message when the user has no roles', () => {
    // No initialize → auth.isAdmin=false (line 10 false branch), auth.roles.length=0 (line 27 false branch)
    const w = mountView(DashboardView)
    expect(w.text()).toContain('don')
    expect(w.text()).toContain('hold any SLYPN roles')
  })

  it('shows the pending-count badge on the Approvals tile when there are pending items', async () => {
    apiFetch.mockImplementation((url: string) => {
      if (url === '/articles?status=in-review') return Promise.resolve(jsonResponse([{ id: 'p1' }]))
      return Promise.resolve(jsonResponse([]))
    })
    const auth = useAuthStore()
    await auth.initialize()
    const w = mountView(DashboardView)
    await flushPromises()
    // pendingCount=1 → badge visible (line 50 true branch)
    const badgeEl = w.find('span.rounded-full.bg-amber-500')
    expect(badgeEl.exists()).toBe(true)
    expect(badgeEl.text()).toBe('1')
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
