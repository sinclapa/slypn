import { describe, it, expect } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import AppFooter from './AppFooter.vue'

function mountFooter() {
  return mount(AppFooter, { global: { stubs: { RouterLink: RouterLinkStub } } })
}

describe('AppFooter', () => {
  it('renders the white logo and explore links', () => {
    const w = mountFooter()
    expect(w.find('img[alt="SLYPN"]').exists()).toBe(true)
    const links = w.findAllComponents(RouterLinkStub).map(l => l.props('to'))
    expect(links).toContain('/about')
    expect(links).toContain('/newsletter')
  })

  it('shows the current year and the Parkinson’s UK affiliation link', () => {
    const w = mountFooter()
    expect(w.text()).toContain(String(new Date().getFullYear()))
    const ext = w.find('a[href="https://www.parkinsons.org.uk/"]')
    expect(ext.exists()).toBe(true)
  })
})
