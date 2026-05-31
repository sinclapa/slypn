import { watch } from 'vue'
import {
  initializeFaro,
  getWebInstrumentations,
  type Faro,
} from '@grafana/faro-web-sdk'
import { TracingInstrumentation } from '@grafana/faro-web-tracing'
import { useCookieConsent } from '@/composables/useCookieConsent'

const FARO_URL = import.meta.env.VITE_FARO_URL ?? ''
const APP_NAME = import.meta.env.VITE_FARO_APP_NAME ?? 'slypn-web'
const APP_ENV  = import.meta.env.VITE_FARO_ENV ?? 'dev'

export const isFaroConfigured = Boolean(FARO_URL)

let faro: Faro | null = null

function init() {
  if (faro || !isFaroConfigured) return
  try {
    faro = initializeFaro({
      url: FARO_URL,
      app: {
        name: APP_NAME,
        version: __APP_VERSION__,
        environment: APP_ENV,
      },
      instrumentations: [
        ...getWebInstrumentations({ captureConsole: true }),
        new TracingInstrumentation(),
      ],
      // Default sample rate is 1 (everything). Lower in production after we
      // see real volume — left at 1 for now since traffic is light.
    })
  } catch (err) {
    // Don't blow up the page if Faro can't start.
    console.warn('Faro init failed:', err)
  }
}

/**
 * Wire Faro to the cookie consent state. Faro **only** initialises once the
 * user has clicked Accept on the cookie banner. Declining (or being undecided)
 * keeps Faro completely inert — no script attempts to ship telemetry.
 */
export function setupFaro() {
  if (!isFaroConfigured) return
  const { choice } = useCookieConsent()
  if (choice.value === 'accepted') init()
  watch(choice, (next) => {
    if (next === 'accepted') init()
  })
}

export function getFaro(): Faro | null {
  return faro
}
