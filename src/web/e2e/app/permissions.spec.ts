import { expect, test } from '../support/fixtures'
import { createDraft, publishAuthoredArticle } from '../support/data'
import { titleFor } from '../support/ids'

/**
 * Server-side authorisation, checked directly rather than through the UI.
 *
 * The UI hides what a role may not do, which means the only way to prove the
 * API enforces the same rules is to ask it without the UI in the way. If these
 * regress, the app still *looks* correct while being wide open — exactly the
 * failure mode a UI-only suite cannot catch.
 *
 * Requests carry the same X-Slypn-Dev-User header the browser sends, so
 * JwtMiddleware applies the real [RequireRole] gate.
 */
test.describe('a member', () => {
  test.use({ persona: 'member' })

  test('is refused every contributor and admin endpoint', async ({ api }) => {
    for (const path of ['/drafts', '/members']) {
      const resp = await api.get(path)
      expect(resp.status(), `GET ${path}`).toBe(403)
    }
    expect((await api.post('/articles', {})).status()).toBe(403)
    expect((await api.post('/events', {})).status()).toBe(403)
    expect((await api.post('/resources', {})).status()).toBe(403)
  })

  test('can still read their own profile and the public endpoints', async ({ api }) => {
    const me = await api.json<{ roles: string[] }>('/me')
    expect(me.roles).toEqual(['Member'])

    for (const path of ['/articles', '/blog', '/events', '/resources', '/newsletters']) {
      expect((await api.get(path)).ok(), `GET ${path}`).toBeTruthy()
    }
  })
})

test.describe('a contributor', () => {
  test.use({ persona: 'contributor' })

  test('cannot publish, delete or revise content', async ({ api, adminApi, cleanup, uid }) => {
    const article = await publishAuthoredArticle(api, adminApi, cleanup, {
      title: titleFor(uid, 'Admin-only actions'),
    })

    expect((await api.post(`/articles/${article.id}/publish`)).status()).toBe(403)
    expect((await api.post(`/articles/${article.id}/revise`, { feedback: 'nope' })).status()).toBe(403)
    expect((await api.post(`/articles/${article.id}/cancel-deletion`)).status()).toBe(403)
    expect((await api.del(`/articles/${article.id}?status=published`)).status()).toBe(403)

    // ...and it really is still there.
    expect((await adminApi.get(`/articles/${article.slug}`)).ok()).toBeTruthy()
  })

  test('cannot administer members, resources or newsletters', async ({ api }) => {
    expect((await api.get('/members')).status()).toBe(403)
    expect((await api.post('/members/invite', {
      email: 'nope@example.invalid', displayName: 'Nope', roles: ['Admin'],
    })).status()).toBe(403)
    expect((await api.post('/resources', {})).status()).toBe(403)
    expect((await api.post('/newsletters', {})).status()).toBe(403)
  })

  test('cannot read or write another author drafts', async ({ api, adminApi, cleanup, uid }) => {
    // Drafts are partitioned by authorId, so another author's draft is simply
    // not there for this caller — not a 403, a 404.
    const other = await createDraft(adminApi, cleanup, { title: titleFor(uid, 'Not yours') })

    expect((await api.get(`/drafts/${other.id}`)).status()).toBe(404)
    expect((await api.json<{ id: string }[]>('/drafts')).map((d) => d.id)).not.toContain(other.id)
  })

  test('sees "Request deletion" rather than "Delete" on their published content',
    async ({ page, api, adminApi, cleanup, uid }) => {
      await publishAuthoredArticle(api, adminApi, cleanup, {
        title: titleFor(uid, 'Contributor owned'),
      })

      await page.goto('/admin/content')
      await page.getByTestId('published-search').fill(uid)

      await expect(
        page.getByTestId('published-item').filter({ hasText: uid }).getByTestId('published-delete'),
      ).toHaveText('Request deletion')
    })
})

test.describe('a caller with no persona header', () => {
  test.use({ persona: 'admin' }) // only for the page fixture; anonApi sends no header

  test('is treated as the default admin persona — dev-only, and deliberate',
    async ({ anonApi }) => {
      // With AzureAd__SkipAuth=true, JwtMiddleware short-circuits to
      // DevPersonas.Resolve(header), and Resolve() falls back to DefaultKey
      // ("admin") when the header is missing. So there is NO anonymous state at
      // the API in this configuration, and the real 401 path
      // ("Missing Bearer token") cannot be reached from e2e at all.
      //
      // This is asserted rather than skipped so that the fallback is pinned: if
      // it ever changed to "deny", or if SkipAuth leaked into an environment
      // where it matters, this test says so out loud.
      const me = await anonApi.json<{ roles: string[] }>('/me')
      expect(me.roles).toEqual(['Admin'])
      expect((await anonApi.get('/members')).ok()).toBeTruthy()
    })

  test('can read every public endpoint', async ({ anonApi }) => {
    for (const path of ['/articles', '/blog', '/events', '/resources', '/newsletters']) {
      expect((await anonApi.get(path)).ok(), `GET ${path} anonymously`).toBeTruthy()
    }
  })

  test('can read in-review submissions — a known gap, asserted as-is', async ({ anonApi }) => {
    // `GET /articles?status=in-review` carries no [RequireRole], so unpublished
    // submissions are world-readable. ApprovalsQueue and EditorView both depend
    // on that today, so this records current behaviour rather than endorsing it.
    // If the endpoint is locked down, this test should be updated to expect 401.
    const resp = await anonApi.get('/articles?status=in-review')
    expect(resp.status()).toBe(200)
  })
})
