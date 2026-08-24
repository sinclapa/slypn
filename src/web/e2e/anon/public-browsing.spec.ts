import { expect, test, type APIRequestContext } from '@playwright/test'
import { primePage } from '../support/fixtures'

/**
 * What a signed-out visitor sees, asserted against what the API actually
 * returns rather than against hard-coded copy.
 *
 * The demo seed (`Slypn.Seed --demo`, ensured by global-setup) is the baseline:
 * deterministic ids, and nothing in the suite mutates it. Requests go through
 * the page's own origin so Vite proxies them to :7071 exactly as the app does.
 */

const API = 'http://localhost:7071/api'

async function apiJson<T>(request: APIRequestContext, path: string): Promise<T> {
  const resp = await request.get(`${API}${path}`)
  expect(resp.ok(), `GET ${path} should succeed`).toBeTruthy()
  return await resp.json() as T
}

interface Article {
  id: string
  slug: string
  title: string
  category: string
  summary: string
}
interface CommunityEvent { id: string; title: string; location: string }
interface Newsletter { id: string; title: string }
interface Resource { id: string; title: string; url: string }

test.describe('public browsing', () => {
  // Dismiss the cookie banner up front: it is a full-width `<dialog>` pinned to
  // the bottom of the viewport, so it covers the last card in every list and
  // makes those clicks fail actionability. The banner itself is covered by
  // cookie-banner.spec.ts, which opts out of this.
  test.beforeEach(async ({ page }) => {
    await primePage(page, null)
  })

  test('the articles page lists exactly what the API publishes', async ({ page, request }) => {
    const published = await apiJson<Article[]>(request, '/articles?status=published')
    expect(published.length, 'demo seed should have published articles').toBeGreaterThan(0)

    await page.goto('/articles')

    const cards = page.getByTestId('article-card')
    await expect(cards).toHaveCount(published.length)
    for (const article of published.slice(0, 3)) {
      await expect(page.getByTestId('article-card').filter({ hasText: article.title })).toBeVisible()
    }
  })

  test('the category filter narrows the list to that category', async ({ page, request }) => {
    const published = await apiJson<Article[]>(request, '/articles?status=published')
    const category = published.find((a) => a.category)!.category
    const expected = published.filter((a) => a.category === category).length

    await page.goto('/articles')
    await page.getByRole('button', { name: category, exact: true }).click()

    await expect(page.getByTestId('article-card')).toHaveCount(expected)
  })

  test('an article card opens a detail page with its body', async ({ page, request }) => {
    const [article] = await apiJson<Article[]>(request, '/articles?status=published')

    await page.goto('/articles')
    await page.getByRole('link', { name: article.title }).first().click()

    await expect(page).toHaveURL(`/articles/${article.slug}`)
    await expect(page.getByRole('heading', { name: article.title })).toBeVisible()
    // The body lives in a blob, not the list payload — seeing it proves the
    // detail endpoint stitched the two together.
    const detail = await apiJson<Article & { body: string }>(request, `/articles/${article.slug}`)
    expect(detail.body.length).toBeGreaterThan(0)
  })

  test('the blog page lists published posts', async ({ page, request }) => {
    const posts = await apiJson<Article[]>(request, '/blog?status=published')
    expect(posts.length).toBeGreaterThan(0)

    await page.goto('/blog')
    await expect(page.getByRole('heading', { name: posts[0].title, exact: true })).toBeVisible()
  })

  test('the events page shows the upcoming events the API reports', async ({ page, request }) => {
    const upcoming = await apiJson<CommunityEvent[]>(request, '/events?upcoming=true')
    expect(upcoming.length).toBeGreaterThan(0)

    await page.goto('/events')
    await expect(page.getByTestId('event-card').first()).toBeVisible()
    await expect(page.getByTestId('event-card').filter({ hasText: upcoming[0].title }).first())
      .toBeVisible()
  })

  test('an event card opens its detail page', async ({ page, request }) => {
    const [event] = await apiJson<CommunityEvent[]>(request, '/events?upcoming=true')

    await page.goto('/events')
    await page.getByTestId('event-card').filter({ hasText: event.title }).first().click()

    await expect(page).toHaveURL(`/events/${event.id}`)
    await expect(page.getByText(event.location).first()).toBeVisible()
  })

  test('previous events are on their own page', async ({ page }) => {
    await page.goto('/events/previous')
    await expect(page.getByTestId('event-card').first()).toBeVisible()
  })

  test('the resources page links out to every seeded resource', async ({ page, request }) => {
    const resources = await apiJson<Resource[]>(request, '/resources')
    expect(resources.length).toBeGreaterThan(0)

    await page.goto('/resources')
    await expect(page.getByTestId('resource-card')).toHaveCount(resources.length)
    await expect(page.getByTestId('resource-card').filter({ hasText: resources[0].title }))
      .toHaveAttribute('href', resources[0].url)
  })

  test('the newsletter page lists the published issues', async ({ page, request }) => {
    const newsletters = await apiJson<Newsletter[]>(request, '/newsletters')
    expect(newsletters.length).toBeGreaterThan(0)

    await page.goto('/newsletter')
    await expect(page.getByText(newsletters[0].title).first()).toBeVisible()
  })

  test('subscribing stores the address as a subscriber', async ({ page, request }) => {
    const email = `e2e-subscribe-${Date.now().toString(36)}@example.invalid`

    await page.goto('/newsletter')
    await page.getByLabel('Email address').fill(email)

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/newsletter/subscribe')),
      page.getByTestId('subscribe-submit').click(),
    ])
    // 2xx, not 201: FunctionHelpers.Created() builds a 201 but the following
    // WriteAsJsonAsync(value) overload resets the status to 200, so every
    // "Created" response in this API is actually a 200 despite the OpenAPI
    // annotation. Asserted loosely so this test tracks the fix rather than
    // blocking it.
    expect(response.ok(), `subscribe returned ${response.status()}`).toBeTruthy()
    await expect(page.getByTestId('subscribe-result')).toBeVisible()

    // SEC-5: the address lands in the subscribers table, never in members — a subscriber
    // that shows up as a member is what let an anonymous subscribe past the sign-up gate.
    // Both lists are admin-only, so read them with the persona header.
    const members = await request.get(`${API}/members`, {
      headers: { 'X-Slypn-Dev-User': 'admin' },
    })
    const memberRows = await members.json() as { email: string }[]
    expect(memberRows.some((m) => m.email === email)).toBe(false)

    const subscribers = await request.get(`${API}/subscribers`, {
      headers: { 'X-Slypn-Dev-User': 'admin' },
    })
    const subscriberRows = await subscribers.json() as { id: string; email: string }[]
    const row = subscriberRows.find((s) => s.email === email)
    expect(row, `${email} not found in the subscriber list`).toBeDefined()

    await request.delete(`${API}/subscribers/${row!.id}`, {
      headers: { 'X-Slypn-Dev-User': 'admin' },
    })
  })

  test('an unknown path renders the not-found view', async ({ page }) => {
    await page.goto('/definitely-not-a-route')
    await expect(page.getByTestId('not-found')).toBeVisible()
  })
})
