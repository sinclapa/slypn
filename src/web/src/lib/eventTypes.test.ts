import { describe, it, expect } from 'vitest'
import { EVENT_TYPES } from './eventTypes'

describe('EVENT_TYPES', () => {
  it('has the seven canonical types', () => {
    expect(EVENT_TYPES).toHaveLength(7)
    expect(EVENT_TYPES).toContain('Coffee meet-up')
    expect(EVENT_TYPES).toContain('Q&A')
  })

  it('is sorted alphabetically', () => {
    const sorted = [...EVENT_TYPES].sort((a, b) => a.localeCompare(b))
    expect([...EVENT_TYPES]).toEqual(sorted)
  })

  it('has no duplicates', () => {
    expect(new Set(EVENT_TYPES).size).toBe(EVENT_TYPES.length)
  })
})
