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
  // ── Length limit ───────────────────────────────────────────────────────────
  // Measured in HTML, which is what the API caps. The handlers that consume this
  // (handleTextInput / handlePaste / handleDrop) only fire on insertion, so a full
  // document can always be edited back down.

  it('is not full below the limit', () => {
    const w = mountEditor({ modelValue: 'x'.repeat(39), maxLength: 40 })
    expect((w.vm as unknown as { isFull: () => boolean }).isFull()).toBe(false)
  })

  it('is full at the limit and beyond', () => {
    const at = mountEditor({ modelValue: 'x'.repeat(40), maxLength: 40 })
    expect((at.vm as unknown as { isFull: () => boolean }).isFull()).toBe(true)

    const over = mountEditor({ modelValue: 'x'.repeat(60), maxLength: 40 })
    expect((over.vm as unknown as { isFull: () => boolean }).isFull()).toBe(true)
  })

  it('has no limit when maxLength is not given', () => {
    const w = mountEditor({ modelValue: 'x'.repeat(100_000) })
    expect((w.vm as unknown as { isFull: () => boolean }).isFull()).toBe(false)
  })

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
