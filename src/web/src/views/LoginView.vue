<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
</script>

<template>
  <HeroBanner
    eyebrow="Sign in"
    title="Member sign-in is coming soon"
    subtitle="We're wiring up sign-in via Entra External ID — with Google and Facebook as social options — as part of Phase 3 of the build. Until then, all public content is available without signing in."
  />

  <section class="mx-auto max-w-3xl px-6 py-16">
    <h2 class="font-display text-2xl font-bold text-slypn-700">What sign-in will unlock</h2>
    <ul class="mt-4 space-y-3 text-slypn-900/85">
      <li><strong>Members</strong> — a private dashboard, RSVP for events, comment on articles.</li>
      <li><strong>Contributors</strong> — write articles and blog posts in the browser with autosave; admin approves before publication.</li>
      <li><strong>Admins</strong> — manage members, articles, events, resources, and the newsletter.</li>
    </ul>

    <p class="mt-8 text-sm text-slypn-900/70">
      The detailed plan is on
      <a
        href="https://github.com/sinclapa/slypn/issues/18"
        class="text-slypn-600 underline underline-offset-4 hover:text-slypn-700"
        rel="noopener"
        target="_blank"
      >issue #18 (Phase 3 &mdash; Auth + RBAC)</a>.
    </p>
  </section>
</template>
