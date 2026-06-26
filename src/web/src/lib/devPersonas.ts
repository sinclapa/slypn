/**
 * Local-dev test personas. Pairs with the API's matching DevPersonas table.
 *
 * When VITE_DEV_SKIP_AUTH=true the app signs in as one of these synthetic
 * accounts instead of going through Entra. The active persona is stored in
 * localStorage so it survives reloads and can be set by Playwright (via
 * addInitScript) before the first navigation. The persona key is also sent to
 * the API on every request (X-Slypn-Dev-User) so the backend synthesises the
 * matching principal and enforces the right role gate.
 *
 * Keep the keys/OIDs/roles in sync with
 * src/api/Slypn.Api/Infrastructure/DevPersonas.cs.
 */
export type DevPersonaKey = 'admin' | 'admin2' | 'contributor' | 'contributor2' | 'member'

export interface DevPersona {
  key: DevPersonaKey
  /** Email / username shown in the UI and stored on the seeded member record. */
  username: string
  /** Display name. */
  name: string
  /** Stable fake OID — matches the seeded member's Oid on the API side. */
  oid: string
  /** Roles this persona holds (exactly one each, by design). */
  roles: string[]
}

export const DEV_PERSONA_STORAGE_KEY = 'slypn.devPersona'

export const DEV_PERSONAS: Record<DevPersonaKey, DevPersona> = {
  admin: {
    key: 'admin',
    username: 'slypn.test.admin@cookingcode.com',
    name: 'Test Admin',
    oid: '11111111-1111-1111-1111-111111111111',
    roles: ['Admin'],
  },
  admin2: {
    key: 'admin2',
    username: 'slypn.test.admin2@cookingcode.com',
    name: 'Test Admin 2',
    oid: '44444444-4444-4444-4444-444444444444',
    roles: ['Admin'],
  },
  contributor: {
    key: 'contributor',
    username: 'slypn.test.contributor@cookingcode.com',
    name: 'Test Contributor',
    oid: '22222222-2222-2222-2222-222222222222',
    roles: ['Contributor'],
  },
  contributor2: {
    key: 'contributor2',
    username: 'slypn.test.contributor2@cookingcode.com',
    name: 'Test Contributor 2',
    oid: '55555555-5555-5555-5555-555555555555',
    roles: ['Contributor'],
  },
  member: {
    key: 'member',
    username: 'slypn.test.member@cookingcode.com',
    name: 'Test Member',
    oid: '33333333-3333-3333-3333-333333333333',
    roles: ['Member'],
  },
}

/** Ordered list for rendering the switcher. */
export const DEV_PERSONA_LIST: DevPersona[] = [
  DEV_PERSONAS.admin,
  DEV_PERSONAS.admin2,
  DEV_PERSONAS.contributor,
  DEV_PERSONAS.contributor2,
  DEV_PERSONAS.member,
]

/** Default persona when nothing is selected — keeps first-run "all open". */
export const DEFAULT_DEV_PERSONA_KEY: DevPersonaKey = 'admin'

function isPersonaKey(value: string | null): value is DevPersonaKey {
  return value === 'admin' || value === 'admin2' || value === 'contributor' || value === 'contributor2' || value === 'member'
}

/** The persona key currently stored in localStorage (default `admin`). */
export function getActivePersonaKey(): DevPersonaKey {
  try {
    const stored = localStorage.getItem(DEV_PERSONA_STORAGE_KEY)
    if (isPersonaKey(stored)) return stored
  } catch {
    // localStorage unavailable (SSR / privacy mode) — fall through to default.
  }
  return DEFAULT_DEV_PERSONA_KEY
}

/** The full persona object currently active. */
export function getActivePersona(): DevPersona {
  return DEV_PERSONAS[getActivePersonaKey()]
}

/** Persist the selected persona key. */
export function setActivePersonaKey(key: DevPersonaKey): void {
  try {
    localStorage.setItem(DEV_PERSONA_STORAGE_KEY, key)
  } catch {
    // Ignore — non-persisted switch still works for the current page.
  }
}
