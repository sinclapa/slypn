<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <main class="mx-auto max-w-3xl px-6 py-16">
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
      <h2 class="font-display text-xl font-bold text-slypn-700">Your roles</h2>
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
        v-if="auth.isAdmin"
        to="/admin"
        class="rounded-xl border border-slypn-100 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <p class="font-display font-bold text-slypn-700">Admin</p>
        <p class="mt-2 text-sm text-slypn-900/75">
          Approve pending content, manage members, edit anything.
        </p>
      </RouterLink>
    </section>
  </main>
</template>
