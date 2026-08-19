import { expect, test } from '../support/fixtures'
import { PIXEL_PNG, createDraft, expectDraftBodyToContain } from '../support/data'
import { clickToolbar, openDraftRow, typeInRichText } from '../helpers'
import { titleFor } from '../support/ids'

/**
 * The TipTap surface, asserted through the HTML that reaches storage rather
 * than through ProseMirror's DOM. That matters because the server sanitises on
 * write (HtmlSanitizer allows only the tags this toolbar can produce), so
 * "the editor rendered bold" and "bold survived the round trip" are different
 * claims and only the second one is useful.
 */
test.describe('rich text editor', () => {
  test.use({ persona: 'contributor2' })

  async function openDraft(page: import('@playwright/test').Page, uid: string) {
    await page.goto('/editor')
    await openDraftRow(page, uid)
  }

  test('bold and italic survive the round trip to storage', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Formatting'), body: '' })
    await openDraft(page, uid)

    await page.getByTestId('rte-content').locator('.ProseMirror').click()
    await clickToolbar(page, 'bold')
    await page.keyboard.type('Bold text')
    await clickToolbar(page, 'bold', false) // toggle off
    await clickToolbar(page, 'italic')
    await page.keyboard.type(' and italic text')

    await expectDraftBodyToContain(api, draft.id, '<strong>Bold text</strong>')
    await expectDraftBodyToContain(api, draft.id, '<em> and italic text</em>')
  })

  test('lists and headings survive the round trip', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Structure'), body: '' })
    await openDraft(page, uid)

    await page.getByTestId('rte-content').locator('.ProseMirror').click()
    await clickToolbar(page, 'h2')
    await page.keyboard.type('A heading')
    await page.keyboard.press('Enter')
    await clickToolbar(page, 'ul')
    await page.keyboard.type('First bullet')

    await expectDraftBodyToContain(api, draft.id, '<h2>A heading</h2>')
    await expectDraftBodyToContain(api, draft.id, '<ul>')
    await expectDraftBodyToContain(api, draft.id, 'First bullet')
  })

  test('a link can be inserted and is kept by the sanitiser', async ({ page, api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Linking'), body: '' })
    await openDraft(page, uid)

    await typeInRichText(page, 'Read more here')
    // ControlOrMeta, not Control: select-all is Cmd+A on macOS.
    await page.keyboard.press('ControlOrMeta+A')
    await page.getByTestId('rte-btn').and(page.locator('[data-cmd="link"]')).click()

    await expect(page.getByTestId('rte-link-dialog')).toBeVisible()
    await page.locator('#link-url').fill('https://www.parkinsons.org.uk/')
    await page.getByTestId('rte-link-apply').click()

    await expectDraftBodyToContain(api, draft.id, 'href="https://www.parkinsons.org.uk/"')
  })

  test('an image uploads to blob storage and is embedded in the draft', async ({ page, api, cleanup, uid }) => {
    test.slow() // multipart upload + blob round trip through Azurite

    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Image upload'), body: '' })
    await openDraft(page, uid)

    const [uploadResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/media') && r.request().method() === 'POST'),
      page.getByTestId('rte-image-input').setInputFiles({
        name: 'pixel.png', mimeType: 'image/png', buffer: PIXEL_PNG,
      }),
    ])
    expect(uploadResponse.ok(), `media upload returned ${uploadResponse.status()}`).toBeTruthy()

    const image = page.getByTestId('rte-content').locator('img')
    await expect(image).toBeVisible()
    // The SAS URL must point at the emulator's blob endpoint, and the browser
    // must actually be able to fetch it — a wrong host or an unsigned URL both
    // leave a broken image that a src-only assertion would miss.
    await expect(image).toHaveAttribute('src', /127\.0\.0\.1:10000\/devstoreaccount1\/media\//)
    expect(await image.evaluate((el: HTMLImageElement) => el.naturalWidth)).toBeGreaterThan(0)

    await expectDraftBodyToContain(api, draft.id, '<img')
  })

  test('a disallowed file type is rejected with a visible error', async ({ page, api, cleanup, uid }) => {
    await createDraft(api, cleanup, { title: titleFor(uid, 'Bad upload'), body: '' })
    await openDraft(page, uid)

    // BlobService allows png/jpeg/webp only.
    await page.getByTestId('rte-image-input').setInputFiles({
      name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('not an image'),
    })

    await expect(page.getByTestId('draft-upload-error')).toBeVisible()
    await expect(page.getByTestId('rte-content').locator('img')).toHaveCount(0)
  })
})
