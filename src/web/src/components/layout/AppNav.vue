<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import logoUrl from '@/assets/logo.svg'
import { useAuthStore } from '@/stores/auth'
import { useApprovalsStore } from '@/stores/approvals'

const APP_ENV   = import.meta.env.VITE_FARO_ENV ?? 'dev'
const envLabel  = APP_ENV !== 'prod' ? APP_ENV : null
const envClass  = APP_ENV === 'local'
  ? 'bg-green-100 text-green-700 border-green-300'
  : 'bg-amber-100 text-amber-700 border-amber-300'

const navItems = [
  { to: '/',           label: 'Home' },
  { to: '/about',      label: 'About' },
  { to: '/articles',   label: 'Articles' },
  { to: '/blog',       label: 'Blog' },
  { to: '/events',     label: 'Events' },
  { to: '/resources',  label: 'Resources' },
  { to: '/newsletter', label: 'Newsletter' },
]

const auth = useAuthStore()
const approvalsStore = useApprovalsStore()
const router = useRouter()
const mobileOpen = ref(false)
const userMenuOpen = ref(false)

// Account/admin links, defined once and reused by the desktop dropdown and the
// mobile account panel. `dividerAfter` renders a separator; `badge` shows the
// pending-approvals count.
const accountLinks = computed(() => ([
  { to: '/dashboard',       label: 'Dashboard',          show: true, dividerAfter: true },
  { to: '/admin/approvals', label: 'Approvals',          show: auth.isAdmin, badge: true },
  { to: '/admin/content',   label: 'Content management', show: auth.isContributor || auth.isAdmin },
  { to: '/editor',          label: 'Editor',             show: auth.isContributor || auth.isAdmin },
  { to: '/admin/events',    label: 'Event management',   show: auth.isContributor || auth.isAdmin },
  { to: '/admin/members',   label: 'Members',            show: auth.isAdmin },
  { to: '/admin/subscribers', label: 'Newsletter subscribers', show: auth.isAdmin },
  { to: '/admin/resources', label: 'Resources',          show: auth.isAdmin },
  { to: '/admin/newsletters', label: 'Newsletters',      show: auth.isAdmin },
] as { to: string; label: string; show: boolean; dividerAfter?: boolean; badge?: boolean }[]).filter(l => l.show))

const envMenuOpen = ref(false)
const swaggerUrl = APP_ENV === 'local'
  ? 'http://localhost:7071/api/swagger/ui'
  : '/swagger.html'

// Mobile: the hamburger (primary nav) and the avatar (account) are separate
// panels — opening one closes the other. Both float over the page as an
// overlay (rather than pushing content down) so opening them never reflows
// whatever the user was looking at.
function toggleMobileNav() { mobileOpen.value = !mobileOpen.value; userMenuOpen.value = false }
function toggleAccount()   { userMenuOpen.value = !userMenuOpen.value; mobileOpen.value = false }
function closeMobilePanels() { mobileOpen.value = false; userMenuOpen.value = false }
function toggleEnvMenu()   { envMenuOpen.value = !envMenuOpen.value }
function closeEnvMenu()    { envMenuOpen.value = false }

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') closeMobilePanels()
}

onMounted(() => {
  if (auth.isAdmin) approvalsStore.refresh()
  window.addEventListener('keydown', onKeydown)
})
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
watch(() => auth.isAdmin, (isAdmin) => { if (isAdmin) approvalsStore.refresh() })

async function onSignIn() {
  if (!auth.isConfigured) {
    router.push({ name: 'login' })
    return
  }
  try {
    await auth.login()
  } catch (err) {
    console.error('login failed', err)
    router.push({ name: 'login' })
  }
}

async function onSignOut() {
  userMenuOpen.value = false
  mobileOpen.value = false
  try {
    await auth.logout()
  } catch (err) {
    console.error('logout failed', err)
  }
}
</script>

<template>
  <header class="sticky top-0 z-40 border-b border-slypn-100 bg-white/85 backdrop-blur">
    <div class="page-container flex items-center justify-between gap-6 py-4">
      <RouterLink
        to="/"
        class="flex items-center gap-2"
        aria-label="SLYPN — Home"
        @click="mobileOpen = false; userMenuOpen = false"
      >
        <img :src="logoUrl" alt="SLYPN" class="h-16 w-auto" width="684" height="488" />
      </RouterLink>

      <div v-if="envLabel" class="relative">
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded border px-2.5 py-1 font-mono text-xs font-semibold uppercase tracking-wide"
          :class="envClass"
          :aria-expanded="envMenuOpen"
          @click="toggleEnvMenu"
        >
          {{ envLabel }}
          <svg class="h-3 w-3" fill="none" stroke="currentColor" stroke-width="2.5" viewBox="0 0 24 24" aria-hidden="true">
            <path stroke-linecap="round" stroke-linejoin="round" d="M19 9l-7 7-7-7" />
          </svg>
        </button>
        <div
          v-if="envMenuOpen"
          class="absolute left-0 top-full mt-1 min-w-max rounded-md border border-slypn-100 bg-white py-1 shadow-lg"
          @mouseleave="closeEnvMenu"
        >
          <a
            :href="swaggerUrl"
            target="_blank"
            rel="noopener"
            class="block px-4 py-2 text-sm font-normal normal-case tracking-normal text-slypn-700 hover:bg-slypn-50"
            @click="closeEnvMenu"
          >API docs</a>
        </div>
      </div>

      <nav class="hidden items-center gap-1 md:flex" aria-label="Primary">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="rounded-md px-3 py-2 text-sm font-medium text-slypn-700 transition-colors hover:bg-slypn-50 hover:text-slypn-900"
          active-class="bg-slypn-50 text-slypn-900"
          exact-active-class="bg-slypn-100 text-slypn-900"
        >
          {{ item.label }}
        </RouterLink>
      </nav>

      <!-- Desktop: avatar dropdown / sign in -->
      <div class="relative hidden md:block">
        <button
          v-if="auth.isAuthenticated"
          type="button"
          data-testid="user-menu-trigger"
          class="flex items-center gap-2 rounded-full bg-slypn-50 px-3 py-1.5 text-sm font-semibold text-slypn-700 hover:bg-slypn-100"
          :aria-expanded="userMenuOpen"
          @click="userMenuOpen = !userMenuOpen"
        >
          <span class="grid h-7 w-7 place-items-center rounded-full bg-slypn-600 text-xs font-bold text-white">
            {{ auth.displayName.charAt(0).toUpperCase() }}
          </span>
          <span class="max-w-[10rem] truncate">{{ auth.displayName }}</span>
        </button>
        <button
          v-else
          type="button"
          class="rounded-md bg-slypn-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-slypn-700"
          @click="onSignIn"
        >
          Sign in
        </button>

        <div
          v-if="auth.isAuthenticated && userMenuOpen"
          class="absolute right-0 mt-2 w-56 overflow-hidden rounded-md border border-slypn-100 bg-white shadow-lg"
          @mouseleave="userMenuOpen = false"
        >
          <div class="border-b border-slypn-100 px-4 py-2 text-xs text-slypn-900/60">
            Signed in as
            <p class="mt-0.5 truncate font-medium text-slypn-900">{{ auth.account?.username }}</p>
          </div>
          <ul class="text-sm text-slypn-700">
            <template v-for="link in accountLinks" :key="link.to">
              <li>
                <RouterLink
                  :to="link.to"
                  data-testid="nav-account-link"
                  :data-to="link.to"
                  class="flex items-center justify-between px-4 py-2 hover:bg-slypn-50"
                  @click="userMenuOpen = false"
                >
                  {{ link.label }}
                  <span
                    v-if="link.badge && approvalsStore.pendingCount > 0"
                    data-testid="approvals-badge"
                    class="ml-2 rounded-full bg-amber-500 px-1.5 py-0.5 text-xs font-bold text-white"
                  >{{ approvalsStore.pendingCount }}</span>
                </RouterLink>
              </li>
              <li v-if="link.dividerAfter" aria-hidden="true"><hr class="my-1 border-t border-slypn-100" /></li>
            </template>
            <li>
              <button type="button" class="block w-full px-4 py-2 text-left hover:bg-slypn-50" @click="onSignOut">Sign out</button>
            </li>
          </ul>
        </div>
      </div>

      <!-- Mobile: avatar (account) + hamburger (primary nav) -->
      <div class="flex items-center gap-1 md:hidden">
        <button
          v-if="auth.isAuthenticated"
          type="button"
          class="rounded-full p-0.5 hover:bg-slypn-50"
          aria-label="Account menu"
          aria-controls="mobile-account"
          :aria-expanded="userMenuOpen"
          @click="toggleAccount"
        >
          <span class="grid h-9 w-9 place-items-center rounded-full bg-slypn-600 text-sm font-bold text-white">
            {{ auth.displayName.charAt(0).toUpperCase() }}
          </span>
        </button>
        <button
          type="button"
          class="inline-flex items-center justify-center rounded-md p-2 text-slypn-700 hover:bg-slypn-50"
          :aria-expanded="mobileOpen"
          aria-controls="mobile-nav"
          aria-label="Toggle navigation menu"
          @click="toggleMobileNav"
        >
          <svg v-if="!mobileOpen" class="h-6 w-6" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4 6h16M4 12h16M4 18h16" />
          </svg>
          <svg v-else class="h-6 w-6" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" d="M6 6l12 12M6 18L18 6" />
          </svg>
        </button>
      </div>
    </div>

    <!-- Mobile: primary navigation only -->
    <nav
      v-show="mobileOpen"
      id="mobile-nav"
      class="absolute inset-x-0 top-full z-40 max-h-[75vh] overflow-y-auto border-t border-slypn-100 bg-white shadow-lg md:hidden"
      aria-label="Mobile primary"
    >
      <div class="page-container flex flex-col gap-1 py-3">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="rounded-md px-3 py-2 text-base font-medium text-slypn-800 hover:bg-slypn-50"
          active-class="bg-slypn-50"
          @click="mobileOpen = false"
        >
          {{ item.label }}
        </RouterLink>
        <button type="button"
          v-if="!auth.isAuthenticated"
          class="mt-1 rounded-md bg-slypn-600 px-3 py-2 text-center text-base font-semibold text-white hover:bg-slypn-700"
          @click="mobileOpen = false; onSignIn()"
        >
          Sign in
        </button>
      </div>
    </nav>

    <!-- Mobile: signed-in account + admin tools -->
    <nav
      v-show="userMenuOpen && auth.isAuthenticated"
      id="mobile-account"
      class="absolute inset-x-0 top-full z-40 max-h-[75vh] overflow-y-auto border-t border-slypn-100 bg-white shadow-lg md:hidden"
      aria-label="Account"
    >
      <div class="page-container flex flex-col gap-1 py-3">
        <p class="px-3 pb-1 text-xs text-slypn-900/60">
          Signed in as <span class="font-medium text-slypn-900">{{ auth.account?.username }}</span>
        </p>
        <template v-for="link in accountLinks" :key="link.to">
          <RouterLink
            :to="link.to"
            data-testid="nav-account-link-mobile"
            :data-to="link.to"
            class="flex items-center justify-between rounded-md px-3 py-2 text-base font-medium text-slypn-800 hover:bg-slypn-50"
            active-class="bg-slypn-50"
            @click="userMenuOpen = false"
          >
            {{ link.label }}
            <span
              v-if="link.badge && approvalsStore.pendingCount > 0"
              class="ml-2 rounded-full bg-amber-500 px-1.5 py-0.5 text-xs font-bold text-white"
            >{{ approvalsStore.pendingCount }}</span>
          </RouterLink>
          <hr v-if="link.dividerAfter" class="my-1 border-t border-slypn-100" aria-hidden="true" />
        </template>
        <button type="button"
          class="mt-1 rounded-md border border-slypn-200 bg-white px-3 py-2 text-center text-base font-semibold text-slypn-700 hover:bg-slypn-50"
          @click="onSignOut"
        >
          Sign out
        </button>
      </div>
    </nav>
  </header>

  <!-- Backdrop lives outside <header> deliberately: header has backdrop-blur,
       and CSS filter/backdrop-filter makes an element the containing block for
       position:fixed descendants — nesting this inside header would trap it to
       the header's own (small) box instead of the full viewport, breaking both
       click-to-close and the intended page-only dimming. -->
  <div
    v-show="mobileOpen || userMenuOpen"
    class="fixed inset-0 z-30 bg-slypn-900/30 md:hidden"
    aria-hidden="true"
    @click="closeMobilePanels"
  />
</template>
