import type { Article } from '@/types/content'

export const mockArticles: Article[] = [
  {
    id: 'a1',
    slug: 'working-with-parkinsons',
    title: "Living well at work with Parkinson's",
    summary:
      "Practical thinking on workplace adjustments, when to disclose, and how to keep doing the job you love after a Parkinson's diagnosis.",
    body:
      "A Parkinson's diagnosis at working age often comes with a stack of practical questions about the job. There's no single right answer — much depends on your role, your employer, and your symptoms — but a few things consistently help.\n\nStart by reading the rights you already have. In the UK, Parkinson's is covered by the Equality Act 2010, meaning employers must make reasonable adjustments. That might be flexible hours, voice-to-text software, a quieter workspace, or breaks for medication timing.\n\nWhen to disclose is personal. Many SLYPN members tell us they wished they'd waited — and just as many wish they'd told their manager sooner. There's no universally right moment. What helps is having a clear ask ready when you do.\n\nFinally, the small daily things compound. Lay out clothes the night before. Tackle the hardest task in your best-medication window. Build in recovery. Working with Parkinson's is not the same as working without it; pretending otherwise wears you down.",
    author: 'Helen Stoinanov',
    publishedAt: '2026-05-12T09:00:00Z',
    readingMinutes: 6,
    category: "Living with Parkinson's",
    tags: ['work', 'rights', 'adjustments'],
  },
  {
    id: 'a2',
    slug: 'medication-side-effects',
    title: 'Understanding the medication side effects no one warned you about',
    summary:
      'A frank look at the common (and less common) side effects of the medications most often prescribed for Parkinson’s — and what to do when they show up.',
    body:
      "Levodopa is still the most effective treatment for Parkinson's, but the body's relationship with it is messy. Many people experience nausea in the first few weeks, which usually settles. Some find their sleep changes — vivid dreams, sudden tiredness, or difficulty getting off in the first place.\n\nDopamine agonists carry their own profile. Impulse-control issues — sudden cravings to gamble, shop, or eat — are well documented but under-discussed. If you or someone close to you notices new behaviours, it's worth raising with your neurologist; a dose tweak often helps.\n\nWhat we hear most often in our meet-ups is that people would have liked clearer warnings up front. This article is not medical advice — talk to your specialist nurse — but it's the conversation we wish we'd had earlier.",
    author: 'Kate Wellington',
    publishedAt: '2026-04-28T09:00:00Z',
    readingMinutes: 8,
    category: 'Treatment',
    tags: ['medication', 'side-effects', 'levodopa'],
  },
  {
    id: 'a3',
    slug: 'support-network-at-any-age',
    title: 'Building a support network at any age',
    summary:
      'Why peer support matters more than people expect, and how SLYPN works to provide it for the South London under-50s.',
    body:
      "We started SLYPN in 2011 because the support that existed at the time wasn't built around working-age people. Daytime groups didn't fit around jobs or school runs. Helen, Kate, and Sarah wanted somewhere informal, in the evenings, where you could talk frankly without having to explain Parkinson's from scratch every time.\n\nFifteen years on, the format has barely changed: coffee meet-ups across South London, occasional drinks, a few activities a year, and the odd fundraiser. What's grown is the membership. We've watched people arrive newly diagnosed and stay long enough to support the next person walking in.\n\nThe best advice we can offer anyone newly diagnosed: don't wait until things feel hard to ask for support. Find your people early.",
    author: 'Sarah Webb',
    publishedAt: '2026-04-10T09:00:00Z',
    readingMinutes: 5,
    category: 'Community',
    tags: ['peer-support', 'history', 'founders'],
  },
  {
    id: 'a4',
    slug: 'sleep-exercise-parkinsons',
    title: 'Sleep, exercise, and Parkinson’s — what the evidence actually says',
    summary:
      'Cutting through the noise on lifestyle interventions: what helps, what doesn’t, and what to try first.',
    body:
      "There is no shortage of advice online about lifestyle changes for Parkinson's. Some of it is genuinely useful. Some of it is wishful thinking. A few practical things have decent evidence behind them.\n\nExercise — particularly aerobic and resistance training — has the strongest evidence for slowing symptom progression. The Parkinson's UK Cycle to Beat Parkinson's research and several US studies point in the same direction. The key word is regular: little and often beats heroic but sporadic.\n\nSleep is harder. REM sleep behaviour disorder is common, and so is daytime fatigue. Sleep hygiene basics (consistent times, cool dark room, no screens late) help everyone, but if you suspect a sleep disorder, ask for a referral.\n\nWhat doesn't have evidence: most supplements, most diets marketed as 'Parkinson's diets', most heavily-promoted gadgets. Save the money for things you actually enjoy.",
    author: 'Helen Stoinanov',
    publishedAt: '2026-03-22T09:00:00Z',
    readingMinutes: 7,
    category: 'Lifestyle',
    tags: ['sleep', 'exercise', 'evidence'],
  },
  {
    id: 'a5',
    slug: 'what-to-bring-to-your-first-meetup',
    title: 'What to bring (and expect) at your first SLYPN meet-up',
    summary:
      'A short, practical note for anyone thinking about coming to a meet-up for the first time.',
    body:
      "Just yourself. That's the honest answer.\n\nWe meet in coffee shops or pubs around South London, usually for a couple of hours in the evening. There's no introductions round, no name badges, no pressure to talk about Parkinson's if you don't want to. People come along, order whatever they fancy, and end up chatting. Partners and carers are welcome.\n\nIf it would help to know who'll be there, send us a message before — we'll point you toward someone to look out for.",
    author: 'Kate Wellington',
    publishedAt: '2026-03-04T09:00:00Z',
    readingMinutes: 3,
    category: 'Community',
    tags: ['newcomers', 'meet-ups'],
  },
]
