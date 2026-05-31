<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="SLYPN administration"
    subtitle="The approvals queue, member management, and content moderation views land with #24 and #29. This page is the role-guarded shell."
  />

  <section class="mx-auto max-w-3xl px-6 py-16 space-y-6">
    <article class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm">
      <h2 class="font-display text-lg font-bold text-slypn-700">Approvals (coming in #29)</h2>
      <p class="mt-2 text-sm text-slypn-900/75">
        Pending articles + blog posts grouped by author with inline publish / reject.
      </p>
    </article>
    <article class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm">
      <h2 class="font-display text-lg font-bold text-slypn-700">Members (coming in #24)</h2>
      <p class="mt-2 text-sm text-slypn-900/75">
        Invite a new member by email, assign a role, revoke access.
      </p>
    </article>
  </section>
</template>
