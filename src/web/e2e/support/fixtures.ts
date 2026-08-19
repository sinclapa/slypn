import { test as base, expect, type Page } from '@playwright/test'
import { DEV_PERSONA_STORAGE_KEY, type DevPersonaKey } from '../../src/lib/devPersonas'
import { createApiClient, type ApiClient } from './api-client'
import { makeUid } from './ids'

/**
 * The only `test` the specs import.
 *
 * Two things here do most of the work of keeping the suite stable:
 *
 * 1. `page` pre-seeds localStorage before the app boots. The cookie banner is a
 *    full-width `<dialog>` pinned to the bottom of the viewport and the dev
 *    persona switcher is `fixed` in a corner — both sit above real controls and
 *    would make clicks fail Playwright's actionability checks.
 * 2. `api` gives every spec a direct, persona-scoped client so a UI assertion
 *    can be backed by a check of what the server actually stored.
 */

const CONSENT_STORAGE_KEY = 'slypn:cookie-consent'
const CORNER_STORAGE_KEY = 'slypn.devPersona.corner'

export interface SlypnFixtures {
  /** Persona the browser and `api` act as. Override per file with test.use(). */
  persona: DevPersonaKey
  /** API client bound to `persona`. */
  api: ApiClient
  /** API client that is always Admin — for cross-persona setup and assertions. */
  adminApi: ApiClient
  /** Anonymous API client — no persona header at all. */
  anonApi: ApiClient
  /** Unique-per-test token; put it in every title this test creates. */
  uid: string
  /** Register teardown work; drained LIFO after the test. */
  cleanup: (fn: () => Promise<void>) => void
}

export const test = base.extend<SlypnFixtures>({
  persona: ['admin', { option: true }],

  page: async ({ page, persona }, use) => {
    await primePage(page, persona)
    await use(page)
  },

  api: async ({ persona }, use) => {
    const client = await createApiClient(persona)
    await use(client)
    await client.dispose()
  },

  adminApi: async ({ persona }, use) => {
    // Reuse the persona client when it is already an admin, so a spec running
    // as `admin2` does not accidentally act as `admin` and cross partitions.
    const client = await createApiClient(persona.startsWith('admin') ? persona : 'admin')
    await use(client)
    await client.dispose()
  },

  anonApi: async ({}, use) => {
    const client = await createApiClient(null)
    await use(client)
    await client.dispose()
  },

  uid: async ({}, use, testInfo) => {
    await use(makeUid(testInfo.parallelIndex))
  },

  cleanup: async ({}, use) => {
    const stack: (() => Promise<void>)[] = []
    await use((fn) => { stack.push(fn) })
    while (stack.length > 0) {
      const fn = stack.pop()!
      // Teardown is best-effort: a failed delete must not mask the real failure.
      try { await fn() } catch (err) { console.warn('[e2e] cleanup step failed:', err) }
    }
  },
})

/**
 * Seed the storage keys the app reads at boot. `addInitScript` runs before any
 * app code on every navigation, so these are in place by the time the auth
 * store and the cookie composable read them.
 *
 * Each key is only seeded when ABSENT. `addInitScript` also fires on reloads,
 * and several flows reload deliberately — `auth.setPersona()` does, and so does
 * accepting the cookie banner — so overwriting every time would undo whatever
 * the test just did.
 */
export async function primePage(page: Page, persona: DevPersonaKey | null): Promise<void> {
  await page.addInitScript(
    ([personaKey, personaStore, consentKey, cornerKey]) => {
      const seed = (key: string, value: string) => {
        if (window.localStorage.getItem(key) === null) window.localStorage.setItem(key, value)
      }
      if (personaKey) seed(personaStore, personaKey)
      seed(consentKey, 'accepted')
      // Park the dev switcher away from the bottom-left action buttons.
      seed(cornerKey, 'top-right')
    },
    [persona, DEV_PERSONA_STORAGE_KEY, CONSENT_STORAGE_KEY, CORNER_STORAGE_KEY] as const,
  )
}

export { expect }
