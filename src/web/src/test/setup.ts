// Global test setup: stub browser APIs happy-dom doesn't implement so views
// that use them (infinite-scroll sentinels, etc.) can mount.
import { vi } from 'vitest'

class IntersectionObserverStub {
  observe = vi.fn()
  unobserve = vi.fn()
  disconnect = vi.fn()
  takeRecords = vi.fn(() => [])
  root = null
  rootMargin = ''
  thresholds = []
}

vi.stubGlobal('IntersectionObserver', IntersectionObserverStub)
