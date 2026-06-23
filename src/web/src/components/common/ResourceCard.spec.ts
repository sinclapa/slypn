import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ResourceCard from './ResourceCard.vue'
import type { Resource } from '@/types/content'

const resource: Resource = {
  id: 'r1',
  title: 'Parkinson’s UK helpline',
  description: 'Free confidential support',
  url: 'https://www.parkinsons.org.uk/helpline',
  category: "Parkinson's UK",
}

describe('ResourceCard', () => {
  it('renders the title, category and description', () => {
    const w = mount(ResourceCard, { props: { resource } })
    expect(w.text()).toContain('Parkinson’s UK helpline')
    expect(w.text()).toContain("Parkinson's UK")
    expect(w.text()).toContain('Free confidential support')
  })

  it('links out to the url and strips the protocol in the label', () => {
    const w = mount(ResourceCard, { props: { resource } })
    const a = w.find('a')
    expect(a.attributes('href')).toBe('https://www.parkinsons.org.uk/helpline')
    expect(a.attributes('target')).toBe('_blank')
    expect(w.text()).toContain('www.parkinsons.org.uk/helpline')
    expect(w.text()).not.toContain('https://www.parkinsons.org.uk/helpline')
  })
})
