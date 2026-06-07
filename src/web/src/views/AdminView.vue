<script setup lang="ts">
import { ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import ApprovalsQueue from '@/components/common/ApprovalsQueue.vue'
import PublishedContent from '@/components/common/PublishedContent.vue'
import { apiFetch } from '@/lib/api'

type Role = 'Admin' | 'Contributor' | 'Member'

interface InviteResponseOk {
  member: { id: string; email: string; displayName: string; roles: string[]; status: string }
  inviteSent: boolean
  redeemUrl: string | null
  inviteReason: string | null
}

const allRoles: Role[] = ['Admin', 'Contributor', 'Member']

const email = ref('')
const displayName = ref('')
const role = ref<Role>('Member')
const submitting = ref(false)
const error = ref<string | null>(null)
const success = ref<InviteResponseOk | null>(null)

async function submit() {
  if (submitting.value) return
  error.value = null
  success.value = null
  submitting.value = true
  try {
    const resp = await apiFetch('/members/invite', {
      method: 'POST',
      body: JSON.stringify({
        email: email.value.trim(),
        displayName: displayName.value.trim(),
        roles: [role.value],
      }),
    })
    if (!resp.ok) {
      const body = await resp.text()
      throw new Error(`${resp.status} ${resp.statusText}${body ? ` — ${body}` : ''}`)
    }
    success.value = await resp.json() as InviteResponseOk
    email.value = ''
    displayName.value = ''
    role.value = 'Member'
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="SLYPN administration"
    subtitle="Invite members, manage roles, and approve pending content."
  />

  <section class="mx-auto max-w-3xl space-y-6 px-6 py-16">
    <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <h2 class="font-display text-xl font-bold text-slypn-700">Invite a member</h2>
      <p class="mt-2 text-sm text-slypn-900/75">
        Sends an Entra External ID invitation. The recipient gets an email with a link
        to sign up; when they accept they&rsquo;ll be granted the role you choose.
      </p>

      <form class="mt-6 space-y-4" @submit.prevent="submit">
        <div>
          <label class="block text-sm font-medium text-slypn-800">Email</label>
          <input
            v-model="email"
            type="email"
            required
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-slypn-800">Display name</label>
          <input
            v-model="displayName"
            type="text"
            required
            class="mt-1 w-full rounded-md border border-slypn-200 bg-white px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
          />
        </div>
        <fieldset>
          <legend class="text-sm font-medium text-slypn-800">Role</legend>
          <div class="mt-2 inline-flex rounded-md border border-slypn-200 bg-white p-1">
            <button
              v-for="r in allRoles"
              :key="r"
              type="button"
              :class="[
                'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
                role === r
                  ? 'bg-slypn-600 text-white'
                  : 'text-slypn-700 hover:bg-slypn-50',
              ]"
              @click="role = r"
            >
              {{ r }}
            </button>
          </div>
        </fieldset>

        <button
          type="submit"
          class="rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
          :disabled="submitting || !email || !displayName"
        >
          {{ submitting ? 'Inviting…' : 'Send invitation' }}
        </button>

        <p v-if="error" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">{{ error }}</p>
        <div v-if="success" class="rounded-md bg-emerald-50 p-4 text-sm text-emerald-900">
          <p class="font-semibold">Invitation recorded for {{ success.member.email }}.</p>
          <p v-if="success.inviteSent" class="mt-1">Graph sent the invitation email.</p>
          <p v-else class="mt-1">
            Graph was skipped ({{ success.inviteReason }}) &mdash; the member record is in Cosmos but no email
            was sent. Configure <code class="rounded bg-emerald-100 px-1 text-emerald-900">Graph__*</code>
            settings on the API to enable.
          </p>
          <p v-if="success.redeemUrl" class="mt-1">
            Redeem URL: <a class="underline" :href="success.redeemUrl">{{ success.redeemUrl }}</a>
          </p>
        </div>
      </form>
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
