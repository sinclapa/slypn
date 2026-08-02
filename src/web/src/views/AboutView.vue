<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
import SkylineIcon from '@/components/common/SkylineIcon.vue'

const founders = [
  { name: 'Helen Stoinanov', role: 'Co-founder, 2011' },
  { name: 'Kate Wellington', role: 'Co-founder, 2011' },
  { name: 'Sarah Webb',      role: 'Co-founder, 2011' },
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

    <h2 class="mt-12 font-display text-2xl font-bold text-slypn-700">How to join</h2>
    <p class="mt-4 text-slypn-900/85">
      Come to a meet-up. Bring whoever you&rsquo;d like with you. There&rsquo;s no membership
      form to fill in &mdash; just show up. If it would help to know who to look out for, send us
      a message first.
    </p>
  </section>
</template>
