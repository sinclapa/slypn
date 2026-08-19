import { expect, test } from '../support/fixtures'

/**
 * The signed-in half of the routing guard: which role may reach which route,
 * and which account links each role is offered.
 *
 * Both sides are asserted because they fail independently — the nav can hide a
 * link the guard still lets through, and vice versa. The server-side half of
 * the same rules lives in permissions.spec.ts.
 */

const ADMIN_ONLY = ['/admin/members', '/admin/approvals', '/admin/resources', '/admin/newsletters']
const CONTRIBUTOR_OR_ADMIN = ['/editor', '/admin/content', '/admin/events']

async function accountLinks(page: import('@playwright/test').Page): Promise<string[]> {
  await page.getByTestId('user-menu-trigger').click()
  return await page.getByTestId('nav-account-link').evaluateAll(
    (nodes) => nodes.map((n) => n.getAttribute('data-to') ?? ''),
  )
}

test.describe('member', () => {
  test.use({ persona: 'member' })

  for (const path of [...ADMIN_ONLY, ...CONTRIBUTOR_OR_ADMIN]) {
    test(`is bounced home from ${path}`, async ({ page }) => {
      await page.goto(path)
      await expect(page).toHaveURL(`/?forbidden=${path}`)
    })
  }

  test('is offered only the dashboard', async ({ page }) => {
    await page.goto('/')
    expect(await accountLinks(page)).toEqual(['/dashboard'])
  })

  test('can still reach the dashboard', async ({ page }) => {
    await page.goto('/dashboard')
    await expect(page).toHaveURL('/dashboard')
  })
})

test.describe('contributor', () => {
  test.use({ persona: 'contributor' })

  for (const path of CONTRIBUTOR_OR_ADMIN) {
    test(`can open ${path}`, async ({ page }) => {
      await page.goto(path)
      await expect(page).toHaveURL(path)
    })
  }

  for (const path of ADMIN_ONLY) {
    test(`is bounced home from ${path}`, async ({ page }) => {
      await page.goto(path)
      await expect(page).toHaveURL(`/?forbidden=${path}`)
    })
  }

  test('is offered the contributor links and no admin-only ones', async ({ page }) => {
    await page.goto('/')
    const links = await accountLinks(page)

    expect(links).toEqual(expect.arrayContaining(['/dashboard', ...CONTRIBUTOR_OR_ADMIN]))
    for (const adminOnly of ADMIN_ONLY) {
      expect(links).not.toContain(adminOnly)
    }
  })
})

test.describe('admin', () => {
  test.use({ persona: 'admin' })

  for (const path of [...ADMIN_ONLY, ...CONTRIBUTOR_OR_ADMIN, '/dashboard']) {
    test(`can open ${path}`, async ({ page }) => {
      await page.goto(path)
      await expect(page).toHaveURL(path)
    })
  }

  test('is offered every account link', async ({ page }) => {
    await page.goto('/')
    expect(await accountLinks(page)).toEqual(
      expect.arrayContaining(['/dashboard', ...CONTRIBUTOR_OR_ADMIN, ...ADMIN_ONLY]),
    )
  })

  test('/admin is not a route and renders the not-found view', async ({ page }) => {
    // The route table has no bare /admin, so it falls through to the catch-all.
    // The spec this replaced asserted `toHaveURL(/\/admin$/)`, which passes
    // against the 404 page and therefore proved nothing.
    await page.goto('/admin')
    await expect(page.getByTestId('not-found')).toBeVisible()
  })
})
