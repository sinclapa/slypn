<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
import SkylineIcon from '@/components/common/SkylineIcon.vue'

const founders = [
  { name: 'Helen Stoinanov', role: 'Co-founder, 2011' },
  { name: 'Kate Wellington', role: 'Co-founder, 2011' },
  { name: 'Sarah Webb',      role: 'Co-founder, 2011' },
]

interface TeamMember {
  name: string
  role: string
  bio?: string
  contact?: { label: string; href: string }
}

const team: TeamMember[] = [
  {
    name: 'Sarah Webb',
    role: 'Founder',
    bio: 'Hello. My name is Sarah and I was diagnosed with Parkinson’s at the age of 39 years old. It was the biggest shock of my life and I still have days when I can’t believe it has happened to me. My husband and myself realised we needed to start a network, as we found there wasn’t much support for us when I was first diagnosed. It’s been going since December 2011 and it’s been amazing to see it grow. My goals are to help find that cure, offer help and support to fellow Parkies, plus try and raise as much money and awareness of Young Onset Parkinson’s.',
  },
  {
    name: 'Chris McNicholas',
    role: 'Website',
  },
  {
    name: 'Kerry Wilson',
    role: 'Marketing',
    bio: 'When my mum was diagnosed with Parkinson’s at 53 we were shocked; wrongly we believed Parkinson’s only affected the elderly. That was over 20 years ago & she continues to have struggles although it is well managed with a number of drugs, a positive outlook, yoga sessions, walking, travelling & spending time with family & friends. My background is in Marketing & when I came across an opportunity to join SLYPN I felt it was a perfect fit. I’ve been delighted to work on a number of fundraising projects for research and to find a cure.',
  },
  {
    name: 'Angela Barton',
    role: 'Treasurer',
    bio: 'Hi I’m Angela. I am an accountant and I volunteered to prepare the accounts and do the banking for SLYPN, although Sarah doesn’t let me get away that lightly! My son is friends with her son at school, and I met her shortly before she was diagnosed. I have also seen the effects of Parkinson’s on my husband’s side of the family and wanted to help out in whatever way I could. There’s always something going on, and it’s such a positive and enthusiastic group to be involved with.',
  },
  {
    name: 'Simeone Sistarelli',
    role: 'Popping Instructor',
    contact: { label: 'info@poppingforparkinsons.com', href: 'mailto:info@poppingforparkinsons.com' },
    bio: 'Hi. I am a professional dancer and teacher. I contacted Sarah as I wanted to teach Popping to people with Parkinson’s. My grandfather lived with Parkinson’s, so I know and understand how it feels to have Parkinson’s. Our first session was in the summer of 2015, and the feedback from the project has been very exciting and it is growing incredibly. All I am going to say, come along and give it a go… we work hard, but have a lot of fun. Just email me with any questions you may have…',
  },
  {
    name: 'Liz Whitson',
    role: 'Social Media',
    bio: 'I have known Sarah since our boys started at our local pre-school nursery. We have always spoken about social media and the importance of it for SLYPN and now I am part of the team! I’ve lived in the Wimbledon area since my son was born so it’s great to become more involved in the community. I work with technology every day as I work part time for a digital magazine, so social media is core to what I do for my job. I hope to meet lots of followers in person over the next few months at the various events that I will be tweeting about!',
  },
  {
    name: 'Sue Roberts',
    role: 'New Members',
    bio: 'My name is Sue and I was diagnosed in March 2013. I heard about SLYPN through my consultant and it has helped me over time because I feel that I’m not alone. I was introduced to Pauline when I first joined and the two of us are now good friends. I am also in touch with Sarah and Susan — Susan and I have very similar symptoms. I know that by being part of this close network, there is always support. There are also many social gatherings and fundraising events. Please feel free to contact me, I look forward to speaking or meeting you very soon.',
  },
]
</script>

<template>
  <HeroBanner
    eyebrow="About"
    title="A community for working-age South Londoners with Parkinson's"
    subtitle="Founded in 2011 because the daytime support that existed back then didn't fit around jobs, family, or school runs. Over fifteen years on, SLYPN still meets in the evenings and weekends — and is now a couple of hundred members strong."
  >
    <template #brand>
      <SkylineIcon class="w-48 text-slypn-700 sm:w-72 md:w-80" />
    </template>
  </HeroBanner>

  <section class="page-container-prose py-16">
    <h2 class="font-display text-2xl font-bold text-slypn-700">Who we are</h2>
    <p class="mt-4 text-slypn-900/85">
      Around 1 in 20 people diagnosed with Parkinson&rsquo;s is under 40. South London Younger
      Parkinson&rsquo;s Network (SLYPN) is a support group for the demographic that often
      can&rsquo;t make daytime meetings: people in work, with young families, with school runs.
      We&rsquo;re affiliated with
      <a href="https://www.parkinsons.org.uk/" class="text-slypn-600 underline underline-offset-4 hover:text-slypn-700" rel="noopener" target="_blank">Parkinson&rsquo;s UK</a>.
    </p>

    <h2 class="mt-12 font-display text-2xl font-bold text-slypn-700">What we do</h2>
    <ul class="mt-4 space-y-3 text-slypn-900/85">
      <li>Regular coffee meet-ups on the last Saturday of the month.</li>
      <li>Occasional drinks evenings and activities.</li>
      <li>Social media groups for sharing information and support.</li>
      <li>Weekly online quiz night.</li>
      <li>Q&amp;As with neurologists, specialist nurses, and physios.</li>
      <li>Fundraising for Parkinson&rsquo;s UK.</li>
    </ul>

    <h2 class="mt-12 font-display text-2xl font-bold text-slypn-700">Founders</h2>
    <p class="mt-4 text-slypn-900/85">
      SLYPN was started in 2011 by Helen Stoinanov, Kate Wellington, and Sarah Webb &mdash; three
      South Londoners who wanted somewhere to talk about Parkinson&rsquo;s frankly, in the evening,
      without having to explain the basics each time.
    </p>
    <ul class="mt-4 grid gap-4 sm:grid-cols-3">
      <li
        v-for="f in founders"
        :key="f.name"
        class="rounded-xl border border-slypn-100 bg-white p-4"
      >
        <p class="font-display font-bold text-slypn-700">{{ f.name }}</p>
        <p class="mt-1 text-xs text-slypn-900/60">{{ f.role }}</p>
      </li>
    </ul>

    <h2 class="mt-12 font-display text-2xl font-bold text-slypn-700">The Team</h2>
    <ul class="mt-4 grid gap-4 sm:grid-cols-2">
      <li
        v-for="member in team"
        :key="member.name"
        class="rounded-xl border border-slypn-100 bg-white p-6"
      >
        <p class="font-display font-bold text-slypn-700">{{ member.name }}</p>
        <p class="mt-1 text-xs text-slypn-900/60">{{ member.role }}</p>
        <a
          v-if="member.contact"
          :href="member.contact.href"
          class="mt-1 block text-xs text-slypn-600 underline underline-offset-4 hover:text-slypn-700"
        >{{ member.contact.label }}</a>
        <p v-if="member.bio" class="mt-3 text-sm text-slypn-900/85">{{ member.bio }}</p>
      </li>
    </ul>

    <h2 class="mt-12 font-display text-2xl font-bold text-slypn-700">How to join</h2>
    <p class="mt-4 text-slypn-900/85">
      Come to a meet-up. Bring whoever you&rsquo;d like with you. There&rsquo;s no membership
      form to fill in &mdash; just show up. If it would help to know who to look out for, send us
      a message first.
    </p>
  </section>
</template>
