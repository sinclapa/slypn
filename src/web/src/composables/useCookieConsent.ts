import { ref } from 'vue'

export type ConsentChoice = 'accepted' | 'declined'
const STORAGE_KEY = 'slypn:cookie-consent'

const stored = (() => {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    return v === 'accepted' || v === 'declined' ? (v as ConsentChoice) : null
  } catch {
    return null
  }
})()

const choice = ref<ConsentChoice | null>(stored)

function persist(next: ConsentChoice) {
  choice.value = next
  try {
    localStorage.setItem(STORAGE_KEY, next)
  } catch {
    /* storage blocked — keep in-memory state only */
  }
}

function reset() {
  choice.value = null
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    /* storage blocked */
  }
}

export function useCookieConsent() {
  return {
    choice,
    accept: () => persist('accepted'),
    decline: () => persist('declined'),
    reset,
  }
}
