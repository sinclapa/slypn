import { expect, test } from '@playwright/test'

/**
 * The signed-out half of the routing guard.
 *
 * These can only run in the `anon` project (:5174, VITE_DEV_SKIP_AUTH=false).
 * On :5173 `useAuthStore.initialize()` short-circuits to a synthetic account,
 * so `isAuthenticated` is permanently true and this redirect is unreachable.
 */
const guarded = [
  '/dashboard',
  '/editor',
  '/admin/members',
  '/admin/content',
  '/admin/approvals',
  '/admin/events',
  '/admin/resources',
  '/admin/newsletters',
]

test.describe('anonymous visitor', () => {
  for (const path of guarded) {
    test(`is sent to the sign-in page from ${path}`, async ({ page }) => {
      await page.goto(path)

      // vue-router leaves `/` unescaped in the query value, so match the raw form.
      await expect(page).toHaveURL(`/login?returnTo=${path}`)
      // The hero title, not the sign-in button: which button LoginView renders
      // depends on whether VITE_MSAL_* happens to be configured, and that
      // differs between a dev box with .env.local and CI without one.
      await expect(page.getByRole('heading', { name: 'Sign in to SLYPN' })).toBeVisible()
    })
  }

  test('sees no account menu at all', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByTestId('user-menu-trigger')).toHaveCount(0)
    await expect(page.getByTestId('nav-account-link')).toHaveCount(0)
  })

  test('can still read every public page', async ({ page }) => {
    for (const path of ['/', '/about', '/articles', '/blog', '/events', '/resources', '/newsletter']) {
      await page.goto(path)
      await expect(page).toHaveURL(path)
      await expect(page.getByTestId('not-found')).toHaveCount(0)
    }
  })
})
