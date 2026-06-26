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

  it('lists all personas once opened, including the second admin and contributor', async () => {
    const wrapper = mount(DevPersonaSwitcher, { global: { plugins: [createPinia()] } })

    await wrapper.find('[data-testid="dev-persona-trigger"]').trigger('click')

    expect(wrapper.find('[data-testid="dev-persona-admin"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-admin2"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-contributor"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-contributor2"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dev-persona-member"]').exists()).toBe(true)
  })

  it('places the control in the chosen corner via the dropdown buttons and persists it', async () => {
    const w = mount(DevPersonaSwitcher, { global: { plugins: [createPinia()] } })
    const root = w.find('[data-testid="dev-persona-switcher"]')
    await w.find('[data-testid="dev-persona-trigger"]').trigger('click') // open

    // default: bottom-left
    expect(root.classes()).toEqual(expect.arrayContaining(['bottom-4', 'left-4']))

    await w.find('[data-testid="dev-persona-corner-top-right"]').trigger('click')
    expect(root.classes()).toEqual(expect.arrayContaining(['top-4', 'right-4']))
    expect(localStorage.getItem('slypn.devPersona.corner')).toBe('top-right')

    await w.find('[data-testid="dev-persona-corner-bottom-right"]').trigger('click')
    expect(root.classes()).toEqual(expect.arrayContaining(['bottom-4', 'right-4']))
    expect(localStorage.getItem('slypn.devPersona.corner')).toBe('bottom-right')
  })
})
