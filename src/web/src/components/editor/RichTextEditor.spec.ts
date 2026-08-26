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
  // Counted in visible text, not HTML, so the figure matches what the author sees.
  // The parent measures it once per change and passes it down as currentLength.
  // The handlers that consume this only fire on insertion, so a full document can
  // always be edited back down.

  const isFull = (w: ReturnType<typeof mountEditor>) =>
    (w.vm as unknown as { isFull: () => boolean }).isFull()

  it('is not full below the limit', () => {
    expect(isFull(mountEditor({ maxLength: 40, currentLength: 39 }))).toBe(false)
  })

  it('is full at the limit and beyond', () => {
    expect(isFull(mountEditor({ maxLength: 40, currentLength: 40 }))).toBe(true)
    expect(isFull(mountEditor({ maxLength: 40, currentLength: 60 }))).toBe(true)
  })

  it('has no limit when maxLength is not given', () => {
    expect(isFull(mountEditor({ currentLength: 100_000 }))).toBe(false)
  })

  it('counts text, so markup does not eat the allowance', () => {
    // A short sentence wrapped in a link is long as HTML and short as text. The
    // parent decides that; this asserts the editor trusts the figure it is given.
    const w = mountEditor({
      modelValue: '<p><a href="https://example.com/a/very/long/url">hi</a></p>',
      maxLength: 40,
      currentLength: 2,
    })
    expect(isFull(w)).toBe(false)
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
