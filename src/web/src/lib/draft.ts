// Shared draft shape + helpers used by the editor page and the reusable
// DraftEditor component.
export interface DraftPayload {
  type: 'article' | 'blog'
  title: string
  slug: string
  summary: string
  body: string
  category: string
  readingMinutes: number
  revisionFeedback?: string | null
}

export interface DraftSummary {
  id: string
  title: string
  type: string
  updatedAt: string
  _etag?: string
}

export const EMPTY_DRAFT: DraftPayload = {
  type: 'article', title: '', slug: '', summary: '',
  body: '', category: '', readingMinutes: 1,
}

export function makeDraftId(): string {
  if ('randomUUID' in crypto) return crypto.randomUUID().replaceAll('-', '')
  return randomHexFallback()
}

function randomHexFallback(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
}

// Strips markup so callers can reason about the text a draft actually carries.
// Parsing beats a tag-stripping regex here: it can't backtrack on malformed
// HTML, and it decodes entities (&nbsp; and friends) for free.
export function htmlToText(html: string): string {
  return new DOMParser().parseFromString(html, 'text/html').body.textContent ?? ''
}
