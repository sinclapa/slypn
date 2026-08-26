import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FieldCounter from './FieldCounter.vue'
import ClearFieldButton from './ClearFieldButton.vue'

const counter = (props: { used: number; max?: number; showFrom?: number; testid?: string }) =>
  mount(FieldCounter, { props: { max: 100, ...props } })

describe('FieldCounter', () => {
  it('stays hidden until the limit is close', () => {
    // A permanent "3 / 200" under every field is noise, and trains people to ignore it.
    expect(counter({ used: 0 }).find('[data-testid="field-counter"]').exists()).toBe(false)
    expect(counter({ used: 79 }).find('[data-testid="field-counter"]').exists()).toBe(false)
  })

  it('appears at the threshold and shows the figures', () => {
    const w = counter({ used: 80 })
    expect(w.text()).toContain('80 / 100')
    expect(w.text()).not.toContain('limit reached')
  })

  it('says so at the limit, and stays said beyond it', () => {
    expect(counter({ used: 100 }).text()).toContain('limit reached')
    expect(counter({ used: 140 }).text()).toContain('limit reached')
  })

  it('honours a custom threshold', () => {
    expect(counter({ used: 50, showFrom: 0.5 }).exists()).toBe(true)
    expect(counter({ used: 49, showFrom: 0.5 }).find('[data-testid="field-counter"]').exists()).toBe(false)
  })

  it('takes a testid so each field can be addressed on its own', () => {
    const w = counter({ used: 90, testid: 'summary-count' })
    expect(w.find('[data-testid="summary-count"]').exists()).toBe(true)
  })
})

describe('ClearFieldButton', () => {
  it('emits clear, and names the field for screen readers', async () => {
    const w = mount(ClearFieldButton, { props: { field: 'category' } })
    const btn = w.find('[data-testid="field-clear"]')
    expect(btn.attributes('aria-label')).toBe('Clear category')
    expect(btn.attributes('title')).toBe('Clear category')

    await btn.trigger('click')
    expect(w.emitted('clear')).toHaveLength(1)
  })
})
