import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import NewsletterCard from './NewsletterCard.vue'
import type { Newsletter } from '@/types/content'

const newsletter: Newsletter = {
  id: 'n1',
  title: 'May 2026 issue',
  issueDate: '2026-05-01',
  summary: 'What happened in May',
  topics: ['Meet-ups', 'Fundraising'],
}

describe('NewsletterCard', () => {
  it('renders the title, summary and formatted issue date', () => {
    const w = mount(NewsletterCard, { props: { newsletter } })
    expect(w.text()).toContain('May 2026 issue')
    expect(w.text()).toContain('What happened in May')
    expect(w.text()).toMatch(/May 2026/)
  })

  it('lists each topic', () => {
    const w = mount(NewsletterCard, { props: { newsletter } })
    const items = w.findAll('li')
    expect(items).toHaveLength(2)
    expect(w.text()).toContain('Meet-ups')
    expect(w.text()).toContain('Fundraising')
  })
})
