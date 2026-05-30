import type { CommunityEvent } from '@/types/content'

export const mockEvents: CommunityEvent[] = [
  {
    id: 'e1',
    title: 'Coffee meet-up — Clapham',
    type: 'Coffee meet-up',
    startsAt: '2026-05-28T18:30:00+01:00',
    endsAt: '2026-05-28T20:30:00+01:00',
    location: 'WatchHouse, Clapham Common SW4',
    description:
      'Our regular monthly meet-up. Drop in any time between 6:30 and 8:30. New members very welcome — message us if it helps to know who to look out for.',
  },
  {
    id: 'e2',
    title: 'Q&A with a movement-disorder neurologist',
    type: 'Q&A',
    startsAt: '2026-06-04T19:00:00+01:00',
    endsAt: '2026-06-04T20:30:00+01:00',
    location: 'Online (Zoom)',
    description:
      "An hour-and-a-half with Dr Priya Iyer (King's College Hospital) covering medication windows, when to consider DBS, and questions submitted in advance.",
    signupUrl: 'https://example.com/slypn/qa-june',
  },
  {
    id: 'e3',
    title: 'Summer drinks — Greenwich',
    type: 'Drinks',
    startsAt: '2026-06-13T19:00:00+01:00',
    endsAt: '2026-06-13T22:00:00+01:00',
    location: 'The Trafalgar Tavern, Greenwich SE10',
    description:
      'Our summer social. Partners and carers welcome. Booked the back room, so there will be seats.',
  },
  {
    id: 'e4',
    title: 'Carer-only catch-up',
    type: 'Carer session',
    startsAt: '2026-06-18T19:30:00+01:00',
    endsAt: '2026-06-18T21:00:00+01:00',
    location: 'Online (Zoom)',
    description:
      'A small group for partners and carers of SLYPN members. Quiet, confidential, hosted by Sarah.',
  },
  {
    id: 'e5',
    title: 'Coffee meet-up — Dulwich',
    type: 'Coffee meet-up',
    startsAt: '2026-06-25T18:30:00+01:00',
    endsAt: '2026-06-25T20:30:00+01:00',
    location: 'Romeo Jones, Dulwich Village SE21',
    description: 'June meet-up. Same format as Clapham — drop in any time.',
  },
  {
    id: 'e6',
    title: 'Beat Parkinson’s 5k — autumn',
    type: 'Fundraising',
    startsAt: '2026-09-20T09:30:00+01:00',
    endsAt: '2026-09-20T12:00:00+01:00',
    location: 'Hyde Park, London W2',
    description:
      'Our autumn fundraiser for Parkinson’s UK. Run, walk, or cheer — same as the spring event. Sign up via the Parkinson’s UK page.',
    signupUrl: 'https://example.com/slypn/5k-autumn',
  },
  {
    id: 'e7',
    title: 'Coffee meet-up — Tooting',
    type: 'Coffee meet-up',
    startsAt: '2026-07-23T18:30:00+01:00',
    endsAt: '2026-07-23T20:30:00+01:00',
    location: 'Brick House, Tooting SW17',
    description: 'July meet-up.',
  },
]
