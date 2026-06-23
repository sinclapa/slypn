import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

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

const stubs = { RouterLink: RouterLinkStub }
const mountView = (C: unknown) => mount(C as never, { global: { plugins: [createPinia()], stubs } })

const article = (over = {}) => ({
  id: 'a1', slug: 'a-1', title: 'Article One', summary: 's', body: 'b',
  author: 'Jane', publishedAt: '2026-05-01T00:00:00Z', readingMinutes: 3,
  category: 'Community', tags: [], ...over,
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
})

describe('HomeView', () => {
  it('renders featured articles, blog, and events', async () => {
    apiJson.mockImplementation((path: string) => {
      if (path.startsWith('/articles')) return Promise.resolve([article()])
      if (path.startsWith('/blog')) return Promise.resolve([{ id: 'p1', title: 'Blog One', summary: 's', author: 'Sam', publishedAt: '2026-05-01T00:00:00Z' }])
      if (path.startsWith('/events')) return Promise.resolve([{ id: 'e1', title: 'Coffee', type: 'Coffee meet-up', startsAt: '2026-06-01T10:00:00Z', endsAt: '2026-06-01T12:00:00Z', location: 'Brixton', description: 'd' }])
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
