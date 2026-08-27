import { expect, primePage, test } from '../support/fixtures'
import { createApiClient, type ApiClient } from '../support/api-client'
import { gotoFresh, openDraftRow, typeInRichText } from '../helpers'
import { expectDraftBodyToContain, expectDraftFieldToBe } from '../support/data'
import { titleFor } from '../support/ids'

/**
 * The whole authoring workflow, driven entirely through the UI:
 *
 *   contributor writes a draft -> autosaves -> submits for review
 *   -> admin sees it in the approvals queue -> approves
 *   -> it appears on the public /articles page
 *
 * This is the one spec that refuses to take API shortcuts, because the
 * hand-offs between the two roles ARE the thing under test. Everything else in
 * the suite creates its prerequisites over HTTP instead.
 *
 * Serial: each step depends on the state the previous one left behind.
 */
test.describe.configure({ mode: 'serial' })

test.describe('content lifecycle', () => {
  test.use({ persona: 'contributor' })

  let draftId = ''
  let uid = ''
  let title = ''
  let admin: ApiClient

  test.beforeAll(async () => {
    uid = `lifecycle-${Date.now().toString(36)}`
    title = titleFor(uid, 'Lifecycle article')
    admin = await createApiClient('admin')
  })

  test.afterAll(async () => {
    // Belt and braces: the run may have failed part-way, so clear whichever
    // partition the item ended up in.
    for (const status of ['in-review', 'published', 'draft']) {
      await admin.del(`/content/${draftId}?status=${status}`)
    }
    await admin.dispose()
  })

  test('a contributor creates a draft', async ({ page, api }) => {
    await page.goto('/editor')

    await page.getByTestId('new-draft-open').click()
    await expect(page.getByTestId('new-draft-dialog')).toBeVisible()
    await page.locator('#new-draft-title').fill(title)
    await page.getByTestId('new-draft-type-article').click()
    await page.getByTestId('new-draft-submit').click()

    // The row is the source of truth for the generated id.
    const row = page.getByTestId('draft-row').filter({ hasText: uid })
    await expect(row).toBeVisible()
    draftId = (await row.getAttribute('data-id'))!
    expect(draftId).toBeTruthy()

    const drafts = await api.json<{ id: string; title: string }[]>('/drafts')
    expect(drafts.map((d) => d.id)).toContain(draftId)
  })

  test('typing autosaves the draft to the API', async ({ page, api }) => {
    await page.goto('/editor')
    await openDraftRow(page, uid)

    await page.locator('#draft-summary').fill(`Summary for ${uid}, written by the e2e suite.`)
    await page.locator('#draft-category').fill('Community')
    await typeInRichText(page, `Body paragraph for ${uid}. `)

    // The indicator is the UI-facing assertion; the polls below assert what
    // actually reached storage, which the indicator alone cannot prove (it
    // reports the last autosave cycle, not necessarily the last edit).
    await expect(page.getByTestId('save-indicator')).toHaveAttribute('data-status', 'saved')

    await expectDraftFieldToBe(api, draftId, 'summary', `Summary for ${uid}, written by the e2e suite.`)
    await expectDraftBodyToContain(api, draftId, uid)
    await expectDraftFieldToBe(api, draftId, 'category', 'Community')
  })

  test('the saved draft survives a reload', async ({ page }) => {
    await page.goto('/editor')
    await page.getByTestId('draft-row').filter({ hasText: uid }).click()

    await expect(page.locator('#draft-summary')).toHaveValue(new RegExp(uid))
    await expect(page.getByTestId('rte-content')).toContainText(uid)
  })

  test('submitting for review moves it out of drafts and into in-review', async ({ page, api }) => {
    await page.goto('/editor')
    await openDraftRow(page, uid)

    await expect(page.getByTestId('draft-submit')).toBeEnabled()
    await page.getByTestId('draft-submit').click()

    await expect(page.getByTestId('draft-submit-message')).toBeVisible()

    // Same row id, now read-only and badged "In review".
    const row = page.getByTestId('draft-row').filter({ hasText: uid })
    await expect(row).toHaveAttribute('data-state', 'in-review')
    await expect(row.getByTestId('draft-row-state')).toHaveText('In review')

    expect((await api.json<{ id: string }[]>('/drafts')).map((d) => d.id)).not.toContain(draftId)
    const inReview = await api.json<{ id: string; authorId: string }[]>('/review/articles')
    expect(inReview.map((a) => a.id)).toContain(draftId)
  })

  test('the author sees their submission read-only', async ({ page }) => {
    await page.goto('/editor')
    await page.getByTestId('draft-row').filter({ hasText: uid }).click()

    await expect(page.getByTestId('draft-readonly-badge')).toBeVisible()
    await expect(page.locator('#draft-title')).toHaveAttribute('readonly', '')
    await expect(page.getByTestId('draft-submit')).toHaveCount(0)
  })

  test('an admin approves it from the approvals queue', async ({ browser }) => {
    // A second context, because this step is a different person.
    const context = await browser.newContext()
    const page = await context.newPage()
    await primePage(page, 'admin')

    await page.goto('/admin/approvals')
    const item = page.getByTestId('approvals-item').filter({ hasText: uid })
    await expect(item).toBeVisible()

    await item.getByTestId('approvals-approve').click()
    await expect(page.getByTestId('approvals-item').filter({ hasText: uid })).toHaveCount(0)

    const published = await admin.json<{ id: string; slug: string }[]>('/articles?status=published')
    expect(published.map((a) => a.id)).toContain(draftId)

    await context.close()
  })

  test('the approved article is live on the public articles page', async ({ page }) => {
    const published = await admin.json<{ id: string; slug: string; title: string }[]>('/articles?status=published')
    const article = published.find((a) => a.id === draftId)!

    // Full reload: <keep-alive> caches ArticlesView, so an SPA navigation would
    // replay a list fetched before the approval.
    await gotoFresh(page, '/articles')
    await expect(page.getByTestId('article-card').filter({ hasText: uid })).toBeVisible()

    await gotoFresh(page, `/articles/${article.slug}`)
    await expect(page.getByRole('heading', { name: title })).toBeVisible()
    await expect(page.getByText(`Body paragraph for ${uid}.`)).toBeVisible()
  })
})
