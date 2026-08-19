import { expect, test } from '../support/fixtures'

/**
 * The dev-persona switcher is the mechanism the whole `app` project depends on:
 * it is how a spec chooses who the browser — and, via the X-Slypn-Dev-User
 * header, the API — believes it is. If it stops working, every other spec in
 * this project is testing the wrong principal, so verify it directly.
 */
test.describe('dev persona switcher', () => {
  test.use({ persona: 'contributor' })

  test('reflects the persona chosen before the app booted', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByTestId('dev-persona-switcher')).toBeVisible()
    await expect(page.getByTestId('dev-persona-trigger')).toContainText('contributor')
  })

  test('the API agrees with the persona the UI is showing', async ({ page, api }) => {
    await page.goto('/dashboard')

    // The dashboard renders roles from GET /api/me, so this asserts the round
    // trip: localStorage -> auth store -> X-Slypn-Dev-User -> JwtMiddleware.
    await expect(page.getByText('Contributor', { exact: true }).first()).toBeVisible()

    const me = await api.json<{ roles: string[]; status: string }>('/me')
    expect(me.roles).toEqual(['Contributor'])
    expect(me.status).toBe('active')
  })

  test('switching persona in the UI changes what the API allows', async ({ page }) => {
    await page.goto('/')

    await page.getByTestId('dev-persona-trigger').click()
    await page.getByTestId('dev-persona-member').click()

    await expect(page.getByTestId('dev-persona-trigger')).toContainText('member')

    // A Member has no Contributor rights, so the router guard now bounces them.
    await page.goto('/editor')
    await expect(page).toHaveURL(/\/\?forbidden=/)
  })
})
