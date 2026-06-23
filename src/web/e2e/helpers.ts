import type { Page } from '@playwright/test'
import {
  DEV_PERSONA_STORAGE_KEY,
  type DevPersonaKey,
} from '../src/lib/devPersonas'

/**
 * Select the active dev persona before the app boots. Must be called before the
 * first `page.goto` — `addInitScript` runs on every navigation, so the auth
 * store picks up the right persona on initial render (no sign-in, no UI click).
 */
export async function usePersona(page: Page, key: DevPersonaKey): Promise<void> {
  await page.addInitScript(
    ([storageKey, value]) => {
      window.localStorage.setItem(storageKey, value)
    },
    [DEV_PERSONA_STORAGE_KEY, key] as const,
  )
}
