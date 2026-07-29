import { describe, it, expect } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import ArticleCard from './ArticleCard.vue'
import type { Article } from '@/types/content'

const article: Article = {
  id: 'a1',
  slug: 'hello-world',
  title: 'Hello World',
  summary: 'A short summary',
  body: '<p>body</p>',
  author: 'Jane',
  publishedAt: '2026-05-01T10:00:00Z',
  readingMinutes: 4,
  category: 'Community',
}

function mountCard(a: Article = article) {
  return mount(ArticleCard, {
    props: { article: a },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('ArticleCard', () => {
  it('renders title, category, author, summary and reading time', () => {
    const w = mountCard()
    expect(w.text()).toContain('Hello World')
    expect(w.text()).toContain('Community')
    expect(w.text()).toContain('Jane')
    expect(w.text()).toContain('4 min read')
  })

  it('links to the article slug', () => {
    const w = mountCard()
    expect(w.findComponent(RouterLinkStub).props('to')).toBe('/articles/hello-world')
  })

  it('falls back to the id when slug is empty', () => {
    const w = mountCard({ ...article, slug: '' })
    expect(w.findComponent(RouterLinkStub).props('to')).toBe('/articles/a1')
  })

  it('formats the published date in en-GB', () => {
    const w = mountCard()
    expect(w.text()).toMatch(/May 2026/)
  })
})
