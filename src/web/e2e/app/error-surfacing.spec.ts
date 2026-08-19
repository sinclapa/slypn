import { expect, test } from '../support/fixtures'
import { createDraft } from '../support/data'
import { openDraftRow } from '../helpers'
import { titleFor } from '../support/ids'

/**
 * What the user sees when the API fails.
 *
 * This is the one spec that intercepts network traffic instead of hitting the
 * live API, because server errors are the thing under test and they cannot be
 * provoked reliably from a healthy backend. Everything else in the suite talks
 * to the real thing.
 *
 * It matters because every one of these views degrades quietly by default: a
 * failed fetch leaves an empty list, which looks exactly like "no content yet".
 */
test.describe('API failures reach the user', () => {
  test.use({ persona: 'admin' })

  test('the articles page shows an error and can retry', async ({ page }) => {
    // A URL predicate, not a glob: Playwright treats `?` in a glob as a
    // single-character wildcard, so `articles?status=published` is ambiguous.
    //
    // The flag is flipped explicitly rather than after the first request: an
    // admin's page load also fetches the approvals counts and the category
    // hints, so more than one component asks for this URL and a
    // fail-once handler would leave a later success to overwrite the error.
    let failing = true
    await page.route(
      (url) => url.pathname === '/api/articles' && url.searchParams.get('status') === 'published',
      async (route) => {
        if (failing) await route.fulfill({ status: 500, body: 'boom' })
        else await route.continue()
      },
    )

    await page.goto('/articles')
    await expect(page.getByText(/Couldn.t load articles/)).toBeVisible()

    // The retry must actually re-fetch, not just clear the message.
    failing = false
    await page.getByRole('button', { name: 'Retry' }).click()
    await expect(page.getByText(/Couldn.t load articles/)).toHaveCount(0)
    await expect(page.getByTestId('article-card').first()).toBeVisible()
  })

  test('a failed autosave is reported instead of silently losing the edit',
    async ({ page, api, cleanup, uid }) => {
      await createDraft(api, cleanup, { title: titleFor(uid, 'Save failure') })

      await page.goto('/editor')
      await openDraftRow(page, uid)

      await page.route('**/api/drafts/**', async (route) => {
        if (route.request().method() === 'PUT') {
          await route.fulfill({ status: 500, body: 'storage unavailable' })
          return
        }
        await route.continue()
      })

      await page.locator('#draft-summary').fill('An edit that cannot be saved.')

      await expect(page.getByTestId('save-indicator')).toHaveAttribute('data-status', 'error')
      await expect(page.getByTestId('save-indicator')).toContainText('Save failed')
    })

  test('the approvals queue reports a load failure', async ({ page }) => {
    await page.route(
      (url) => url.pathname === '/api/articles' && url.searchParams.get('status') === 'in-review',
      (route) => route.fulfill({ status: 500, body: 'boom' }),
    )

    await page.goto('/admin/approvals')

    await expect(page.getByText(/\/articles: 500/)).toBeVisible()
    await expect(page.getByTestId('approvals-empty')).toHaveCount(0)
  })

  test('member management reports a load failure and can retry', async ({ page }) => {
    let failing = true
    await page.route(
      (url) => url.pathname === '/api/members',
      async (route) => {
        if (failing) await route.fulfill({ status: 500, body: 'boom' })
        else await route.continue()
      },
    )

    await page.goto('/admin/members')
    await expect(page.getByText(/Couldn.t load members/)).toBeVisible()

    failing = false
    await page.getByRole('button', { name: 'Retry' }).click()
    await expect(page.getByTestId('member-row').first()).toBeVisible()
  })

  test('a rejected publish surfaces the API message on the queue item',
    async ({ page }) => {
      await page.route(
        (url) => url.pathname.endsWith('/publish'),
        (route) => route.fulfill({ status: 409, body: 'The article was modified by someone else.' }),
      )

      // Give the queue something to act on without depending on other specs.
      await page.route(
        (url) => url.pathname === '/api/articles' && url.searchParams.get('status') === 'in-review',
        (route) => route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([{
          id: 'e2e-fake-1', slug: 'e2e-fake-1', title: 'E2E queue failure fixture',
          summary: 'Publishing this will fail.', body: '<p>x</p>', author: 'E2E',
          publishedAt: new Date().toISOString(), category: 'Community',
          status: 'in-review', readingMinutes: 1, type: 'article',
        }]),
      }))

      await page.goto('/admin/approvals')
      const item = page.getByTestId('approvals-item').filter({ hasText: 'queue failure fixture' })
      await expect(item).toBeVisible()

      await item.getByTestId('approvals-approve').click()

      await expect(item.getByTestId('approvals-item-error'))
        .toContainText('The article was modified by someone else.')
      // Still in the queue: a failed publish must not look like a success.
      await expect(item).toBeVisible()
    })
})
