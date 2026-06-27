import { test, expect } from '@playwright/test'
import { usePersona } from './helpers'

test.describe('dev persona switcher', () => {
  test('switcher is visible and reflects the active persona', async ({ page }) => {
    await usePersona(page, 'contributor')
    await page.goto('/')

    const switcher = page.getByTestId('dev-persona-switcher')
    await expect(switcher).toBeVisible()
    await expect(page.getByTestId('dev-persona-trigger')).toContainText('contributor')
  })

  test('admin persona sees admin nav and can open /admin', async ({ page }) => {
    await usePersona(page, 'admin')
    await page.goto('/')

    await page.getByTestId('user-menu-trigger').click()
    await expect(page.getByRole('link', { name: 'Members' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Admin' })).toBeVisible()

    await page.goto('/admin')
    await expect(page).toHaveURL(/\/admin$/)
  })

  test('member persona has no admin nav and is bounced from /admin', async ({ page }) => {
    await usePersona(page, 'member')
    await page.goto('/')

    await page.getByTestId('user-menu-trigger').click()
    await expect(page.getByRole('link', { name: 'Members' })).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'Admin' })).toHaveCount(0)

    // Router guard redirects unauthorised roles back home with ?forbidden=.
    await page.goto('/admin/members')
    await expect(page).toHaveURL(/\/\?forbidden=/)
  })
})
