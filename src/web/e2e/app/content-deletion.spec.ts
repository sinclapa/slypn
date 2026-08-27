import { expect, primePage, test } from '../support/fixtures'
import { publishAuthoredArticle, type ArticleLike } from '../support/data'
import { gotoFresh, withConfirm } from '../helpers'
import { titleFor } from '../support/ids'
import type { Page } from '@playwright/test'

/**
 * Deleting published content is a two-step, two-role flow: a contributor can
 * only REQUEST deletion, and the item stays live until an admin approves. The
 * distinction is easy to regress into "contributor deletes immediately", so
 * both the request and each admin outcome are covered.
 */
test.describe('deletion requests', () => {
  test.use({ persona: 'contributor2' })

  async function openApprovals(page: Page): Promise<void> {
    await primePage(page, 'admin2')
    await page.goto('/admin/approvals')
  }

  test('a contributor requests deletion instead of deleting', async ({ page, api, adminApi, cleanup, uid }) => {
    const article = await publishAuthoredArticle(api, adminApi, cleanup, {
      title: titleFor(uid, 'Please delete me'),
    })

    await page.goto('/admin/content')
    await page.getByTestId('published-search').fill(uid)
    const row = page.getByTestId('published-item').filter({ hasText: uid })

    // Contributors get the "request" wording; admins get "Delete".
    await expect(row.getByTestId('published-delete')).toHaveText('Request deletion')

    await withConfirm(page, /Request deletion of/, 'accept', async () => {
      await row.getByTestId('published-delete').click()
    })

    await expect(row.getByTestId('published-badge-deletion')).toBeVisible()

    // Still live, and now flagged for an admin.
    const published = await adminApi.json<(ArticleLike & { deletionRequestedBy?: string })[]>(
      '/articles?status=published')
    const flagged = published.find((a) => a.id === article.id)
    expect(flagged, 'the article must stay published until approved').toBeDefined()
    expect(flagged?.deletionRequestedBy).toBeTruthy()
  })

  test('cancelling the confirm dialog leaves the article untouched', async ({ page, api, adminApi, cleanup, uid }) => {
    const article = await publishAuthoredArticle(api, adminApi, cleanup, {
      title: titleFor(uid, 'Keep me as is'),
    })

    await page.goto('/admin/content')
    await page.getByTestId('published-search').fill(uid)
    const row = page.getByTestId('published-item').filter({ hasText: uid })

    await withConfirm(page, /Request deletion of/, 'dismiss', async () => {
      await row.getByTestId('published-delete').click()
    })

    await expect(row.getByTestId('published-badge-deletion')).toHaveCount(0)
    const published = await adminApi.json<(ArticleLike & { deletionRequestedBy?: string })[]>(
      '/articles?status=published')
    expect(published.find((a) => a.id === article.id)?.deletionRequestedBy).toBeFalsy()
  })

  test('an admin can keep the article, clearing the request', async ({ page, browser, api, adminApi, cleanup, uid }) => {
    const article = await publishAuthoredArticle(api, adminApi, cleanup, {
      title: titleFor(uid, 'Admin keeps this'),
    })
    await api.post(`/content/${article.id}/request-deletion`)

    const context = await browser.newContext()
    const adminPage = await context.newPage()
    await openApprovals(adminPage)

    const item = adminPage.getByTestId('deletion-item').filter({ hasText: uid })
    await expect(item).toBeVisible()
    await item.getByTestId('deletion-keep').click()
    await expect(adminPage.getByTestId('deletion-item').filter({ hasText: uid })).toHaveCount(0)
    await context.close()

    const published = await adminApi.json<(ArticleLike & { deletionRequestedBy?: string })[]>(
      '/articles?status=published')
    const kept = published.find((a) => a.id === article.id)
    expect(kept?.deletionRequestedBy).toBeFalsy()

    await gotoFresh(page, '/articles')
    await expect(page.getByTestId('article-card').filter({ hasText: uid })).toBeVisible()
  })

  test('an admin can approve the deletion, removing it from the public site',
    async ({ page, browser, api, adminApi, cleanup, uid }) => {
      const article = await publishAuthoredArticle(api, adminApi, cleanup, {
        title: titleFor(uid, 'Admin deletes this'),
      })
      await api.post(`/content/${article.id}/request-deletion`)

      const context = await browser.newContext()
      const adminPage = await context.newPage()
      await openApprovals(adminPage)

      await adminPage.getByTestId('deletion-item').filter({ hasText: uid })
        .getByTestId('deletion-approve').click()
      await expect(adminPage.getByTestId('deletion-item').filter({ hasText: uid })).toHaveCount(0)
      await context.close()

      const published = await adminApi.json<ArticleLike[]>('/articles?status=published')
      expect(published.map((a) => a.id)).not.toContain(article.id)

      await gotoFresh(page, '/articles')
      await expect(page.getByTestId('article-card').filter({ hasText: uid })).toHaveCount(0)
    })
})
