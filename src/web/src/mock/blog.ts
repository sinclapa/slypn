import type { BlogPost } from '@/types/content'

export const mockBlogPosts: BlogPost[] = [
  {
    id: 'b1',
    slug: 'brixton-meetup-may-2026',
    title: 'Brixton coffee meet-up — May recap',
    excerpt:
      'Eleven of us, two new faces, far too much cake. A short recap from Tuesday night.',
    body:
      "We had eleven members at Federation Coffee in Brixton on Tuesday — two new, both newly diagnosed and brave enough to walk in without knowing anyone. (Thank you both.) Conversation drifted, as it always does, between very serious (medication changes) and completely silly (someone's dog had eaten a passport).\n\nNext South London meet-up is in Clapham on the 28th. See the Events page.",
    author: 'Sarah Webb',
    publishedAt: '2026-05-15T20:30:00Z',
  },
  {
    id: 'b2',
    slug: 'welcome-new-members-q2',
    title: 'Welcome to our new members',
    excerpt:
      'A short hello to the eight people who have joined the network since the start of April.',
    body:
      "We've had a busy spring. Eight new people have joined SLYPN since the start of April, taking total membership past 220. If you've signed up recently — welcome. Come to a meet-up, drop a note in the newsletter, or just say hello.",
    author: 'Helen Stoinanov',
    publishedAt: '2026-05-02T09:00:00Z',
  },
  {
    id: 'b3',
    slug: 'thank-you-5k-runners',
    title: 'Thank you to our 5k runners',
    excerpt:
      'Our spring fundraiser raised £4,820 for Parkinson’s UK. Thank you to everyone who ran, walked, or cheered.',
    body:
      "Forty of us turned out in Hyde Park on a damp Sunday morning. Between sponsorship and matched donations we raised £4,820 for Parkinson's UK research. Particular thanks to David's team for the post-race coffee run, and to anyone whose knees are still recovering.",
    author: 'Kate Wellington',
    publishedAt: '2026-04-21T09:00:00Z',
  },
]
