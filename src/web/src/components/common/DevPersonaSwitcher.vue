<script setup lang="ts">
import { computed, ref } from 'vue'
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

// ── Corner placement (move the control off whatever you're testing) ───────────
const CORNERS = ['top-left', 'top-right', 'bottom-left', 'bottom-right'] as const
type Corner = typeof CORNERS[number]
const CORNER_KEY = 'slypn.devPersona.corner'

function readCorner(): Corner {
  try {
    const v = localStorage.getItem(CORNER_KEY)
    if (v && (CORNERS as readonly string[]).includes(v)) return v as Corner
  } catch { /* storage unavailable */ }
  return 'bottom-left'
}
const corner = ref<Corner>(readCorner())

function setCorner(c: Corner) {
  corner.value = c
  try { localStorage.setItem(CORNER_KEY, c) } catch { /* ignore */ }
}

// Four corner buttons (one row); the dot marks the position in each icon.
const cornerButtons: { key: Corner; dot: { cx: number; cy: number } }[] = [
  { key: 'top-left',     dot: { cx: 8,  cy: 8  } },
  { key: 'top-right',    dot: { cx: 16, cy: 8  } },
  { key: 'bottom-right', dot: { cx: 16, cy: 16 } },
  { key: 'bottom-left',  dot: { cx: 8,  cy: 16 } },
]

const positionClass = computed(() => ({
  'top-left':     'top-4 left-4',
  'top-right':    'top-4 right-4',
  'bottom-left':  'bottom-4 left-4',
  'bottom-right': 'bottom-4 right-4',
}[corner.value]))

// The dropdown opens away from the anchored edge.
const menuClass = computed(() => [
  corner.value.startsWith('top') ? 'top-full mt-2' : 'bottom-full mb-2',
  corner.value.endsWith('right') ? 'right-0' : 'left-0',
])
</script>

<template>
  <div
    v-if="isDevSkipAuth"
    data-testid="dev-persona-switcher"
    class="fixed z-50 font-mono text-xs"
    :class="positionClass"
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
      class="absolute w-64 overflow-hidden rounded-md border border-amber-200 bg-white shadow-lg"
      :class="menuClass"
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
            {{ p.name }} · {{ p.roles.join(', ') }}
          </span>
          <span class="pl-3 text-[10px] text-slypn-900/50">{{ p.username }}</span>
        </button>
      </li>

      <!-- Corner placement: four buttons on one row -->
      <li class="flex items-center justify-between gap-2 border-t border-amber-100 bg-amber-50/60 px-3 py-2">
        <span class="text-[10px] uppercase tracking-wide text-amber-700">Corner</span>
        <div class="flex gap-1">
          <button
            v-for="c in cornerButtons"
            :key="c.key"
            type="button"
            :data-testid="`dev-persona-corner-${c.key}`"
            :aria-label="`Move to ${c.key}`"
            :title="c.key"
            class="rounded border p-1"
            :class="corner === c.key
              ? 'border-amber-500 bg-amber-200 text-amber-900'
              : 'border-amber-200 bg-white text-amber-700 hover:bg-amber-100'"
            @click="setCorner(c.key)"
          >
            <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="18" height="18" rx="3" />
              <circle :cx="c.dot.cx" :cy="c.dot.cy" r="2.6" fill="currentColor" stroke="none" />
            </svg>
          </button>
        </div>
      </li>
    </ul>
  </div>
</template>
