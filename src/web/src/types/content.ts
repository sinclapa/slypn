export type ArticleCategory =
  | "Living with Parkinson's"
  | 'Treatment'
  | 'Community'
  | 'Lifestyle'

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
  tags: string[]
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
}
