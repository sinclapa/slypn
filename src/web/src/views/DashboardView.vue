<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useApprovalsStore } from '@/stores/approvals'

const auth = useAuthStore()
const approvalsStore = useApprovalsStore()

onMounted(() => { if (auth.isAdmin) approvalsStore.refresh() })
</script>

<template>
  <main class="page-container py-16">
    <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-slypn-500">
      Dashboard
    </p>
    <h1 class="mt-2 text-4xl font-extrabold text-slypn-700">
      Welcome back, {{ auth.displayName }}
    </h1>
    <p class="mt-3 text-slypn-900/80">
      You&rsquo;re signed in as <code class="rounded bg-slypn-100 px-1.5 py-0.5 text-slypn-800">{{ auth.account?.username }}</code>.
    </p>

    <section class="mt-10">
      <h2 class="font-display text-xl font-bold text-slypn-700">Your role</h2>
      <ul v-if="auth.roles.length" class="mt-3 flex flex-wrap gap-2">
        <li
          v-for="role in auth.roles"
          :key="role"
          class="rounded-full bg-slypn-100 px-3 py-1 text-sm font-medium text-slypn-800"
        >
          {{ role }}
        </li>
      </ul>
      <p v-else class="mt-3 text-sm text-slypn-900/70">
        You don&rsquo;t hold any SLYPN roles yet. An admin will assign one.
      </p>
    </section>

    <section class="mt-10 grid gap-4 sm:grid-cols-2">
      <RouterLink
        v-if="auth.isAdmin"
        to="/admin/approvals"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="flex items-center gap-2 font-display font-bold text-slypn-700">
          Approvals
          <span
            v-if="approvalsStore.pendingCount > 0"
            class="rounded-full bg-amber-500 px-1.5 py-0.5 text-xs font-bold text-white"
          >{{ approvalsStore.pendingCount }}</span>
        </p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Review pending content and deletion requests, then approve or request revisions.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isContributor || auth.isAdmin"
        to="/admin/content"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Content management</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Review and remove published articles and blog posts.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isContributor || auth.isAdmin"
        to="/editor"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Editor</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Draft articles and blog posts. Submit for admin approval when ready.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isContributor || auth.isAdmin"
        to="/admin/events"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Event management</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Add events to the community calendar and remove old ones.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isAdmin"
        to="/admin/members"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Members</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Invite new members, view all members, and manage roles.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isAdmin"
        to="/admin/subscribers"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Newsletter subscribers</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          View who signed up for the newsletter, and remove addresses.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isAdmin"
        to="/admin/resources"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Resources</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Add, edit, and remove the links on the public Resources page.
        </p>
      </RouterLink>
      <RouterLink
        v-if="auth.isAdmin"
        to="/admin/newsletters"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Newsletters</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Add, edit, and remove newsletter issues, and attach their files.
        </p>
      </RouterLink>
    </section>
  </main>
</template>
