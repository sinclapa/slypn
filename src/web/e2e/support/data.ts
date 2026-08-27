import { randomBytes } from 'node:crypto'
import { expect } from '@playwright/test'
import type { ApiClient } from './api-client'
import { expectOk } from './api-client'

/**
 * API-only factories for test prerequisites.
 *
 * Building a fixture through the UI is slow and couples unrelated specs to the
 * editor's behaviour, so anything that is merely a precondition is created over
 * HTTP. The one exception is the content lifecycle spec, where the UI path IS
 * the subject under test.
 *
 * Every factory returns the created entity and registers a cleanup callback on
 * the caller's stack, which the fixture drains LIFO after each test.
 */

export type Cleanup = (fn: () => Promise<void>) => void

export interface ArticleLike {
  id: string
  slug: string
  title: string
  status: string
  type?: string
  authorId?: string | null
  category: string
}

export interface EventLike {
  id: string
  title: string
  startsAt: string
  createdBy?: string | null
}

export interface ResourceLike { id: string; title: string; category: string }
export interface NewsletterLike { id: string; title: string; fileName?: string | null }
export interface DraftLike { id: string; title: string; type: string }
export interface MemberLike { id: string; email: string; roles: string[]; status: string }

export function draftId(): string {
  return randomBytes(16).toString('hex')
}

/**
 * A published article with no authorId — the shape `POST /api/articles`
 * produces. Fine for public browsing; NOT visible to a contributor in
 * /admin/content, which filters on `authorId === auth.oid`.
 *
 * Pass an ADMIN api client: setting status=published on create is Admin-only,
 * so a contributor persona gets 403 here. Use publishAuthoredArticle below for
 * content that has to belong to a contributor.
 */
export async function createPublishedArticle(
  api: ApiClient,
  cleanup: Cleanup,
  fields: { title: string; category?: string; summary?: string; body?: string; author?: string; type?: 'article' | 'blog' },
): Promise<ArticleLike> {
  const slug = fields.title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')
  const article = await expectOk<ArticleLike>(
    await api.post('/content', {
      slug,
      title: fields.title,
      summary: fields.summary ?? `${fields.title} — summary for the e2e suite.`,
      body: fields.body ?? `<p>${fields.title} body content for the e2e suite.</p>`,
      author: fields.author ?? 'E2E Author',
      readingMinutes: 3,
      category: fields.category ?? 'Community',
      status: 'published',
      // Required: the create endpoint is type-agnostic, so the body says what it is making.
      type: fields.type ?? 'article',
    }),
    'POST /content',
  )
  cleanup(async () => { await api.del(`/content/${article.id}?status=published`) })
  return article
}

/** A draft owned by the client's persona. */
export async function createDraft(
  api: ApiClient,
  cleanup: Cleanup,
  fields: { title: string; type?: 'article' | 'blog'; summary?: string; body?: string; category?: string },
): Promise<DraftLike> {
  const id = draftId()
  const draft = await expectOk<DraftLike>(
    await api.put(`/drafts/${id}`, {
      type: fields.type ?? 'article',
      title: fields.title,
      slug: '',
      summary: fields.summary ?? `${fields.title} — summary for the e2e suite.`,
      body: fields.body ?? `<p>${fields.title} body content for the e2e suite.</p>`,
      category: fields.category ?? 'Community',
      readingMinutes: 2,
    }),
    `PUT /drafts/${id}`,
  )
  // A submitted draft no longer exists; DELETE then 404s, which is harmless.
  cleanup(async () => { await api.del(`/drafts/${id}`) })
  return draft
}

/** Promote a draft to an in-review article (same id). */
export async function submitDraft(
  api: ApiClient,
  cleanup: Cleanup,
  id: string,
): Promise<ArticleLike> {
  const article = await expectOk<ArticleLike>(
    await api.post(`/drafts/${id}/submit`),
    `POST /drafts/${id}/submit`,
  )
  cleanup(async () => { await api.del(`/content/${article.id}?status=in-review`) })
  return article
}

/**
 * The full authored path: draft → submit → admin publish. Use this (not
 * createPublishedArticle) whenever a spec needs published content that the
 * authoring contributor can see and manage in /admin/content.
 */
export async function publishAuthoredArticle(
  authorApi: ApiClient,
  adminApi: ApiClient,
  cleanup: Cleanup,
  fields: { title: string; type?: 'article' | 'blog'; summary?: string; body?: string; category?: string },
): Promise<ArticleLike> {
  const draft = await createDraft(authorApi, cleanup, fields)
  const inReview = await submitDraft(authorApi, cleanup, draft.id)
  const published = await expectOk<ArticleLike>(
    await adminApi.post(`/content/${inReview.id}/publish`),
    `POST /content/${inReview.id}/publish`,
  )
  cleanup(async () => { await adminApi.del(`/content/${published.id}?status=published`) })
  return published
}

export async function createEvent(
  api: ApiClient,
  cleanup: Cleanup,
  fields: { title: string; startsAt?: Date; type?: string; location?: string; description?: string },
): Promise<EventLike> {
  const starts = fields.startsAt ?? new Date(Date.now() + 14 * 24 * 3600 * 1000)
  const ends = new Date(starts.getTime() + 2 * 3600 * 1000)
  const event = await expectOk<EventLike>(
    await api.post('/events', {
      title: fields.title,
      type: fields.type ?? 'Coffee meet-up',
      startsAt: starts.toISOString(),
      endsAt: ends.toISOString(),
      location: fields.location ?? 'E2E Test Cafe, London',
      description: fields.description ?? `${fields.title} — an event created by the e2e suite.`,
    }),
    'POST /events',
  )
  cleanup(async () => { await api.del(`/events/${event.id}`) })
  return event
}

export async function createResource(
  api: ApiClient,
  cleanup: Cleanup,
  fields: { title: string; category?: string; url?: string; description?: string },
): Promise<ResourceLike> {
  const category = fields.category ?? 'Support'
  const resource = await expectOk<ResourceLike>(
    await api.post('/resources', {
      title: fields.title,
      description: fields.description ?? `${fields.title} — a resource created by the e2e suite.`,
      url: fields.url ?? 'https://www.parkinsons.org.uk/',
      category,
    }),
    'POST /resources',
  )
  cleanup(async () => { await api.del(`/resources/${resource.id}?category=${encodeURIComponent(category)}`) })
  return resource
}

export async function createNewsletter(
  api: ApiClient,
  cleanup: Cleanup,
  fields: { title: string; issueDate?: string; summary?: string; topics?: string[] },
): Promise<NewsletterLike> {
  const newsletter = await expectOk<NewsletterLike>(
    await api.post('/newsletters', {
      title: fields.title,
      issueDate: fields.issueDate ?? new Date().toISOString().slice(0, 10),
      summary: fields.summary ?? `${fields.title} — an issue created by the e2e suite.`,
      topics: fields.topics ?? ['Research', 'Events'],
    }),
    'POST /newsletters',
  )
  cleanup(async () => { await api.del(`/newsletters/${newsletter.id}`) })
  return newsletter
}

/** Attach a PDF/DOCX to a newsletter (multipart part name must be `file`). */
export async function uploadNewsletterFile(
  api: ApiClient,
  id: string,
  file: { name: string; mimeType: string; buffer: Buffer },
): Promise<NewsletterLike> {
  const resp = await api.raw.put(api.resolve(`/newsletters/${id}/file`), {
    multipart: { file },
    failOnStatusCode: false,
  })
  return await expectOk<NewsletterLike>(resp, `PUT /newsletters/${id}/file`)
}

export async function inviteMember(
  api: ApiClient,
  cleanup: Cleanup,
  fields: { email: string; displayName: string; role?: 'Admin' | 'Contributor' | 'Member' },
): Promise<MemberLike> {
  const result = await expectOk<{ member: MemberLike }>(
    await api.post('/members/invite', {
      email: fields.email,
      displayName: fields.displayName,
      roles: [fields.role ?? 'Member'],
    }),
    'POST /members/invite',
  )
  cleanup(async () => { await api.del(`/members/${result.member.id}`) })
  return result.member
}

/** 1x1 transparent PNG — the smallest thing BlobService's allowlist accepts. */
export const PIXEL_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
)

/**
 * Wait until a draft's stored body contains `needle`.
 *
 * Do NOT use the save indicator as a barrier before reading the API. It reports
 * the state of the LAST autosave cycle, and the 1.5s debounce means a burst of
 * edits produces several: the indicator can already read "saved" from an
 * earlier cycle while the most recent change is still only in the browser.
 * Polling the API asserts the thing actually under test — what reached storage
 * — and is immune to that ordering.
 */
export async function expectDraftBodyToContain(
  api: ApiClient,
  id: string,
  needle: string,
): Promise<void> {
  await expect
    .poll(async () => (await api.json<{ body: string }>(`/drafts/${id}`)).body, {
      message: `draft ${id} body should contain ${needle}`,
      timeout: 20_000,
    })
    .toContain(needle)
}

/** As above, for a scalar field on the draft. */
export async function expectDraftFieldToBe(
  api: ApiClient,
  id: string,
  field: 'summary' | 'title' | 'category',
  value: string,
): Promise<void> {
  await expect
    .poll(async () => (await api.json<Record<string, string>>(`/drafts/${id}`))[field], {
      message: `draft ${id} ${field} should be "${value}"`,
      timeout: 20_000,
    })
    .toBe(value)
}
