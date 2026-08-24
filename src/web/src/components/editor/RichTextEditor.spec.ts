import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiFetch }
})

import RichTextEditor from './RichTextEditor.vue'

const mountEditor = (props: Record<string, unknown> = {}) =>
  mount(RichTextEditor, { props: { modelValue: '<p>hello</p>', ...props }, global: { stubs: { teleport: true } } })

beforeEach(() => apiFetch.mockReset())

describe('RichTextEditor', () => {
  it('renders the formatting toolbar', () => {
    const w = mountEditor()
    const labels = w.findAll('button').map(b => b.text())
    expect(labels).toContain('B')
    expect(labels).toContain('H1')
    expect(labels).toContain('🔗')
  })

  it('hides the toolbar in readonly mode', () => {
    const w = mountEditor({ readonly: true })
    expect(w.findAll('button')).toHaveLength(0)
  })

  it('runs formatting commands without error', async () => {
    const w = mountEditor()
    await w.findAll('button').find(b => b.text() === 'B')!.trigger('click')
    await w.findAll('button').find(b => b.text() === 'H2')!.trigger('click')
    await w.findAll('button').find(b => b.text() === '•')!.trigger('click')
    expect(w.exists()).toBe(true)
  })

  it('opens and cancels the link dialog', async () => {
    const w = mountEditor()
    await w.findAll('button').find(b => b.text() === '🔗')!.trigger('click')
    expect(w.text()).toContain('Insert link')
    await w.findAll('button').find(b => b.text() === 'Cancel')!.trigger('click')
    expect(w.text()).not.toContain('Insert link')
  })

  it('uploads an image through the media endpoint', async () => {
    apiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve({ name: 'p.png', url: 'https://cdn/p.png' }), text: () => Promise.resolve('') } as unknown as Response)
    const w = mountEditor()
    const file = new File(['x'], 'p.png', { type: 'image/png' })
    const input = w.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
    await input.trigger('change')
    await Promise.resolve()
    expect(apiFetch).toHaveBeenCalledWith('/media', expect.objectContaining({ method: 'POST' }))
  })
})
