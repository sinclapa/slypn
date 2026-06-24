<script setup lang="ts">
import { computed, ref } from 'vue'
import HeroBanner from '@/components/common/HeroBanner.vue'
import { apiFetch, apiJson } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import { useAuthStore } from '@/stores/auth'

type Role = 'Admin' | 'Contributor' | 'Member'

interface Member {
  id: string
  email: string
  displayName: string
  roles: string[]
  status: string
  invitedAt: string
  oid?: string | null
  _etag?: string
}

interface InviteResponseOk {
  member: Member
  inviteSent: boolean
  redeemUrl: string | null
  inviteReason: string | null
}

const auth = useAuthStore()
const allRoles: Role[] = ['Admin', 'Contributor', 'Member']

// ── Member list ────────────────────────────────────────────────────────────

const { data: members, loading, error, refresh } = useAsyncData(
  () => apiJson<Member[]>('/members'),
)

// Display members alphabetically by name (case-insensitive).
const sortedMembers = computed(() =>
  [...(members.value ?? [])].sort((a, b) =>
    a.displayName.localeCompare(b.displayName, 'en', { sensitivity: 'base' })))

// ── Role update ────────────────────────────────────────────────────────────

const savingId = ref<string | null>(null)
const saveError = ref<string | null>(null)

async function setRole(member: Member, role: Role) {
  if (savingId.value) return
  saveError.value = null
  savingId.value = member.id
  try {
    const resp = await apiFetch(`/members/${member.id}`, {
      method: 'PATCH',
      body: JSON.stringify({ roles: [role] }),
      headers: member._etag ? { 'If-Match': member._etag } : {},
    })
    if (!resp.ok) throw new Error(`${resp.status} ${await resp.text()}`)
    await refresh()
  } catch (err) {
    saveError.value = err instanceof Error ? err.message : String(err)
  } finally {
    savingId.value = null
  }
}

// ── Delete ─────────────────────────────────────────────────────────────────

const deletingId = ref<string | null>(null)

async function deleteMember(member: Member) {
  if (!confirm(`Remove ${member.displayName} (${member.email})? This cannot be undone.`)) return
  if (deletingId.value) return
  deletingId.value = member.id
  try {
    const resp = await apiFetch(`/members/${member.id}`, {
      method: 'DELETE',
      headers: member._etag ? { 'If-Match': member._etag } : {},
    })
    if (!resp.ok) throw new Error(`${resp.status} ${await resp.text()}`)
    await refresh()
  } catch (err) {
    saveError.value = err instanceof Error ? err.message : String(err)
  } finally {
    deletingId.value = null
  }
}

// ── Invite form ────────────────────────────────────────────────────────────

const showInvite  = ref(false)
const invEmail    = ref('')
const invName     = ref('')
const invRole     = ref<Role>('Member')
const invSubmitting = ref(false)
const invError    = ref<string | null>(null)
const invSuccess  = ref<InviteResponseOk | null>(null)

async function submitInvite() {
  if (invSubmitting.value) return
  invError.value   = null
  invSuccess.value = null
  invSubmitting.value = true
  try {
    const resp = await apiFetch('/members/invite', {
      method: 'POST',
      body: JSON.stringify({
        email: invEmail.value.trim(),
        displayName: invName.value.trim(),
        roles: [invRole.value],
      }),
    })
    if (!resp.ok) throw new Error(`${resp.status} ${resp.statusText} — ${await resp.text()}`)
    invSuccess.value = await resp.json() as InviteResponseOk
    invEmail.value = ''
    invName.value  = ''
    invRole.value  = 'Member'
    await refresh()
  } catch (err) {
    invError.value = err instanceof Error ? err.message : String(err)
  } finally {
    invSubmitting.value = false
  }
}

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
</script>

<template>
  <HeroBanner
    eyebrow="Admin"
    title="Members"
    subtitle="View all members, change roles, and invite new people."
  >
    <template #actions>
      <RouterLink
        :to="{ name: 'dashboard' }"
        class="text-sm font-semibold text-white/80 hover:text-white"
      >&larr; Dashboard</RouterLink>
    </template>
  </HeroBanner>

  <section class="page-container space-y-6 py-16">

    <!-- Invite panel -->
    <article class="rounded-xl border border-slypn-100 bg-white p-6 shadow-sm">
      <button
        type="button"
        class="flex w-full items-center justify-between text-left"
        @click="showInvite = !showInvite; invSuccess = null; invError = null"
      >
        <h2 class="font-display text-xl font-bold text-slypn-700">Invite a member</h2>
        <span class="text-slypn-400">{{ showInvite ? '▲' : '▼' }}</span>
      </button>

      <div v-if="showInvite" class="mt-6">
        <p class="text-sm text-slypn-900/75">
          Saves the member record and gives you a sign-up link to share with them directly.
          They visit the link, click Sign in, and create their account with email and password.
        </p>

        <form class="mt-4 space-y-4" @submit.prevent="submitInvite">
          <div class="space-y-4">
            <div>
              <label class="block text-sm font-medium text-slypn-800">Email</label>
              <input
                v-model="invEmail"
                type="email"
                required
                class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>
            <div>
              <label class="block text-sm font-medium text-slypn-800">Display name</label>
              <input
                v-model="invName"
                type="text"
                required
                class="mt-1 w-full rounded-md border border-slypn-200 px-3 py-2 text-sm shadow-sm focus:border-slypn-600 focus:outline-none focus:ring-1 focus:ring-slypn-600"
              />
            </div>
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
                  invRole === r ? 'bg-slypn-600 text-white' : 'text-slypn-700 hover:bg-slypn-50',
                ]"
                @click="invRole = r"
              >{{ r }}</button>
            </div>
          </fieldset>

          <button
            type="submit"
            class="rounded-md bg-slypn-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-slypn-700 disabled:opacity-50"
            :disabled="invSubmitting || !invEmail || !invName"
          >{{ invSubmitting ? 'Inviting…' : 'Send invitation' }}</button>

          <p v-if="invError" class="rounded-md bg-rose-50 px-4 py-2 text-sm text-rose-700">{{ invError }}</p>

          <div v-if="invSuccess" class="rounded-md bg-emerald-50 p-4 text-sm text-emerald-900">
            <p class="font-semibold">Member {{ invSuccess.member.email }} has been saved.</p>
            <template v-if="invSuccess.redeemUrl">
              <p class="mt-2">Share this sign-up link with them:</p>
              <p class="mt-1 break-all rounded bg-emerald-100 px-2 py-1 font-mono text-xs">
                <a class="underline" :href="invSuccess.redeemUrl" target="_blank" rel="noopener">{{ invSuccess.redeemUrl }}</a>
              </p>
              <p class="mt-2 text-emerald-700">They should click <strong>Sign in</strong> and create an account with their email address and a password.</p>
            </template>
            <p v-else class="mt-1 text-amber-700">
              Sign-up URL not configured — member saved. Share the site address with them manually.
            </p>
          </div>
        </form>
      </div>
    </article>

    <!-- Member list -->
    <article class="rounded-xl border border-slypn-100 bg-white shadow-sm">
      <div class="border-b border-slypn-100 px-6 py-4">
        <h2 class="font-display text-xl font-bold text-slypn-700">All members</h2>
      </div>

      <p v-if="loading && !members" class="px-6 py-8 text-center text-sm text-slypn-900/60">
        Loading members…
      </p>

      <div v-else-if="error" class="px-6 py-4 text-sm text-rose-700">
        Couldn't load members: {{ error }}.
        <button class="ml-2 underline" @click="refresh">Retry</button>
      </div>

      <p v-else-if="saveError" class="px-6 py-3 text-sm text-rose-700 bg-rose-50">
        {{ saveError }}
        <button class="ml-2 underline" @click="saveError = null">Dismiss</button>
      </p>

      <div v-if="members?.length" class="divide-y divide-slypn-100">
        <div
          v-for="m in sortedMembers"
          :key="m.id"
          class="flex flex-col gap-3 px-6 py-4 sm:flex-row sm:items-center sm:gap-6"
        >
          <!-- Identity -->
          <div class="min-w-0 flex-1">
            <p class="truncate font-semibold text-slypn-800">{{ m.displayName }}</p>
            <p class="mt-0.5 truncate text-sm text-slypn-500">{{ m.email }}</p>
            <p class="mt-0.5 text-xs text-slypn-400">Invited {{ fmtDate(m.invitedAt) }}</p>
          </div>

          <!-- Status badge -->
          <span
            :class="[
              'shrink-0 self-start rounded-full px-2.5 py-0.5 text-xs font-semibold sm:self-auto',
              m.status === 'active' ? 'bg-emerald-100 text-emerald-700' : 'bg-amber-100 text-amber-700',
            ]"
          >{{ m.status }}</span>

          <!-- Role picker -->
          <div class="inline-flex shrink-0 rounded-md border border-slypn-200 bg-white p-0.5">
            <button
              v-for="r in allRoles"
              :key="r"
              type="button"
              :disabled="savingId === m.id || deletingId === m.id"
              :class="[
                'rounded-md px-3 py-1 text-xs font-medium transition-colors disabled:opacity-50',
                m.roles.includes(r)
                  ? 'bg-slypn-600 text-white'
                  : 'text-slypn-700 hover:bg-slypn-50',
              ]"
              @click="setRole(m, r)"
            >
              <span v-if="savingId === m.id && m.roles.includes(r)">…</span>
              <span v-else>{{ r }}</span>
            </button>
          </div>

          <!-- Delete -->
          <button
            type="button"
            :disabled="savingId === m.id || deletingId === m.id || m.oid === auth.oid"
            :title="m.oid === auth.oid ? 'You cannot remove yourself' : undefined"
            class="shrink-0 rounded-md border border-rose-200 px-3 py-1.5 text-xs font-medium text-rose-600 hover:bg-rose-50 disabled:opacity-50"
            @click="deleteMember(m)"
          >{{ deletingId === m.id ? 'Removing…' : 'Remove' }}</button>
        </div>
      </div>

      <p v-else-if="members" class="px-6 py-8 text-center text-sm text-slypn-900/60">
        No members yet.
      </p>
    </article>

  </section>
</template>
