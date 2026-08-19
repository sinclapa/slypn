import { expect, test } from '@playwright/test'

/**
 * The consent banner, tested without the suite-wide priming that every other
 * spec relies on — this is the one place the raw first-visit state matters.
 *
 * It is worth its own spec because the banner gates Faro: `setupFaro()` only
 * initialises once consent is accepted, so a broken banner silently means
 * either no telemetry or telemetry without consent.
 */
test.describe('cookie banner', () => {
  test('appears on a first visit', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByTestId('cookie-banner')).toBeVisible()
  })

  test('accepting hides it and survives a reload', async ({ page }) => {
    await page.goto('/')
    await page.getByTestId('cookie-accept').click()

    await expect(page.getByTestId('cookie-banner')).toHaveCount(0)
    expect(await readConsent(page)).toBe('accepted')

    await page.reload()
    await expect(page.getByTestId('cookie-banner')).toHaveCount(0)
  })

  test('declining also hides it and is remembered', async ({ page }) => {
    await page.goto('/')
    await page.getByTestId('cookie-decline').click()

    await expect(page.getByTestId('cookie-banner')).toHaveCount(0)
    expect(await readConsent(page)).toBe('declined')

    await page.reload()
    await expect(page.getByTestId('cookie-banner')).toHaveCount(0)
  })
})

function readConsent(page: import('@playwright/test').Page) {
  return page.evaluate(() => window.localStorage.getItem('slypn:cookie-consent'))
}
