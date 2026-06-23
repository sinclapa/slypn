import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PillFilter from './PillFilter.vue'

describe('PillFilter', () => {
  it('renders nothing when there are no options', () => {
    const w = mount(PillFilter, { props: { options: [], modelValue: 'All' } })
    expect(w.find('button').exists()).toBe(false)
  })

  it('renders All plus an option per entry', () => {
    const w = mount(PillFilter, { props: { options: ['A', 'B'], modelValue: 'All' } })
    const buttons = w.findAll('button')
    expect(buttons).toHaveLength(3)
    expect(buttons[0].text()).toBe('All')
  })

  it('emits the option value on click', async () => {
    const w = mount(PillFilter, { props: { options: ['A', 'B'], modelValue: 'All' } })
    await w.findAll('button')[1].trigger('click')
    expect(w.emitted('update:modelValue')![0]).toEqual(['A'])
  })

  it('emits All when the All button is clicked', async () => {
    const w = mount(PillFilter, { props: { options: ['A'], modelValue: 'A' } })
    await w.findAll('button')[0].trigger('click')
    expect(w.emitted('update:modelValue')![0]).toEqual(['All'])
  })

  it('marks the active option', () => {
    const w = mount(PillFilter, { props: { options: ['A', 'B'], modelValue: 'B' } })
    const active = w.findAll('button')[2]
    expect(active.classes().join(' ')).toContain('bg-slypn-600')
  })
})
