import { describe, it, expect, beforeEach } from 'vitest'
import {
  DEV_PERSONA_LIST,
  DEV_PERSONAS,
  DEFAULT_DEV_PERSONA_KEY,
  DEV_PERSONA_STORAGE_KEY,
  getActivePersona,
  getActivePersonaKey,
  setActivePersonaKey,
} from './devPersonas'

describe('devPersonas', () => {
  beforeEach(() => localStorage.clear())

  it('defaults to the admin persona when nothing is stored', () => {
    expect(getActivePersonaKey()).toBe('admin')
    expect(getActivePersona().roles).toEqual(['Admin'])
  })

  it('gives every persona exactly one role', () => {
    expect(DEV_PERSONA_LIST).toHaveLength(3)
    for (const p of DEV_PERSONA_LIST) {
      expect(p.roles).toHaveLength(1)
    }
  })

  it('round-trips a stored persona key', () => {
    setActivePersonaKey('member')
    expect(localStorage.getItem(DEV_PERSONA_STORAGE_KEY)).toBe('member')
    expect(getActivePersonaKey()).toBe('member')
    expect(getActivePersona()).toBe(DEV_PERSONAS.member)
  })

  it('falls back to the default on an unknown stored value', () => {
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, 'superuser')
    expect(getActivePersonaKey()).toBe(DEFAULT_DEV_PERSONA_KEY)
  })
})
