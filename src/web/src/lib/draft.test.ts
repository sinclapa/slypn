import { describe, it, expect, vi } from 'vitest'
import { EMPTY_DRAFT, makeDraftId } from './draft'

describe('draft helpers', () => {
  it('EMPTY_DRAFT is an article skeleton with sane defaults', () => {
    expect(EMPTY_DRAFT.type).toBe('article')
    expect(EMPTY_DRAFT.title).toBe('')
    expect(EMPTY_DRAFT.tags).toEqual([])
    expect(EMPTY_DRAFT.readingMinutes).toBe(1)
  })

  it('makeDraftId returns a 32-char hex id with no dashes', () => {
    const id = makeDraftId()
    expect(id).toMatch(/^[0-9a-f]{32}$/)
    expect(id).not.toContain('-')
  })

  it('makeDraftId returns unique ids', () => {
    const ids = new Set(Array.from({ length: 50 }, () => makeDraftId()))
    expect(ids.size).toBe(50)
  })

  it('falls back to Math.random when crypto.randomUUID is absent', () => {
    const orig = globalThis.crypto
    vi.stubGlobal('crypto', { getRandomValues: vi.fn() })
    try {
      const id = makeDraftId()
      expect(id).toMatch(/^[0-9a-f]+$/)
    } finally {
      vi.stubGlobal('crypto', orig)
    }
  })
})
