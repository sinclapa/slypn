import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest'
import { ref, nextTick } from 'vue'

// initFaro in vi.hoisted so the @grafana mock factory can close over it at hoist time.
// choice is module-level because vi.mock factories are invoked lazily (when faro.ts is first
// imported inside beforeAll), by which point the top-level ref() call has already run.
const { initFaro } = vi.hoisted(() => ({
  initFaro: vi.fn().mockReturnValue({ api: { pushEvent: vi.fn() } }),
}))
const choice = ref<string | null>(null)

vi.mock('@grafana/faro-web-sdk', () => ({
  initializeFaro: initFaro,
  getWebInstrumentations: vi.fn().mockReturnValue([]),
  LogLevel: { DEBUG: 'debug' },
}))
vi.mock('@grafana/faro-web-tracing', () => ({
  TracingInstrumentation: class {},
}))
vi.mock('@/composables/useCookieConsent', () => ({
  useCookieConsent: () => ({ choice }),
}))

describe('faro (configured, immediate init when accepted)', () => {
  let isFaroConfigured: boolean
  let setupFaro: () => void
  let getFaro: () => unknown

  beforeAll(async () => {
    vi.stubEnv('VITE_FARO_URL', 'https://faro.example.com')
    vi.resetModules()
    choice.value = 'accepted'
    const mod = await import('./faro')
    isFaroConfigured = mod.isFaroConfigured
    setupFaro = mod.setupFaro
    getFaro = mod.getFaro
  })

  afterAll(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('reports configured when VITE_FARO_URL is set', () => {
    expect(isFaroConfigured).toBe(true)
  })

  it('setupFaro calls initializeFaro immediately when cookie choice is accepted', () => {
    initFaro.mockClear()
    setupFaro()
    expect(initFaro).toHaveBeenCalledTimes(1)
    expect(getFaro()).not.toBeNull()
  })
})

describe('faro (configured, deferred init via watch)', () => {
  let setupFaro2: () => void
  let getFaro2: () => unknown

  beforeAll(async () => {
    vi.stubEnv('VITE_FARO_URL', 'https://faro.example.com')
    vi.resetModules()
    initFaro.mockClear()
    choice.value = null
    const mod = await import('./faro')
    setupFaro2 = mod.setupFaro
    getFaro2 = mod.getFaro
  })

  afterAll(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    choice.value = null
  })

  it('does not call initializeFaro before cookie is accepted', () => {
    setupFaro2()
    expect(initFaro).not.toHaveBeenCalled()
    expect(getFaro2()).toBeNull()
  })

  it('initialises faro when choice changes to accepted (watch callback)', async () => {
    choice.value = 'accepted'
    await nextTick()
    expect(initFaro).toHaveBeenCalledTimes(1)
    expect(getFaro2()).not.toBeNull()
  })

  it('swallows errors from initializeFaro and warns', async () => {
    vi.resetModules()
    initFaro.mockClear()
    choice.value = null
    initFaro.mockImplementationOnce(() => { throw new Error('Faro unavailable') })
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const mod3 = await import('./faro')
    choice.value = 'accepted'
    mod3.setupFaro()
    expect(() => {}).not.toThrow()
    expect(warnSpy).toHaveBeenCalledWith('Faro init failed:', expect.any(Error))
    warnSpy.mockRestore()
  })
})
