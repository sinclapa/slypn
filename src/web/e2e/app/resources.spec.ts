import { expect, test } from '../support/fixtures'
import { createResource, type ResourceLike } from '../support/data'
import { gotoFresh, withConfirm } from '../helpers'
import { titleFor } from '../support/ids'

test.describe('resource management', () => {
  test.use({ persona: 'admin' })

  test('creating a resource publishes it to the public page', async ({ page, api, cleanup, uid }) => {
    const title = titleFor(uid, 'Helpful link')

    await page.goto('/admin/resources')
    await page.getByTestId('resource-add').click()
    await expect(page.getByTestId('resource-dialog')).toBeVisible()

    await page.locator('#resource-title').fill(title)
    await page.locator('#resource-description').fill('A resource created by the e2e suite.')
    await page.locator('#resource-url').fill('https://www.parkinsons.org.uk/')
    await page.locator('#resource-category').fill('Support')

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/resources') && r.request().method() === 'POST'),
      page.getByTestId('resource-save').click(),
    ])
    const created = await response.json() as ResourceLike
    cleanup(async () => { await api.del(`/resources/${created.id}?category=Support`) })

    await expect(page.getByTestId('resource-row').filter({ hasText: uid })).toBeVisible()

    const resources = await api.json<ResourceLike[]>('/resources')
    expect(resources.map((r) => r.id)).toContain(created.id)

    await gotoFresh(page, '/resources')
    await expect(page.getByTestId('resource-card').filter({ hasText: uid })).toBeVisible()
  })

  test('editing a resource updates it', async ({ page, api, cleanup, uid }) => {
    const resource = await createResource(api, cleanup, { title: titleFor(uid, 'Before edit') })

    await page.goto('/admin/resources')
    await page.getByTestId('resource-row').filter({ hasText: uid }).getByTestId('resource-edit').click()

    await page.locator('#resource-description').fill('Description rewritten by the e2e suite.')
    await Promise.all([
      page.waitForResponse((r) =>
        r.url().includes(`/api/resources/${resource.id}`) && r.request().method() === 'PUT'),
      page.getByTestId('resource-save').click(),
    ])

    const resources = await api.json<(ResourceLike & { description: string })[]>('/resources')
    expect(resources.find((r) => r.id === resource.id)?.description)
      .toBe('Description rewritten by the e2e suite.')
  })

  test('deleting a resource asks first, then removes it', async ({ page, api, cleanup, uid }) => {
    const resource = await createResource(api, cleanup, { title: titleFor(uid, 'Obsolete link') })

    await page.goto('/admin/resources')
    const row = page.getByTestId('resource-row').filter({ hasText: uid })

    await withConfirm(page, /Delete "/, 'accept', async () => {
      await row.getByTestId('resource-delete').click()
    })

    await expect(page.getByTestId('resource-row').filter({ hasText: uid })).toHaveCount(0)
    const resources = await api.json<ResourceLike[]>('/resources')
    expect(resources.map((r) => r.id)).not.toContain(resource.id)
  })

  test('a description shorter than the API allows surfaces the validation error',
    async ({ page, uid }) => {
      await page.goto('/admin/resources')
      await page.getByTestId('resource-add').click()

      // ResourceInput requires a description of at least 10 characters. The
      // browser's own `required` check passes, so the only thing standing
      // between the user and a silent failure is the API error being displayed.
      await page.locator('#resource-title').fill(titleFor(uid, 'Invalid'))
      await page.locator('#resource-description').fill('short')
      await page.locator('#resource-url').fill('https://www.parkinsons.org.uk/')
      await page.locator('#resource-category').fill('Support')
      await page.getByTestId('resource-save').click()

      await expect(page.getByTestId('resource-error')).toBeVisible()
      await expect(page.getByTestId('resource-dialog')).toBeVisible()
    })
})
