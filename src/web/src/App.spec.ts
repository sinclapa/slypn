import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import App from './App.vue'

describe('App shell', () => {
  it('renders nav, footer, and the router outlet', () => {
    const w = mount(App, {
      global: {
        plugins: [createPinia()],
        stubs: {
          AppNav: { template: '<nav class="nav-stub" />' },
          AppFooter: { template: '<footer class="footer-stub" />' },
          CookieBanner: { template: '<div class="cookie-stub" />' },
          DevPersonaSwitcher: { template: '<div class="persona-stub" />' },
          RouterView: { template: '<main class="outlet-stub" />' },
        },
      },
    })
    expect(w.find('.nav-stub').exists()).toBe(true)
    expect(w.find('.footer-stub').exists()).toBe(true)
    expect(w.find('.outlet-stub').exists()).toBe(true)
    expect(w.find('.cookie-stub').exists()).toBe(true)
  })
})
