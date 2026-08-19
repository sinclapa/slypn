import { readFileSync } from 'node:fs'
import { expect, test } from '../support/fixtures'
import { createNewsletter, uploadNewsletterFile, type NewsletterLike } from '../support/data'
import { SEED_DOCX } from '../support/backend'
import { gotoFresh, withConfirm } from '../helpers'
import { titleFor } from '../support/ids'

/**
 * Newsletter issues, including the file round trip: a DOCX or PDF goes to blob
 * storage through a multipart PUT and comes back out through
 * `GET /newsletters/{id}/file`, which NewsletterDetailView renders with
 * docx-preview or an embedded PDF depending on the content type.
 *
 * The preview is the part unit tests cannot reach — it needs a real file, a
 * real blob store, and a real browser.
 */

const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'

/** A structurally valid one-page PDF, small enough to inline. */
const SAMPLE_PDF = Buffer.from(
  '%PDF-1.4\n' +
  '1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n' +
  '2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
  '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n' +
  'trailer<</Root 1 0 R>>\n' +
  '%%EOF\n',
  'latin1',
)

test.describe('newsletter management', () => {
  test.use({ persona: 'admin' })

  test('creating an issue publishes it to the public newsletter page',
    async ({ page, api, cleanup, uid }) => {
      const title = titleFor(uid, 'Monthly issue')

      await page.goto('/admin/newsletters')
      await page.getByTestId('newsletter-add').click()
      await expect(page.getByTestId('newsletter-dialog')).toBeVisible()

      await page.locator('#newsletter-title').fill(title)
      await page.locator('#newsletter-issue-date').fill(new Date().toISOString().slice(0, 10))
      await page.locator('#newsletter-summary').fill('An issue created by the e2e suite.')
      await page.locator('#newsletter-topics').fill('Research, Events')

      const [response] = await Promise.all([
        page.waitForResponse((r) => r.url().endsWith('/api/newsletters') && r.request().method() === 'POST'),
        page.getByTestId('newsletter-save').click(),
      ])
      const created = await response.json() as NewsletterLike
      cleanup(async () => { await api.del(`/newsletters/${created.id}`) })

      await expect(page.getByTestId('newsletter-row').filter({ hasText: uid })).toBeVisible()

      const newsletters = await api.json<(NewsletterLike & { topics: string[] })[]>('/newsletters')
      const saved = newsletters.find((n) => n.id === created.id)
      expect(saved?.topics).toEqual(['Research', 'Events'])

      await gotoFresh(page, '/newsletter')
      await expect(page.getByText(title)).toBeVisible()
    })

  test('a DOCX issue uploads and renders in the preview', async ({ page, api, cleanup, uid }) => {
    test.slow() // multipart upload plus a full docx-preview render

    const newsletter = await createNewsletter(api, cleanup, { title: titleFor(uid, 'DOCX issue') })

    await page.goto('/admin/newsletters')
    await page.getByTestId('newsletter-row').filter({ hasText: uid }).getByTestId('newsletter-edit').click()

    const [response] = await Promise.all([
      page.waitForResponse((r) =>
        r.url().includes(`/api/newsletters/${newsletter.id}/file`) && r.request().method() === 'PUT'),
      (async () => {
        await page.locator('#newsletter-file').setInputFiles(SEED_DOCX)
        await page.getByTestId('newsletter-save').click()
      })(),
    ])
    expect(response.ok(), `file upload returned ${response.status()}`).toBeTruthy()

    const stored = await api.json<NewsletterLike[]>('/newsletters')
    expect(stored.find((n) => n.id === newsletter.id)?.fileName).toMatch(/\.docx$/i)

    await gotoFresh(page, `/newsletter/${newsletter.id}`)
    const preview = page.getByTestId('newsletter-docx')
    await expect(preview).toBeVisible({ timeout: 30_000 })
    // docx-preview builds a `.docx-wrapper` subtree; an empty container would
    // mean the fetch succeeded but the render silently failed.
    await expect(preview.locator('.docx-wrapper')).toBeVisible({ timeout: 30_000 })
  })

  test('a PDF issue renders in the embedded viewer instead', async ({ page, api, cleanup, uid }) => {
    test.slow()

    const newsletter = await createNewsletter(api, cleanup, { title: titleFor(uid, 'PDF issue') })
    await uploadNewsletterFile(api, newsletter.id, {
      name: `e2e-${uid}.pdf`, mimeType: 'application/pdf', buffer: SAMPLE_PDF,
    })

    await gotoFresh(page, `/newsletter/${newsletter.id}`)

    const pdf = page.getByTestId('newsletter-pdf')
    await expect(pdf).toBeVisible({ timeout: 30_000 })
    // Only the element and its object URL: the rendered PDF lives in Chromium's
    // internal viewer, which Playwright cannot see into.
    await expect(pdf).toHaveAttribute('src', /^blob:/)
    await expect(page.getByTestId('newsletter-docx')).toBeHidden()
  })

  test('the download link serves the stored bytes', async ({ page, api, cleanup, uid }) => {
    const newsletter = await createNewsletter(api, cleanup, { title: titleFor(uid, 'Downloadable') })
    const docx = readFileSync(SEED_DOCX)
    await uploadNewsletterFile(api, newsletter.id, {
      name: `e2e-${uid}.docx`, mimeType: DOCX_MIME, buffer: docx,
    })

    const file = await page.request.get(`http://localhost:7071/api/newsletters/${newsletter.id}/file`)
    expect(file.ok()).toBeTruthy()
    expect(file.headers()['content-type']).toContain('wordprocessingml')
    expect(await file.body()).toHaveLength(docx.length)
  })

  test('deleting an issue removes it from the public page', async ({ page, api, cleanup, uid }) => {
    const newsletter = await createNewsletter(api, cleanup, { title: titleFor(uid, 'Retired issue') })

    await page.goto('/admin/newsletters')
    const row = page.getByTestId('newsletter-row').filter({ hasText: uid })

    await withConfirm(page, /Delete "/, 'accept', async () => {
      await row.getByTestId('newsletter-delete').click()
    })

    await expect(page.getByTestId('newsletter-row').filter({ hasText: uid })).toHaveCount(0)
    const newsletters = await api.json<NewsletterLike[]>('/newsletters')
    expect(newsletters.map((n) => n.id)).not.toContain(newsletter.id)
  })
})

test.describe('newsletter files are admin-only', () => {
  test.use({ persona: 'contributor' })

  test('a contributor cannot upload an issue file', async ({ api, adminApi, cleanup, uid }) => {
    const newsletter = await createNewsletter(adminApi, cleanup, { title: titleFor(uid, 'Protected') })

    const resp = await api.raw.put(api.resolve(`/newsletters/${newsletter.id}/file`), {
      multipart: { file: { name: 'x.pdf', mimeType: 'application/pdf', buffer: SAMPLE_PDF } },
      failOnStatusCode: false,
    })
    expect(resp.status()).toBe(403)
  })
})
