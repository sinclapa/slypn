<script setup lang="ts">
import HeroBanner from '@/components/common/HeroBanner.vue'
import ApprovalsQueue from '@/components/common/ApprovalsQueue.vue'
import PublishedContent from '@/components/common/PublishedContent.vue'
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="SLYPN administration"
    subtitle="Invite members, manage roles, and approve pending content."
  />

  <section class="mx-auto max-w-3xl space-y-6 px-6 py-16">
    <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <h2 class="font-display text-xl font-bold text-slypn-700">Members</h2>
      <p class="mt-2 text-sm text-slypn-900/75">Invite new members, view all members, and manage roles.</p>
      <div class="mt-4">
        <RouterLink
          to="/admin/members"
          class="inline-flex items-center rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
        >
          Manage members &rarr;
        </RouterLink>
      </div>
    </article>

    <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <h2 class="font-display text-xl font-bold text-slypn-700">Event management</h2>
      <p class="mt-2 text-sm text-slypn-900/75">Add events to the community calendar and remove old ones.</p>
      <div class="mt-4">
        <RouterLink
          to="/admin/events"
          class="inline-flex items-center rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700"
        >
          Manage events &rarr;
        </RouterLink>
      </div>
    </article>

    <ApprovalsQueue />

    <PublishedContent />
  </section>
</template>
