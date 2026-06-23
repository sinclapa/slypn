import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import HeroBanner from './HeroBanner.vue'

describe('HeroBanner', () => {
  it('renders the title and shows eyebrow + subtitle when given', () => {
    const w = mount(HeroBanner, {
      props: { eyebrow: 'About', title: 'Our story', subtitle: 'A community' },
    })
    expect(w.text()).toContain('About')
    expect(w.find('h1').text()).toBe('Our story')
    expect(w.text()).toContain('A community')
  })

  it('omits eyebrow and subtitle when not provided', () => {
    const w = mount(HeroBanner, { props: { title: 'Just a title' } })
    expect(w.findAll('p')).toHaveLength(0)
    expect(w.find('h1').text()).toBe('Just a title')
  })

  it('renders brand, actions and default slots', () => {
    const w = mount(HeroBanner, {
      props: { title: 'T' },
      slots: {
        brand: '<div class="brand-slot">B</div>',
        actions: '<button>Go</button>',
        default: '<p class="extra">extra</p>',
      },
    })
    expect(w.find('.brand-slot').exists()).toBe(true)
    expect(w.find('button').text()).toBe('Go')
    expect(w.find('.extra').exists()).toBe(true)
  })
})
