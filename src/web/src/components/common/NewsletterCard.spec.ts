import { describe, it, expect } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import NewsletterCard from './NewsletterCard.vue'
import type { Newsletter } from '@/types/content'

const newsletter: Newsletter = {
  id: 'n1',
  title: 'May 2026 issue',
  issueDate: '2026-05-01',
  summary: 'What happened in May',
  topics: ['Meet-ups', 'Fundraising'],
}

function mountCard(n: Newsletter = newsletter) {
  return mount(NewsletterCard, {
    props: { newsletter: n },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('NewsletterCard', () => {
  it('renders the title, summary and formatted issue date', () => {
    const w = mountCard()
    expect(w.text()).toContain('May 2026 issue')
    expect(w.text()).toContain('What happened in May')
    expect(w.text()).toMatch(/May 2026/)
  })

  it('lists each topic', () => {
    const w = mountCard()
    const items = w.findAll('li')
    expect(items).toHaveLength(2)
    expect(w.text()).toContain('Meet-ups')
    expect(w.text()).toContain('Fundraising')
  })

  it('shows a download link to the file endpoint when a file is attached', () => {
    const w = mountCard({ ...newsletter, fileName: 'SLYPN-Newsletter-2026-05.docx' })
    const link = w.get('a[download]')
    expect(link.attributes('href')).toBe('/api/newsletters/n1/file')
    expect(w.text()).toContain('Download issue')
  })

  it('links to the newsletter detail route when a file is attached', () => {
    const w = mountCard({ ...newsletter, fileName: 'SLYPN-Newsletter-2026-05.docx' })
    expect(w.findComponent(RouterLinkStub).props('to')).toEqual({ name: 'newsletter-detail', params: { id: 'n1' } })
    expect(w.text()).toContain('View')
  })

  it('omits the view and download links when no file is attached', () => {
    const w = mountCard()
    expect(w.find('a[download]').exists()).toBe(false)
    expect(w.findComponent(RouterLinkStub).exists()).toBe(false)
  })
})
