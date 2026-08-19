import { expect, test } from '../support/fixtures'
import { createEvent, type EventLike } from '../support/data'
import { gotoFresh, withConfirm } from '../helpers'
import { titleFor } from '../support/ids'

/** dd/mm/yyyy-free local value for a `datetime-local` input. */
function localDateTime(offsetDays: number, hour: number): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  d.setHours(hour, 0, 0, 0)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

test.describe('event management as an admin', () => {
  test.use({ persona: 'admin' })

  test('creating an event puts it in the admin list, the API, and the public page',
    async ({ page, api, cleanup, uid }) => {
      const title = titleFor(uid, 'Coffee morning')

      await page.goto('/admin/events')
      await page.getByTestId('event-add').click()
      await expect(page.getByTestId('event-dialog')).toBeVisible()

      await page.locator('#event-title').fill(title)
      await page.locator('#event-starts-at').fill(localDateTime(21, 10))
      await page.locator('#event-ends-at').fill(localDateTime(21, 12))
      await page.locator('#event-location').fill('E2E Test Cafe, London')
      await page.locator('#event-description').fill('An event created by the e2e suite.')

      const [response] = await Promise.all([
        page.waitForResponse((r) => r.url().endsWith('/api/events') && r.request().method() === 'POST'),
        page.getByTestId('event-save').click(),
      ])
      const created = await response.json() as EventLike
      cleanup(async () => { await api.del(`/events/${created.id}`) })

      await page.getByTestId('event-search').fill(uid)
      await expect(page.getByTestId('event-row').filter({ hasText: uid })).toBeVisible()

      const events = await api.json<EventLike[]>('/events')
      expect(events.find((e) => e.id === created.id)?.title).toBe(title)

      await gotoFresh(page, '/events')
      await expect(page.getByTestId('event-card').filter({ hasText: uid })).toBeVisible()
    })

  test('editing an event updates it', async ({ page, api, cleanup, uid }) => {
    const event = await createEvent(api, cleanup, { title: titleFor(uid, 'Before edit') })

    await page.goto('/admin/events')
    await page.getByTestId('event-search').fill(uid)
    await page.getByTestId('event-row').filter({ hasText: uid }).getByTestId('event-edit').click()

    await expect(page.getByTestId('event-dialog')).toBeVisible()
    await page.locator('#event-location').fill('Relocated by the e2e suite')
    await Promise.all([
      page.waitForResponse((r) => r.url().includes(`/api/events/${event.id}`) && r.request().method() === 'PUT'),
      page.getByTestId('event-save').click(),
    ])

    const updated = await api.json<{ location: string }>(`/events/${event.id}`)
    expect(updated.location).toBe('Relocated by the e2e suite')
  })

  test('deleting an event asks first, then removes it everywhere', async ({ page, api, cleanup, uid }) => {
    const event = await createEvent(api, cleanup, { title: titleFor(uid, 'Cancelled') })

    await page.goto('/admin/events')
    await page.getByTestId('event-search').fill(uid)
    const row = page.getByTestId('event-row').filter({ hasText: uid })

    await withConfirm(page, /Delete "/, 'accept', async () => {
      await row.getByTestId('event-delete').click()
    })

    await expect(page.getByTestId('event-row').filter({ hasText: uid })).toHaveCount(0)
    expect((await api.get(`/events/${event.id}`)).status()).toBe(404)
  })

  test('search and the all-dates toggle narrow the list', async ({ page, api, cleanup, uid }) => {
    await createEvent(api, cleanup, { title: titleFor(uid, 'Findable event') })

    await page.goto('/admin/events')
    await page.getByTestId('event-all-dates').check()

    await page.getByTestId('event-search').fill(uid)
    await expect(page.getByTestId('event-row')).toHaveCount(1)

    await page.getByTestId('event-search').fill('nothing-matches-this-query')
    await expect(page.getByTestId('event-row')).toHaveCount(0)
    await expect(page.getByText('No events match.')).toBeVisible()
  })

  test('a title shorter than the API allows surfaces the validation error', async ({ page, uid }) => {
    await page.goto('/admin/events')
    await page.getByTestId('event-add').click()

    // EventInput requires MinimumLength = 3; the form itself does not enforce it,
    // so this proves the API's 400 reaches the user rather than failing silently.
    await page.locator('#event-title').fill('ab')
    await page.locator('#event-starts-at').fill(localDateTime(30, 10))
    await page.locator('#event-ends-at').fill(localDateTime(30, 12))
    await page.locator('#event-location').fill(`E2E ${uid}`)
    await page.locator('#event-description').fill('Too short a title on purpose.')
    await page.getByTestId('event-save').click()

    await expect(page.getByTestId('event-error')).toBeVisible()
    await expect(page.getByTestId('event-dialog')).toBeVisible()
  })
})

test.describe('event management as a contributor', () => {
  test.use({ persona: 'contributor2' })

  test('sees and can manage their own event', async ({ page, api, cleanup, uid }) => {
    await createEvent(api, cleanup, { title: titleFor(uid, 'My own event') })

    await page.goto('/admin/events')
    await page.getByTestId('event-search').fill(uid)
    const row = page.getByTestId('event-row').filter({ hasText: uid })

    await expect(row.getByTestId('event-edit')).toBeVisible()
    await expect(row.getByTestId('event-delete')).toBeVisible()
  })

  test('cannot see or modify an event someone else created', async ({ page, api, adminApi, cleanup, uid }) => {
    const adminEvent = await createEvent(adminApi, cleanup, { title: titleFor(uid, 'Admin owned') })

    await page.goto('/admin/events')
    await page.getByTestId('event-all-dates').check()
    await page.getByTestId('event-search').fill(uid)

    // EventManagementView filters the list to `createdBy === auth.oid` for
    // non-admins, so another author's event is absent entirely rather than
    // present-but-read-only.
    await expect(page.getByTestId('event-row').filter({ hasText: uid })).toHaveCount(0)

    // It does exist, and it is visible to everyone on the public page — the
    // management list is scoped by ownership, not the data.
    expect((await api.get(`/events/${adminEvent.id}`)).ok()).toBeTruthy()

    // Hiding the row is not the security boundary; the API is.
    const forbiddenDelete = await api.del(`/events/${adminEvent.id}`)
    expect(forbiddenDelete.status()).toBe(403)
    expect(await forbiddenDelete.text()).toContain('You can only delete your own events.')

    const forbiddenUpdate = await api.put(`/events/${adminEvent.id}`, {
      title: titleFor(uid, 'Hijacked'),
      type: 'Coffee meet-up',
      startsAt: new Date(Date.now() + 86_400_000).toISOString(),
      endsAt: new Date(Date.now() + 90_000_000).toISOString(),
      location: 'Nowhere',
      description: 'An update that must not be allowed.',
    })
    expect(forbiddenUpdate.status()).toBe(403)
    expect(await forbiddenUpdate.text()).toContain('You can only edit your own events.')
  })
})
