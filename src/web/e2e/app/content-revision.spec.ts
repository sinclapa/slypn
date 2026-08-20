import { expect, primePage, test } from '../support/fixtures'
import {
  createDraft,
  expectDraftBodyToContain,
  publishAuthoredArticle,
  submitDraft,
  type ArticleLike,
} from '../support/data'
import { gotoFresh, openDraftRow, typeInRichText, waitForDraftLoaded } from '../helpers'
import { titleFor } from '../support/ids'
import type { Page } from '@playwright/test'

/**
 * The two ways published content changes after approval: an admin sending a
 * submission back for revision, and a contributor editing something already
 * live (which spawns a revision draft rather than mutating the live copy).
 *
 * Runs as contributor2/admin2 so it never shares a drafts partition — drafts are
 * partitioned by authorId — with content-lifecycle.spec.ts running in parallel.
 */
test.describe('revision workflow', () => {
  test.use({ persona: 'contributor2' })

  async function asAdmin(page: Page): Promise<void> {
    await primePage(page, 'admin2')
  }

  test('an admin sends a submission back with feedback', async ({ browser, api, adminApi, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Needs work') })
    await submitDraft(api, cleanup, draft.id)

    const adminContext = await browser.newContext()
    const adminPage = await adminContext.newPage()
    await asAdmin(adminPage)

    await adminPage.goto('/admin/approvals')
    const item = adminPage.getByTestId('approvals-item').filter({ hasText: uid })
    await expect(item).toBeVisible()
    await item.getByTestId('approvals-revise').click()

    await expect(adminPage.getByTestId('revise-dialog')).toBeVisible()
    await adminPage.locator('#revise-feedback').fill('Please add detail on medication timing.')
    await adminPage.getByTestId('revise-submit').click()

    await expect(adminPage.getByTestId('approvals-item').filter({ hasText: uid })).toHaveCount(0)
    await adminContext.close()

    // Back with the author as an editable draft carrying the feedback.
    const inReview = await adminApi.json<ArticleLike[]>('/review/articles')
    expect(inReview.map((a) => a.id)).not.toContain(draft.id)

    const drafts = await api.json<{ id: string; revisionFeedback?: string }[]>('/drafts')
    const returned = drafts.find((d) => d.id === draft.id)
    expect(returned?.revisionFeedback).toBe('Please add detail on medication timing.')
  })

  test('the author sees the feedback on the returned draft', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Returned draft') })
    await api.put(`/drafts/${draft.id}`, {
      type: 'article',
      title: titleFor(uid, 'Returned draft'),
      slug: '',
      summary: 'Summary for the returned draft.',
      body: '<p>Body for the returned draft.</p>',
      category: 'Community',
      readingMinutes: 1,
      revisionFeedback: 'Tighten the opening paragraph.',
    })

    await page.goto('/editor')
    await openDraftRow(page, uid)

    await expect(page.getByTestId('draft-revision-feedback'))
      .toContainText('Tighten the opening paragraph.')
  })

  test('short feedback cannot be submitted', async ({ browser, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Short feedback') })
    await submitDraft(api, cleanup, draft.id)

    const context = await browser.newContext()
    const page = await context.newPage()
    await asAdmin(page)

    await page.goto('/admin/approvals')
    await page.getByTestId('approvals-item').filter({ hasText: uid })
      .getByTestId('approvals-revise').click()

    await expect(page.getByTestId('revise-submit')).toBeDisabled()
    await page.locator('#revise-feedback').fill('no')
    await expect(page.getByTestId('revise-submit')).toBeDisabled()
    await page.locator('#revise-feedback').fill('needs more depth')
    await expect(page.getByTestId('revise-submit')).toBeEnabled()

    await context.close()
  })

  test('editing published content creates a revision, and approving it updates the live copy',
    async ({ page, browser, api, adminApi, cleanup, uid }) => {
      const original = await publishAuthoredArticle(api, adminApi, cleanup, {
        title: titleFor(uid, 'Live article'),
        body: '<p>The original body.</p>',
      })

      await page.goto('/admin/content')
      await page.getByTestId('published-search').fill(uid)
      const row = page.getByTestId('published-item').filter({ hasText: uid })
      await expect(row).toBeVisible()

      const [editResponse] = await Promise.all([
        page.waitForResponse((r) =>
          r.url().includes(`/api/articles/${original.id}/edit`) && r.request().method() === 'POST'),
        row.getByTestId('published-edit').click(),
      ])
      const revisionDraft = await editResponse.json() as { id: string; replacesArticleId: string }
      expect(revisionDraft.replacesArticleId).toBe(original.id)
      cleanup(async () => { await api.del(`/drafts/${revisionDraft.id}`) })

      // The dialog mounts DraftEditor immediately but its content arrives a
      // fetch later; typing before that lands is silently discarded.
      await waitForDraftLoaded(page, uid)
      await typeInRichText(page, 'Revised opening. ')
      // Submit sends whatever the server holds, so the edit must be there first.
      await expectDraftBodyToContain(api, revisionDraft.id, 'Revised opening.')
      await page.getByTestId('draft-submit').click()

      // Back on the list, the live item is flagged as having a pending revision.
      await page.getByTestId('published-search').fill(uid)
      await expect(
        page.getByTestId('published-item').filter({ hasText: uid })
          .getByTestId('published-badge-revision'),
      ).toBeVisible()

      // The admin sees it labelled as a revision, not as new content.
      const adminContext = await browser.newContext()
      const adminPage = await adminContext.newPage()
      await asAdmin(adminPage)
      await adminPage.goto('/admin/approvals')

      const queued = adminPage.getByTestId('approvals-item').filter({ hasText: uid })
      await expect(queued).toContainText('Revision')
      await queued.getByTestId('approvals-approve').click()
      await expect(adminPage.getByTestId('approvals-item').filter({ hasText: uid })).toHaveCount(0)
      await adminContext.close()

      // The public URL is unchanged; only the body moved on.
      const live = await adminApi.json<ArticleLike & { body: string }>(`/articles/${original.slug}`)
      expect(live.id).toBe(original.id)
      expect(live.slug).toBe(original.slug)
      expect(live.body).toContain('Revised opening.')

      await gotoFresh(page, `/articles/${original.slug}`)
      await expect(page.getByText('Revised opening.')).toBeVisible()
    })
})
