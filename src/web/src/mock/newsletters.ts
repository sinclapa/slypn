import type { Newsletter } from '@/types/content'

export const mockNewsletters: Newsletter[] = [
  {
    id: 'n1',
    title: 'May 2026',
    issueDate: '2026-05-01',
    summary:
      'Spring fundraiser write-up, eight new members welcomed, and a heads-up about the autumn 5k.',
    topics: ['Spring fundraiser', 'New members', 'Autumn 5k', 'Q&A with neurologist'],
  },
  {
    id: 'n2',
    title: 'April 2026',
    issueDate: '2026-04-01',
    summary:
      'A short feature on medication windows, the spring meet-up schedule, and an introduction from a new member volunteer.',
    topics: ['Medication windows', 'Meet-up schedule', 'Member feature'],
  },
  {
    id: 'n3',
    title: 'March 2026',
    issueDate: '2026-03-01',
    summary:
      'Sleep, exercise, and Parkinson’s — a long-form piece — plus the dates for the spring fundraiser.',
    topics: ['Sleep and exercise', 'Fundraiser dates', 'Carer session announcement'],
  },
  {
    id: 'n4',
    title: 'February 2026',
    issueDate: '2026-02-01',
    summary:
      'A new year retrospective from the founders, plus thoughts on disclosure at work from members.',
    topics: ['Founders retrospective', 'Work and disclosure'],
  },
]
