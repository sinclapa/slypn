import { expect, type Locator, type Page } from '@playwright/test'
import {
  DEV_PERSONA_STORAGE_KEY,
  type DevPersonaKey,
} from '../src/lib/devPersonas'

/**
 * Select the active dev persona before the app boots. Must be called before the
 * first `page.goto` — `addInitScript` runs on every navigation, so the auth
 * store picks up the right persona on initial render (no sign-in, no UI click).
 *
 * Most specs get this from the `persona` fixture instead; this remains for the
 * cases that build their own context (see anon/cookie-banner.spec.ts).
 */
export async function usePersona(page: Page, key: DevPersonaKey): Promise<void> {
  await page.addInitScript(
    ([storageKey, value]) => {
      window.localStorage.setItem(storageKey, value)
    },
    [DEV_PERSONA_STORAGE_KEY, key] as const,
  )
}

/**
 * Handle the next native `confirm()`, asserting its message first.
 *
 * Six views gate destructive actions behind `window.confirm` (EditorView,
 * PublishedContent, and the Member/Event/Resource/Newsletter management views).
 * Playwright auto-DISMISSES dialogs when nothing is listening, so without this
 * a "delete" click silently does nothing and the test fails somewhere far away.
 *
 * Registered per action rather than globally, so each test states which dialog
 * it expects — and `dismiss` is a first-class option, letting us prove the
 * cancel path too.
 */
export async function withConfirm(
  page: Page,
  message: RegExp,
  choice: 'accept' | 'dismiss',
  action: () => Promise<void>,
): Promise<void> {
  const seen = new Promise<string>((resolve) => {
    page.once('dialog', (dialog) => {
      const text = dialog.message()
      void (choice === 'accept' ? dialog.accept() : dialog.dismiss()).then(() => resolve(text))
    })
  })
  await action()
  expect(await seen).toMatch(message)
}

/**
 * Type into the TipTap surface. ProseMirror is a contenteditable, not an input,
 * so `fill()` is a no-op there — the editor only updates on real key events.
 */
export async function typeInRichText(page: Page, text: string): Promise<void> {
  const surface = page.getByTestId('rte-content').locator('.ProseMirror')
  await surface.click()
  await page.keyboard.type(text)
}

/**
 * Navigate with a full document load.
 *
 * `App.vue` wraps the router view in `<keep-alive :include="['EventsView',
 * 'ArticlesView']">`, so returning to either view via SPA navigation replays the
 * cached instance and never re-runs `onMounted` — the list would still show
 * pre-mutation data. Any assertion about newly created public content must come
 * through here.
 */
export async function gotoFresh(page: Page, path: string): Promise<void> {
  await page.goto(path, { waitUntil: 'domcontentloaded' })
}

/**
 * The row for one entity, located by the uid embedded in its title rather than
 * by position — the demo seed and other parallel workers both put unrelated
 * rows in these lists.
 */
export function rowFor(page: Page, testId: string, uid: string): Locator {
  return page.getByTestId(testId).filter({ hasText: uid })
}

/**
 * Open a draft from the editor list and wait until it is actually loaded.
 *
 * `DraftEditor.loadDraft()` is async and starts from EMPTY_DRAFT, so typing
 * straight after the click races the fetch: when it resolves it replaces
 * `draft.value` wholesale and silently discards whatever was typed. Waiting for
 * the title field to show this draft's uid closes that window.
 */
export async function openDraftRow(page: Page, uid: string): Promise<void> {
  await page.getByTestId('draft-row').filter({ hasText: uid }).click()
  await waitForDraftLoaded(page, uid)
}

/**
 * Wait until the open DraftEditor has finished fetching its draft.
 *
 * Applies to every way the editor can be opened, including the edit dialog in
 * PublishedContent. Until `loadDraft()` resolves the form still holds
 * EMPTY_DRAFT, and anything typed in the meantime is thrown away when the
 * fetched content replaces `draft.value`.
 */
export async function waitForDraftLoaded(page: Page, uid: string): Promise<void> {
  await expect(page.getByTestId('draft-editor')).toBeVisible()
  await expect(page.locator('#draft-title')).toHaveValue(new RegExp(uid))
}
