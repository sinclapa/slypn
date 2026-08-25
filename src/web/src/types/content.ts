export type ArticleCategory =
  | "Living with Parkinson's"
  | 'Treatment'
  | 'Community'
  | 'Lifestyle'

export interface ArticleNeighbour {
  slug: string
  title: string
}

export interface Article {
  id: string
  slug: string
  title: string
  summary: string
  body: string
  author: string
  publishedAt: string
  readingMinutes: number
  category: ArticleCategory
  type?: 'article' | 'blog'
  /**
   * Server-computed: may the signed-in caller open this in the editor (Admin, or the
   * Contributor who wrote it)? Deliberately a flag rather than an authorId to compare
   * against — the author's Entra OID has no business in a public payload. Presentation
   * only; the API re-checks on POST /articles/{id}/edit.
   */
  canEdit?: boolean
  prev?: ArticleNeighbour
  next?: ArticleNeighbour
}

export interface BlogPost {
  id: string
  slug: string
  title: string
  excerpt: string
  body: string
  author: string
  publishedAt: string
}

export type EventType =
  | 'Coffee meet-up'
  | 'Drinks'
  | 'Fundraising'
  | 'Q&A'
  | 'Carer session'
  | 'Activity'

export interface EventNeighbour {
  id: string
  title: string
  startsAt: string
}

export interface CommunityEvent {
  id: string
  title: string
  type: EventType
  startsAt: string
  endsAt: string
  location: string
  description: string
  signupUrl?: string
  createdBy?: string
  createdByName?: string
  _etag?: string
  prev?: EventNeighbour
  next?: EventNeighbour
}

export type ResourceCategory =
  | "Parkinson's UK"
  | 'NHS'
  | 'Local'
  | 'Benefits'
  | 'Carers'
  | 'Research'

export interface Resource {
  id: string
  title: string
  description: string
  url: string
  category: ResourceCategory
}

export interface Newsletter {
  id: string
  title: string
  issueDate: string
  summary: string
  topics: string[]
  /** Canonical download filename of the attached issue, present only when a file exists. */
  fileName?: string
}
