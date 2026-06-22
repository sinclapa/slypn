<script setup lang="ts">
import { ref } from 'vue'
import { isDevSkipAuth } from '@/lib/msal'
import { useAuthStore } from '@/stores/auth'
import {
  DEV_PERSONA_LIST,
  getActivePersonaKey,
  type DevPersonaKey,
} from '@/lib/devPersonas'

// Local-dev only. Lets you (and Playwright) flip between the seeded test
// accounts without going through Entra sign-in.
const auth = useAuthStore()
const open = ref(false)
const activeKey = getActivePersonaKey()

function choose(key: DevPersonaKey) {
  open.value = false
  if (key === activeKey) return
  auth.setPersona(key) // persists + reloads
}
</script>

<template>
  <div
    v-if="isDevSkipAuth"
    data-testid="dev-persona-switcher"
    class="fixed bottom-4 left-4 z-50 font-mono text-xs"
  >
    <button
      type="button"
      data-testid="dev-persona-trigger"
      class="flex items-center gap-2 rounded-full border border-amber-400 bg-amber-50 px-3 py-1.5 font-semibold text-amber-900 shadow-md hover:bg-amber-100"
      :aria-expanded="open"
      @click="open = !open"
    >
      <span class="h-2 w-2 rounded-full bg-amber-500"></span>
      <span>DEV · {{ activeKey }}</span>
      <span class="text-amber-700">▾</span>
    </button>

    <ul
      v-if="open"
      class="absolute bottom-full left-0 mb-2 w-64 overflow-hidden rounded-md border border-amber-200 bg-white shadow-lg"
      @mouseleave="open = false"
    >
      <li class="border-b border-amber-100 bg-amber-50 px-3 py-1.5 text-[10px] uppercase tracking-wide text-amber-700">
        Switch test persona
      </li>
      <li v-for="p in DEV_PERSONA_LIST" :key="p.key">
        <button
          type="button"
          :data-testid="`dev-persona-${p.key}`"
          class="flex w-full flex-col items-start px-3 py-2 text-left hover:bg-amber-50"
          :class="p.key === activeKey ? 'bg-amber-50 font-semibold' : ''"
          @click="choose(p.key)"
        >
          <span class="flex items-center gap-1.5">
            <span
              class="h-1.5 w-1.5 rounded-full"
              :class="p.key === activeKey ? 'bg-amber-500' : 'bg-transparent'"
            ></span>
            {{ p.roles.join(', ') }}
          </span>
          <span class="pl-3 text-[10px] text-slypn-900/50">{{ p.username }}</span>
        </button>
      </li>
    </ul>
  </div>
</template>
