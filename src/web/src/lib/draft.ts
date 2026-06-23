// Shared draft shape + helpers used by the editor page and the reusable
// DraftEditor component.
export interface DraftPayload {
  type: 'article' | 'blog'
  title: string
  slug: string
  summary: string
  body: string
  category: string
  tags: string[]
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
  body: '', category: '', tags: [], readingMinutes: 1,
}

export function makeDraftId(): string {
  if ('randomUUID' in crypto) return crypto.randomUUID().replace(/-/g, '')
  return Math.random().toString(16).slice(2).padEnd(32, '0')
}
