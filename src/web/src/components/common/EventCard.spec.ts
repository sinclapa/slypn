import { describe, it, expect } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import EventCard from './EventCard.vue'
import type { CommunityEvent } from '@/types/content'

const base: CommunityEvent = {
  id: 'e1',
  title: 'Coffee morning',
  type: 'Coffee meet-up',
  startsAt: '2026-05-10T10:00:00Z',
  endsAt: '2026-05-10T12:00:00Z',
  location: 'Brixton',
  description: 'Come along',
}

function mountCard(ev: Partial<CommunityEvent> = {}, props: Record<string, unknown> = {}) {
  return mount(EventCard, {
    props: { event: { ...base, ...ev }, ...props },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('EventCard', () => {
  it('renders type, title, location and description', () => {
    const w = mountCard()
    expect(w.text()).toContain('Coffee meet-up')
    expect(w.text()).toContain('Coffee morning')
    expect(w.text()).toContain('Brixton')
    expect(w.text()).toContain('Come along')
  })

  it('links to the event detail route', () => {
    const w = mountCard()
    expect(w.findComponent(RouterLinkStub).props('to')).toEqual({ name: 'event-detail', params: { id: 'e1' } })
  })

  it('renders a single-day time range when start and end share a day', () => {
    const w = mountCard()
    // single-day branch shows one date with two times (start–end); assert two
    // HH:MM tokens without pinning the timezone-dependent values.
    const times = w.text().match(/\d{2}:\d{2}/g) ?? []
    expect(times.length).toBeGreaterThanOrEqual(2)
  })

  it('renders a multi-day range across different days', () => {
    const w = mountCard({ endsAt: '2026-05-11T09:00:00Z' })
    expect(w.text()).toMatch(/May/)
  })

  it('shows a Past badge and signup link when applicable', () => {
    const w = mountCard({ signupUrl: 'https://example.com/signup' }, { past: true })
    expect(w.text()).toContain('Past')
    const link = w.find('a[href="https://example.com/signup"]')
    expect(link.exists()).toBe(true)
  })

  it('shows the year for events outside the current year', () => {
    const w = mountCard({ startsAt: '2020-01-15T10:00:00Z', endsAt: '2020-01-15T11:00:00Z' })
    expect(w.text()).toContain('2020')
  })
})
