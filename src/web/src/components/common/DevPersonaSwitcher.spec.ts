import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import DevPersonaSwitcher from './DevPersonaSwitcher.vue'

// VITE_DEV_SKIP_AUTH=true (vitest.config.ts) makes the switcher render.
describe('DevPersonaSwitcher', () => {
  beforeEach(() => localStorage.clear())

  it('renders the trigger reflecting the active persona', () => {
    const wrapper = mount(DevPersonaSwitcher, { global: { plugins: [createPinia()] } })

    expect(wrapper.find('[data-testid="dev-persona-switcher"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-trigger"]').text()).toContain('admin')
  })

  it('lists all three personas once opened', async () => {
    const wrapper = mount(DevPersonaSwitcher, { global: { plugins: [createPinia()] } })

    await wrapper.find('[data-testid="dev-persona-trigger"]').trigger('click')

    expect(wrapper.find('[data-testid="dev-persona-admin"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-contributor"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-member"]').exists()).toBe(true)
  })
})
