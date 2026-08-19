import { expect, test } from '../support/fixtures'
import { createDraft, draftId, expectDraftFieldToBe, submitDraft } from '../support/data'
import { openDraftRow, typeInRichText, withConfirm } from '../helpers'
import { titleFor } from '../support/ids'

/**
 * Draft editing mechanics: autosave, the submit gate, deletion, and the
 * optimistic-concurrency conflict UI.
 *
 * The conflict tests are the reason this spec exists. `PUT /api/drafts/{id}`
 * uses If-Match and answers 412 when the ETag has moved on; DraftEditor turns
 * that into a choose-a-side banner. Nothing else in the suite exercises it, and
 * a silent regression there means data loss.
 */
test.describe('draft editing', () => {
  test.use({ persona: 'contributor2' })

  test('autosave reports its progress and persists the text', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Autosave') })

    await page.goto('/editor')
    await openDraftRow(page, uid)

    await page.locator('#draft-summary').fill(`Autosaved summary ${uid}.`)

    // The indicator is a UI assertion; the poll below is the data assertion.
    await expect(page.getByTestId('save-indicator')).toHaveAttribute('data-status', 'saved')
    await expectDraftFieldToBe(api, draft.id, 'summary', `Autosaved summary ${uid}.`)
  })

  test('an untitled draft is not saved and says so', async ({ page, api, cleanup }) => {
    const id = draftId()
    await api.put(`/drafts/${id}`, {
      type: 'article', title: '', slug: '', summary: '', body: '', category: '', readingMinutes: 1,
    })
    cleanup(async () => { await api.del(`/drafts/${id}`) })

    await page.goto('/editor')
    await page.getByTestId('draft-row').filter({ hasText: '(untitled)' }).first().click()

    await expect(page.getByText('Add a title to start saving.')).toBeVisible()
  })

  test('submit stays blocked until title, summary and body are all present',
    async ({ page, api, cleanup, uid }) => {
      const id = draftId()
      await api.put(`/drafts/${id}`, {
        type: 'article', title: titleFor(uid, 'Incomplete'), slug: '',
        summary: '', body: '', category: '', readingMinutes: 1,
      })
      cleanup(async () => { await api.del(`/drafts/${id}`) })

      await page.goto('/editor')
      await openDraftRow(page, uid)

      await expect(page.getByTestId('draft-submit')).toBeDisabled()
      await expect(page.getByTestId('draft-submit-missing')).toContainText('summary')
      await expect(page.getByTestId('draft-submit-missing')).toContainText('content')

      await page.locator('#draft-summary').fill('Now it has a summary.')
      await expect(page.getByTestId('draft-submit')).toBeDisabled()

      await typeInRichText(page, 'And now it has a body.')
      await expect(page.getByTestId('draft-submit')).toBeEnabled()
    })

  test('deleting a draft asks first, then removes it', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Delete me') })

    await page.goto('/editor')
    const row = page.getByTestId('draft-row').filter({ hasText: uid })

    await withConfirm(page, /Delete this draft/, 'accept', async () => {
      await row.getByTestId('draft-row-delete').click()
    })

    await expect(page.getByTestId('draft-row').filter({ hasText: uid })).toHaveCount(0)
    expect((await api.get(`/drafts/${draft.id}`)).status()).toBe(404)
  })

  test('cancelling the delete confirm keeps the draft', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Survives cancel') })

    await page.goto('/editor')
    const row = page.getByTestId('draft-row').filter({ hasText: uid })

    await withConfirm(page, /Delete this draft/, 'dismiss', async () => {
      await row.getByTestId('draft-row-delete').click()
    })

    await expect(row).toBeVisible()
    expect((await api.get(`/drafts/${draft.id}`)).ok()).toBeTruthy()
  })

  test('a concurrent edit raises a conflict the author can resolve by taking the server copy',
    async ({ page, api, cleanup, uid }) => {
      const draft = await createDraft(api, cleanup, {
        title: titleFor(uid, 'Conflict discard'),
        summary: 'Original summary.',
      })

      await page.goto('/editor')
      await page.getByTestId('draft-row').filter({ hasText: uid }).click()
      await expect(page.getByTestId('draft-editor')).toBeVisible()

      // Someone else saves first, so the ETag the page is holding goes stale.
      await api.put(`/drafts/${draft.id}`, {
        type: 'article', title: titleFor(uid, 'Conflict discard'), slug: '',
        summary: 'Summary written by the other session.',
        body: '<p>Body written by the other session.</p>',
        category: 'Community', readingMinutes: 1,
      })

      await page.locator('#draft-summary').fill('Summary written in the browser.')
      await expect(page.getByTestId('draft-conflict')).toBeVisible()

      await page.getByTestId('draft-conflict-discard').click()
      await expect(page.getByTestId('draft-conflict')).toHaveCount(0)
      await expect(page.locator('#draft-summary')).toHaveValue('Summary written by the other session.')
    })

  test('...or by overwriting the server with their own copy', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, {
      title: titleFor(uid, 'Conflict overwrite'),
      summary: 'Original summary.',
    })

    await page.goto('/editor')
    await openDraftRow(page, uid)

    await api.put(`/drafts/${draft.id}`, {
      type: 'article', title: titleFor(uid, 'Conflict overwrite'), slug: '',
      summary: 'Summary written by the other session.',
      body: '<p>Body written by the other session.</p>',
      category: 'Community', readingMinutes: 1,
    })

    await page.locator('#draft-summary').fill('Summary written in the browser.')
    await expect(page.getByTestId('draft-conflict')).toBeVisible()

    await page.getByTestId('draft-conflict-overwrite').click()
    await expect(page.getByTestId('draft-conflict')).toHaveCount(0)

    // Assert the server, not the indicator: resolveByForcingLocal() calls
    // save() directly rather than through useAutoSave, so the indicator keeps
    // the 'error' status left by the 412 until the next keystroke. Cosmetic,
    // but it means the badge is not evidence either way here.
    await expectDraftFieldToBe(api, draft.id, 'summary', 'Summary written in the browser.')
  })

  test('an in-review submission opens read-only', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Locked in review') })
    await submitDraft(api, cleanup, draft.id)

    await page.goto('/editor')
    const row = page.getByTestId('draft-row').filter({ hasText: uid })
    await expect(row).toHaveAttribute('data-state', 'in-review')
    await row.click()

    await expect(page.getByTestId('draft-readonly-badge')).toBeVisible()
    await expect(page.locator('#draft-title')).toHaveAttribute('readonly', '')
    await expect(page.getByTestId('rte-toolbar')).toHaveCount(0)
  })
})
