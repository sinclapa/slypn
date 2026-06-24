import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SaveIndicator from './SaveIndicator.vue'
import type { AutoSaveStatus } from '@/composables/useAutoSave'

function mountIndicator(props: { status: AutoSaveStatus; lastSavedAt?: Date | null; error?: string | null }) {
  return mount(SaveIndicator, { props: { lastSavedAt: null, ...props } })
}

describe('SaveIndicator', () => {
  it('shows "Not saved yet" when idle with no save', () => {
    const w = mountIndicator({ status: 'idle' })
    expect(w.text()).toContain('Not saved yet')
  })

  it('shows editing while pending', () => {
    const w = mountIndicator({ status: 'pending' })
    expect(w.html()).toContain('Editing')
  })

  it('shows saving while saving', () => {
    const w = mountIndicator({ status: 'saving' })
    expect(w.html()).toContain('Saving')
  })

  it('shows the error message on error', () => {
    const w = mountIndicator({ status: 'error', error: 'boom' })
    expect(w.text()).toContain('Save failed: boom')
  })

  it('shows a saved timestamp when saved', () => {
    const w = mountIndicator({ status: 'saved', lastSavedAt: new Date('2026-05-01T09:30:00Z') })
    expect(w.text()).toMatch(/Saved at \d{2}:\d{2}:\d{2}/)
  })

  it('shows saved when a lastSavedAt exists even if status is idle', () => {
    const w = mountIndicator({ status: 'idle', lastSavedAt: new Date('2026-05-01T09:30:00Z') })
    expect(w.text()).toContain('Saved at')
  })

  it('applies a status-specific dot colour', () => {
    const w = mountIndicator({ status: 'saved', lastSavedAt: new Date() })
    expect(w.find('span').classes().join(' ')).toContain('bg-emerald-500')
  })
})
