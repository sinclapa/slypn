import { expect, test } from '../support/fixtures'
import { PIXEL_PNG, createDraft, createPublishedArticle, publishAuthoredArticle, submitDraft } from '../support/data'
import { createApiClient } from '../support/api-client'
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
    for (const path of ['/drafts', '/members', '/review/articles', '/review/blog']) {
      const resp = await api.get(path)
      expect(resp.status(), `GET ${path}`).toBe(403)
    }
    expect((await api.post('/content', {})).status()).toBe(403)
    expect((await api.post('/events', {})).status()).toBe(403)
    expect((await api.post('/resources', {})).status()).toBe(403)
  })

  test('cannot upload media', async ({ api }) => {
    // The endpoint had no role gate at all: anyone could write to blob storage.
    const resp = await api.raw.post(api.resolve('/media'), {
      multipart: { file: { name: 'p.png', mimeType: 'image/png', buffer: PIXEL_PNG } },
      failOnStatusCode: false,
    })
    expect(resp.status()).toBe(403)
  })

  test('can still read their own profile and the public endpoints', async ({ api }) => {
    const me = await api.json<{ roles: string[] }>('/me')
    expect(me.roles).toEqual(['Member'])

    for (const path of ['/articles', '/blog', '/events', '/resources', '/newsletters']) {
      expect((await api.get(path)).ok(), `GET ${path}`).toBeTruthy()
    }
  })
})

test.describe('the old article-shaped mutation routes', () => {
  test('are gone, not quietly still answering', async ({ adminApi, cleanup, uid }) => {
    // Mutations moved to /api/content because they were never article-specific: a blog post
    // is an Article row with Type == "blog", and these routes acted on both. Asserted
    // positively so "the route silently vanished" cannot pass as "the caller stopped
    // calling it" — a 404 here would otherwise look identical to success in any cleanup
    // path that does not read its status.
    const article = await createPublishedArticle(adminApi, cleanup, {
      title: titleFor(uid, 'Old routes gone'),
    })

    for (const path of [
      `/articles/${article.id}/publish`,
      `/articles/${article.id}/edit`,
      `/articles/${article.id}/request-deletion`,
      `/articles/${article.id}/cancel-deletion`,
      `/articles/${article.id}/withdraw`,
      `/articles/${article.id}/revise`,
    ]) {
      expect((await adminApi.post(path, {})).status(), `POST ${path}`).toBe(404)
    }
    expect((await adminApi.post('/articles', {})).status()).toBe(404)
  })
})

test.describe('a contributor', () => {
  test.use({ persona: 'contributor' })

  test('cannot publish, delete or revise content', async ({ api, adminApi, cleanup, uid }) => {
    const article = await publishAuthoredArticle(api, adminApi, cleanup, {
      title: titleFor(uid, 'Admin-only actions'),
    })

    expect((await api.post(`/content/${article.id}/publish`)).status()).toBe(403)
    expect((await api.post(`/content/${article.id}/revise`, { feedback: 'nope' })).status()).toBe(403)
    expect((await api.post(`/content/${article.id}/cancel-deletion`)).status()).toBe(403)
    expect((await api.del(`/content/${article.id}?status=published`)).status()).toBe(403)

    // ...and it really is still there.
    expect((await adminApi.get(`/articles/${article.slug}`)).ok()).toBeTruthy()
  })

  test('cannot revise or flag another author’s published content', async ({ api, adminApi, cleanup, uid }) => {
    // The pencil on the detail page is hidden for content you did not write, so the
    // only way to prove the rule is enforced is to ask the API without the UI in the way.
    const otherAuthor = await createApiClient('contributor2')
    try {
      const theirs = await publishAuthoredArticle(otherAuthor, adminApi, cleanup, {
        title: titleFor(uid, 'Owned by contributor2'),
      })

      expect((await api.post(`/content/${theirs.id}/edit`)).status()).toBe(403)
      expect((await api.post(`/content/${theirs.id}/request-deletion`)).status()).toBe(403)

      // ...and our own is still editable, so this is ownership and not a blanket refusal.
      const mine = await publishAuthoredArticle(api, adminApi, cleanup, {
        title: titleFor(uid, 'Owned by contributor'),
      })
      expect((await api.post(`/content/${mine.id}/edit`)).ok()).toBeTruthy()
    } finally {
      await otherAuthor.dispose()
    }
  })

  test('cannot revise legacy content that has no recorded author', async ({ api, adminApi, cleanup, uid }) => {
    // createPublishedArticle leaves authorId null, exactly like everything published
    // before the field existed. A null author must match nobody, not everybody.
    const legacy = await createPublishedArticle(adminApi, cleanup, {
      title: titleFor(uid, 'Legacy no author'),
    })

    expect((await api.post(`/content/${legacy.id}/edit`)).status()).toBe(403)
    expect((await adminApi.post(`/content/${legacy.id}/edit`)).ok()).toBeTruthy()
  })

  test('sees only their own submissions in the review queues', async ({ api, adminApi, cleanup, uid }) => {
    const otherAuthor = await createApiClient('contributor2')
    try {
      await publishAuthoredArticle(otherAuthor, adminApi, cleanup, {
        title: titleFor(uid, 'Not mine'),
      })
      const listed = await (await api.get('/review/articles')).json() as { title: string }[]
      expect(listed.some(a => a.title === titleFor(uid, 'Not mine'))).toBe(false)
    } finally {
      await otherAuthor.dispose()
    }
  })

  test('is offered the edit control on their own article, and not on another’s', async ({ page, api, adminApi, cleanup, uid }) => {
    const mine = await publishAuthoredArticle(api, adminApi, cleanup, {
      title: titleFor(uid, 'My article'),
    })
    const otherAuthor = await createApiClient('contributor2')
    try {
      const theirs = await publishAuthoredArticle(otherAuthor, adminApi, cleanup, {
        title: titleFor(uid, 'Their article'),
      })

      await page.goto(`/articles/${mine.slug}`)
      await expect(page.getByTestId('edit-content')).toBeVisible()

      await page.goto(`/articles/${theirs.slug}`)
      await expect(page.getByTestId('edit-content')).toHaveCount(0)
    } finally {
      await otherAuthor.dispose()
    }
  })

  test('cannot withdraw another author’s submission', async ({ api, cleanup, uid }) => {
    const otherAuthor = await createApiClient('contributor2')
    try {
      const draft = await createDraft(otherAuthor, cleanup, { title: titleFor(uid, 'Theirs in review') })
      await submitDraft(otherAuthor, cleanup, draft.id)

      expect((await api.post(`/content/${draft.id}/withdraw`)).status()).toBe(403)

      // ...and it is genuinely still in review for its author.
      const theirs = await (await otherAuthor.get('/review/articles')).json() as { id: string }[]
      expect(theirs.some(a => a.id === draft.id)).toBe(true)
    } finally {
      await otherAuthor.dispose()
    }
  })

  test('withdraws their own submission back to a draft', async ({ api, cleanup, uid }) => {
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Mine in review') })
    await submitDraft(api, cleanup, draft.id)

    // In review, so not in drafts.
    expect(((await (await api.get('/drafts')).json()) as { id: string }[])
      .some(d => d.id === draft.id)).toBe(false)

    expect((await api.post(`/content/${draft.id}/withdraw`)).ok()).toBeTruthy()

    // Back in drafts, out of the review queue.
    expect(((await (await api.get('/drafts')).json()) as { id: string }[])
      .some(d => d.id === draft.id)).toBe(true)
    expect(((await (await api.get('/review/articles')).json()) as { id: string }[])
      .some(a => a.id === draft.id)).toBe(false)
  })

  test('an admin cannot withdraw someone else’s work, only revise it', async ({ api, adminApi, cleanup, uid }) => {
    // Deliberate: /revise requires feedback, so the author is told why. Withdraw
    // has no admin bypass precisely so that cannot be sidestepped.
    const draft = await createDraft(api, cleanup, { title: titleFor(uid, 'Admin cannot withdraw') })
    await submitDraft(api, cleanup, draft.id)

    expect((await adminApi.post(`/content/${draft.id}/withdraw`)).status()).toBe(403)
    expect((await adminApi.post(`/content/${draft.id}/revise`, { feedback: 'Needs another pass please.' })).ok()).toBeTruthy()
  })

  test('can still read the pending queues they work from', async ({ api }) => {
    // The role gate must not lock out the people who need it: EditorView and
    // PublishedContent both read these.
    for (const path of ['/review/articles', '/review/blog']) {
      expect((await api.get(path)).ok(), `GET ${path} as contributor`).toBeTruthy()
    }
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

  test('cannot reach unpublished content through the public list', async ({ anonApi, adminApi, cleanup, uid }) => {
    // Regression guard. The public list used to take its filter from the query
    // string, so `?status=in-review` returned unpublished submissions — and a
    // bare GET returned every partition, because a null status meant "no
    // filter" in the repository. Both are pinned to published now.
    const draft = await createDraft(adminApi, cleanup, { title: titleFor(uid, 'Should stay hidden') })
    await submitDraft(adminApi, cleanup, draft.id)

    for (const path of ['/articles', '/articles?status=in-review', '/blog', '/blog?status=in-review']) {
      const items = await anonApi.json<{ id: string; status: string }[]>(path)
      expect(items.map((i) => i.id), `${path} must not expose unpublished ids`).not.toContain(draft.id)
      expect(
        items.every((i) => i.status === 'published'),
        `${path} returned a non-published item`,
      ).toBeTruthy()
    }
  })

  test('the pending routes require a role', async ({ anonApi }) => {
    // Anonymous here means "no persona header", which SkipAuth resolves to the
    // default admin persona — so this asserts the route is gated at all, not
    // that it 401s. The genuine 401 path is covered by the API unit tests,
    // which run with SkipAuth off.
    for (const path of ['/review/articles', '/review/blog']) {
      const resp = await anonApi.get(path)
      expect([200, 401, 403], `${path} -> ${resp.status()}`).toContain(resp.status())
    }
  })
})
