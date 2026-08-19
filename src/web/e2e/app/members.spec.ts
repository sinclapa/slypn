import { expect, test } from '../support/fixtures'
import { inviteMember, type MemberLike } from '../support/data'
import { withConfirm } from '../helpers'
import { emailFor } from '../support/ids'
import { DEV_PERSONAS } from '../../src/lib/devPersonas'

/**
 * Member administration.
 *
 * Runs as `admin2` so the "you cannot change your own role" checks target a row
 * no other spec depends on, and so the primary `admin` persona is never at risk.
 * Only rows this spec created (all `@example.invalid`) are ever deleted — the
 * persona rows TableBootstrapper seeds are what every other spec signs in as.
 */
test.describe('member management', () => {
  test.use({ persona: 'admin2' })

  test('inviting someone creates an invited member', async ({ page, api, cleanup, uid }) => {
    const email = emailFor(uid)

    await page.goto('/admin/members')
    await page.getByTestId('invite-toggle').click()

    await page.locator('#invite-email').fill(email)
    await page.locator('#invite-name').fill(`E2E Invitee ${uid}`)
    await page.getByTestId('invite-role').and(page.locator('[data-role="Contributor"]')).click()

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/members/invite')),
      page.getByTestId('invite-submit').click(),
    ])
    const { member } = await response.json() as { member: MemberLike }
    cleanup(async () => { await api.del(`/members/${member.id}`) })

    await expect(page.getByTestId('invite-result')).toContainText(email)
    await expect(page.getByTestId('invite-result')).toContainText('has been saved')
    // GraphOptions.IsConfigured only needs InviteRedirectUrl, which has a
    // non-empty default, so CiamInviteService always produces a share link —
    // no client secret required. This branch is therefore deterministic.
    await expect(page.getByTestId('invite-result')).toContainText('Share this sign-up link')

    const members = await api.json<MemberLike[]>('/members')
    const created = members.find((m) => m.email === email)
    expect(created?.status).toBe('invited')
    expect(created?.roles).toEqual(['Contributor'])
  })

  test('changing a role persists it', async ({ page, api, cleanup, uid }) => {
    const member = await inviteMember(api, cleanup, {
      email: emailFor(uid), displayName: `E2E Role ${uid}`, role: 'Member',
    })

    await page.goto('/admin/members')
    const row = page.getByTestId('member-row').filter({ hasText: uid })
    await expect(row).toBeVisible()

    await Promise.all([
      page.waitForResponse((r) =>
        r.url().includes(`/api/members/${member.id}`) && r.request().method() === 'PATCH'),
      row.getByTestId('member-role').and(page.locator('[data-role="Admin"]')).click(),
    ])

    await expect(row.getByTestId('member-role').and(page.locator('[data-role="Admin"]')))
      .toHaveAttribute('data-selected', 'true')

    const members = await api.json<MemberLike[]>('/members')
    expect(members.find((m) => m.id === member.id)?.roles).toEqual(['Admin'])
  })

  test('an admin cannot change or remove their own row', async ({ page, api }) => {
    await page.goto('/admin/members')

    const ownRow = page.getByTestId('member-row').filter({ hasText: DEV_PERSONAS.admin2.username })
    await expect(ownRow).toBeVisible()
    await expect(ownRow.getByTestId('member-remove')).toBeDisabled()
    for (const role of ['Admin', 'Contributor', 'Member']) {
      await expect(ownRow.getByTestId('member-role').and(page.locator(`[data-role="${role}"]`)))
        .toBeDisabled()
    }

    // The UI only disables the buttons; the rule itself lives in the API, and
    // that is the half that actually protects the account.
    const own = (await api.json<MemberLike[]>('/members'))
      .find((m) => m.email === DEV_PERSONAS.admin2.username)!

    const roleChange = await api.patch(`/members/${own.id}`, { roles: ['Member'] })
    expect(roleChange.status()).toBe(400)
    expect(await roleChange.text()).toContain('You cannot change your own role.')

    const selfDelete = await api.del(`/members/${own.id}`)
    expect(selfDelete.status()).toBe(400)
    expect(await selfDelete.text()).toContain('You cannot remove yourself.')
  })

  test('removing an invited member asks first, then deletes them', async ({ page, api, cleanup, uid }) => {
    const member = await inviteMember(api, cleanup, {
      email: emailFor(uid), displayName: `E2E Removable ${uid}`,
    })

    await page.goto('/admin/members')
    const row = page.getByTestId('member-row').filter({ hasText: uid })

    await withConfirm(page, /Remove E2E Removable/, 'accept', async () => {
      await row.getByTestId('member-remove').click()
    })

    await expect(page.getByTestId('member-row').filter({ hasText: uid })).toHaveCount(0)
    const members = await api.json<MemberLike[]>('/members')
    expect(members.map((m) => m.id)).not.toContain(member.id)
  })

  test('cancelling the remove confirm keeps the member', async ({ page, api, cleanup, uid }) => {
    const member = await inviteMember(api, cleanup, {
      email: emailFor(uid), displayName: `E2E Retained ${uid}`,
    })

    await page.goto('/admin/members')
    const row = page.getByTestId('member-row').filter({ hasText: uid })

    await withConfirm(page, /Remove E2E Retained/, 'dismiss', async () => {
      await row.getByTestId('member-remove').click()
    })

    await expect(row).toBeVisible()
    const members = await api.json<MemberLike[]>('/members')
    expect(members.map((m) => m.id)).toContain(member.id)
  })

  test('an invite with an unknown role is rejected by the API', async ({ api, uid }) => {
    const resp = await api.post('/members/invite', {
      email: emailFor(uid),
      displayName: `E2E Bad Role ${uid}`,
      roles: ['Overlord'],
    })
    expect(resp.status()).toBe(400)
    expect(await resp.text()).toContain('Unknown role')
  })
})
