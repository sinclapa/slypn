import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

const route = { params: {} as Record<string, string>, query: {} as Record<string, string>, hash: '', fullPath: '/' }
const router = { push: vi.fn(), replace: vi.fn(), back: vi.fn() }
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => route, useRouter: () => router }
})

import ArticlesView from './ArticlesView.vue'
import ResourcesView from './ResourcesView.vue'
import BlogView from './BlogView.vue'
import NewsletterView from './NewsletterView.vue'
import HomeView from './HomeView.vue'
import AboutView from './AboutView.vue'
import LoginView from './LoginView.vue'
import { useAuthStore } from '@/stores/auth'

const stubs = { RouterLink: RouterLinkStub }
const mountView = (C: unknown) => mount(C as never, { global: { plugins: [createPinia()], stubs } })

const article = (over = {}) => ({
  id: 'a1', slug: 'a-1', title: 'Article One', summary: 's', body: 'b',
  author: 'Jane', publishedAt: '2026-05-01T00:00:00Z', readingMinutes: 3,
  category: 'Community', ...over,
})

beforeEach(() => {
  setActivePinia(createPinia())
  apiJson.mockReset()
  apiFetch.mockReset()
  route.params = {}; route.query = {}; route.hash = ''
})

describe('ArticlesView', () => {
  it('renders a card per published article', async () => {
    apiJson.mockResolvedValue([article(), article({ id: 'a2', title: 'Article Two', category: 'Lifestyle' })])
    const w = mountView(ArticlesView)
    await flushPromises()
    expect(w.text()).toContain('Article One')
    expect(w.text()).toContain('Article Two')
  })

  it('filters by category when a pill is clicked', async () => {
    apiJson.mockResolvedValue([article(), article({ id: 'a2', title: 'Article Two', category: 'Lifestyle' })])
    const w = mountView(ArticlesView)
    await flushPromises()
    const lifestyle = w.findAll('button').find(b => b.text() === 'Lifestyle')!
    await lifestyle.trigger('click')
    expect(w.text()).toContain('Article Two')
    expect(w.text()).not.toContain('Article One')
  })

  it('shows the empty state when there are none', async () => {
    apiJson.mockResolvedValue([])
    const w = mountView(ArticlesView)
    await flushPromises()
    expect(w.text()).toContain('No articles in this category yet.')
  })

  it('shows an error with a retry button', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountView(ArticlesView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load articles')
    expect(w.find('button').text()).toContain('Retry')
  })
})

describe('ResourcesView', () => {
  const resource = (over = {}) => ({ id: 'r1', title: 'Helpline', description: 'd', url: 'https://x.org/a', category: 'NHS', ...over })

  it('renders resources and filters by category', async () => {
    apiJson.mockResolvedValue([resource(), resource({ id: 'r2', title: 'Local clinic', category: 'Local' })])
    const w = mountView(ResourcesView)
    await flushPromises()
    expect(w.text()).toContain('Helpline')
    const local = w.findAll('button').find(b => b.text() === 'Local')!
    await local.trigger('click')
    expect(w.text()).toContain('Local clinic')
    expect(w.text()).not.toContain('Helpline')
  })

  it('shows the error state', async () => {
    apiJson.mockRejectedValue(new Error('nope'))
    const w = mountView(ResourcesView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load resources')
  })
})

describe('BlogView', () => {
  const post = (over = {}) => ({ id: 'p1', title: 'Post One', summary: 's', body: '<p>b</p>', author: 'Sam', publishedAt: '2026-05-01T00:00:00Z', category: 'News', ...over })

  it('renders posts with anchors', async () => {
    apiJson.mockResolvedValue([post()])
    const w = mountView(BlogView)
    await flushPromises()
    expect(w.text()).toContain('Post One')
    expect(w.find('#post-p1').exists()).toBe(true)
  })

  it('shows the empty state', async () => {
    apiJson.mockResolvedValue([])
    const w = mountView(BlogView)
    await flushPromises()
    expect(w.text()).toContain('No posts yet')
  })

  it('shows the error state and a retry button', async () => {
    apiJson.mockRejectedValue(new Error('network error'))
    const w = mountView(BlogView)
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load the blog')
    expect(w.find('button').text()).toContain('Retry')
  })

  it('sorts posts newest-first when there are multiple', async () => {
    apiJson.mockResolvedValue([
      post({ id: 'p1', title: 'Older post', publishedAt: '2026-04-01T00:00:00Z' }),
      post({ id: 'p2', title: 'Newer post', publishedAt: '2026-05-01T00:00:00Z' }),
    ])
    const w = mountView(BlogView)
    await flushPromises()
    const titles = w.findAll('h2').map(h => h.text())
    expect(titles.indexOf('Newer post')).toBeLessThan(titles.indexOf('Older post'))
  })

  it('scrolls to the hash anchor once posts load', async () => {
    route.hash = '#post-p1'
    const scrollSpy = vi.fn()
    const querySpy = vi.spyOn(document, 'querySelector').mockImplementation((sel: string) =>
      sel === '#post-p1' ? ({ scrollIntoView: scrollSpy } as unknown as Element) : null)
    apiJson.mockResolvedValue([post()])
    const w = mountView(BlogView)
    await flushPromises()
    expect(w.find('#post-p1').exists()).toBe(true)
    await new Promise(r => setTimeout(r, 0))
    expect(scrollSpy).toHaveBeenCalled()
    querySpy.mockRestore()
  })

  it('filters posts by category when a pill is clicked', async () => {
    apiJson.mockResolvedValue([
      post({ id: 'p1', title: 'News Post', category: 'News' }),
      post({ id: 'p2', title: 'Community Post', category: 'Community' }),
    ])
    const w = mountView(BlogView)
    await flushPromises()
    const communityPill = w.findAll('button').find(b => b.text() === 'Community')!
    await communityPill.trigger('click')
    expect(w.text()).toContain('Community Post')
    expect(w.text()).not.toContain('News Post')
  })

  it('shows the loading state while posts are fetching', async () => {
    apiJson.mockReturnValue(new Promise(() => {}))
    const w = mountView(BlogView)
    await nextTick()
    expect(w.text()).toContain('Loading')
  })

  it('clicking Retry reloads the blog after an error', async () => {
    apiJson.mockRejectedValue(new Error('network error'))
    const w = mountView(BlogView)
    await flushPromises()
    apiJson.mockResolvedValue([post()])
    await w.findAll('button').find(b => b.text() === 'Retry')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Post One')
  })
})

describe('LoginView', () => {
  let lPinia: ReturnType<typeof createPinia>
  let auth: ReturnType<typeof useAuthStore>

  beforeEach(() => {
    lPinia = createPinia()
    setActivePinia(lPinia)
    auth = useAuthStore()
  })

  const mountLogin = () => mount(LoginView as never, { global: { plugins: [lPinia], stubs } })

  it('shows the sign-in button when not authenticated', async () => {
    const w = mountLogin()
    await flushPromises()
    expect(w.findAll('button').find(b => b.text()?.includes('Continue with'))).toBeDefined()
  })

  it('shows already-signed-in message when authenticated', async () => {
    await auth.initialize()
    const w = mountLogin()
    await flushPromises()
    expect(w.text()).toContain('already signed in')
  })

  it('shows a sign-in error when login throws', async () => {
    const loginSpy = vi.spyOn(auth, 'login').mockRejectedValue(new Error('sign-in failed'))
    const w = mountLogin()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Continue with'))!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('sign-in failed')
    loginSpy.mockRestore()
  })

  it('shows a sign-in error as string when login rejects with non-Error', async () => {
    const loginSpy = vi.spyOn(auth, 'login').mockRejectedValue('network issue')
    const w = mountLogin()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Continue with'))!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('network issue')
    loginSpy.mockRestore()
  })

  it('uses returnTo query param when it starts with slash', async () => {
    route.query = { returnTo: '/dashboard' }
    const loginSpy = vi.spyOn(auth, 'login').mockResolvedValue(undefined)
    const w = mountLogin()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Continue with'))!.trigger('click')
    expect(loginSpy).toHaveBeenCalledWith(expect.stringContaining('/dashboard'))
    loginSpy.mockRestore()
  })

  it('falls back to origin when returnTo does not start with slash', async () => {
    route.query = { returnTo: 'https://evil.com' }
    const loginSpy = vi.spyOn(auth, 'login').mockResolvedValue(undefined)
    const w = mountLogin()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Continue with'))!.trigger('click')
    expect(loginSpy).toHaveBeenCalledWith(expect.not.stringContaining('evil.com'))
    loginSpy.mockRestore()
  })
})

describe('NewsletterView', () => {
  it('lists past issues and subscribes on submit', async () => {
    apiJson.mockResolvedValue([{ id: 'n1', title: 'May 2026', issueDate: '2026-05-01', summary: 's', topics: ['x'] }])
    apiFetch.mockResolvedValue({ ok: true, text: () => Promise.resolve('') })
    const w = mountView(NewsletterView)
    await flushPromises()
    expect(w.text()).toContain('Past issues')
    expect(w.text()).toContain('May 2026')

    await w.find('input[type="email"]').setValue('me@example.com')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/newsletter/subscribe', expect.objectContaining({ method: 'POST' }))
    expect(w.text()).toContain('you’re on the list')
  })

  it('surfaces a subscribe error', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockResolvedValue({ ok: false, status: 400, statusText: 'Bad', text: () => Promise.resolve('nope') })
    const w = mountView(NewsletterView)
    await flushPromises()
    await w.find('input[type="email"]').setValue('me@example.com')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('Couldn’t subscribe')
  })

  it('shows a load error and retries when Retry is clicked', async () => {
    apiJson.mockRejectedValue(new Error('load boom'))
    const w = mountView(NewsletterView)
    await flushPromises()
    expect(w.text()).toContain('Retry')
    apiJson.mockResolvedValue([{ id: 'n1', title: 'May 2026', issueDate: '2026-05-01', summary: 's', topics: ['x'] }])
    await w.findAll('button').find(b => b.text() === 'Retry')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('May 2026')
  })

  it('shows subscribe error as string when rejection is not an Error', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockRejectedValue('subscribe string error')
    const w = mountView(NewsletterView)
    await flushPromises()
    await w.find('input[type="email"]').setValue('me@example.com')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('subscribe string error')
  })
})

describe('HomeView', () => {
  it('renders featured articles, blog, and events', async () => {
    apiJson.mockImplementation((path: string) => {
      if (path.startsWith('/articles')) return Promise.resolve([
        article({ id: 'a1', title: 'Article One', publishedAt: '2026-04-01T00:00:00Z' }),
        article({ id: 'a2', title: 'Article Two', publishedAt: '2026-05-01T00:00:00Z' }),
      ])
      if (path.startsWith('/blog')) return Promise.resolve([
        { id: 'p1', title: 'Blog One', summary: 's', author: 'Sam', publishedAt: '2026-04-01T00:00:00Z' },
        { id: 'p2', title: 'Blog Two', summary: 's', author: 'Sam', publishedAt: '2026-05-01T00:00:00Z' },
      ])
      if (path.startsWith('/events')) return Promise.resolve([
        { id: 'e1', title: 'Coffee', type: 'Coffee meet-up', startsAt: '2026-06-01T10:00:00Z', endsAt: '2026-06-01T12:00:00Z', location: 'Brixton', description: 'd' },
        { id: 'e2', title: 'Quiz', type: 'Quiz', startsAt: '2026-07-01T18:00:00Z', endsAt: '2026-07-01T20:00:00Z', location: 'Pub', description: 'd' },
      ])
      return Promise.resolve([])
    })
    const w = mountView(HomeView)
    await flushPromises()
    expect(w.text()).toContain('From our members')
    expect(w.text()).toContain('Article One')
    expect(w.text()).toContain('Blog One')
    expect(w.text()).toContain('Coffee')
  })
})

describe('AboutView', () => {
  it('renders the static about content', () => {
    const w = mountView(AboutView)
    expect(w.text()).toContain('Who we are')
    expect(w.text()).toContain('Founders')
  })
})
