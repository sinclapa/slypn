import { randomBytes } from 'node:crypto'

/**
 * Run-scoped identifiers.
 *
 * The suite runs fully parallel against ONE shared Azurite instance, and the
 * demo seed already publishes 20 articles, so nothing may be located by
 * position ("the first row"). Every entity a test creates carries a unique
 * token in its title, and every locator filters on that token.
 *
 * The `E2E ` prefix is what sweepLeftovers() keys off to clean up after a
 * crashed run — keep it on every generated title.
 */
export const E2E_PREFIX = 'E2E '

/** Stable for the whole run. globalSetup seeds it so every worker agrees. */
export function runId(): string {
  process.env.E2E_RUN_ID ??= `${Date.now().toString(36)}${randomBytes(2).toString('hex')}`
  return process.env.E2E_RUN_ID
}

/**
 * Unique within the run: `<runId>-w<worker>-<random>`.
 *
 * The random suffix is not decoration. A per-process counter is not enough:
 * Playwright recycles worker processes (after a failure, and with
 * --repeat-each), and the replacement reuses the same `parallelIndex` while
 * restarting any module-level counter — so two live tests can end up sharing an
 * id, and every `filter({ hasText: uid })` locator then matches two rows and
 * fails on strict mode. The worker index is kept only because it makes failure
 * output easier to read.
 */
export function makeUid(parallelIndex: number): string {
  return `${runId()}-w${parallelIndex}-${randomBytes(3).toString('hex')}`
}

/** Title for a created entity, e.g. `E2E k3f9a1-w0-2 Lifecycle article`. */
export function titleFor(uid: string, label: string): string {
  return `${E2E_PREFIX}${uid} ${label}`
}

/**
 * Throwaway address for subscribe/invite tests. `.invalid` is reserved by
 * RFC 2606 and can never resolve, so nothing can accidentally be delivered.
 */
export function emailFor(uid: string): string {
  return `e2e-${uid}@example.invalid`
}
