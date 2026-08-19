import { createApiClient } from './support/api-client'
import {
  assertLocalSettings,
  killAll,
  startAzurite,
  startFunc,
  tailLog,
  waitForApiHealthy,
} from './support/backend'
import { runId } from './support/ids'
import { ensureSeeded, sweepLeftovers } from './support/seed'

/**
 * Brings up the e2e backend and refuses to let the suite run without it.
 *
 * Ordering matters and is all in one place: Azurite, then the Functions host,
 * then a health probe, then the demo baseline, then a sweep of anything a
 * crashed run left behind. Playwright's `webServer` entries (the two Vite dev
 * servers) start before this, which is fine — Vite proxies /api lazily.
 *
 * Set E2E_START_BACKEND=0 to skip orchestration entirely and run against a
 * backend you started yourself (e.g. scripts/startLocal.ps1). The health probe
 * still runs, so "I forgot to start it" fails loudly rather than silently.
 */
export default async function globalSetup(): Promise<() => Promise<void>> {
  // globalSetup runs in the runner process before workers fork, so seeding the
  // run id here is how every worker ends up agreeing on it.
  const id = runId()
  process.stdout.write(`\n[e2e] run id ${id}\n`)

  const startBackend = process.env.E2E_START_BACKEND !== '0'
  let started = false

  if (await waitForApiHealthy(2_000)) {
    process.stdout.write('[e2e] reusing the API already listening on :7071\n')
  } else if (!startBackend) {
    throw new Error(
      'E2E_START_BACKEND=0 but nothing is answering on http://localhost:7071/api/events.\n' +
      'Start the backend with scripts/startLocal.ps1, or unset E2E_START_BACKEND.',
    )
  } else {
    assertLocalSettings()
    await startAzurite()
    startFunc()
    started = true

    // `dotnet run -c Release` compiles the project before the host starts, and
    // the worker JIT is cold on top of that, so the first successful response
    // can take a while on a clean checkout.
    const budget = process.env.CI ? 240_000 : 120_000
    if (!await waitForApiHealthy(budget)) {
      killAll()
      throw new Error(
        `The Functions host did not answer http://localhost:7071/api/events within ${budget / 1000}s.\n` +
        'The Worker SDK shells out to Core Tools, so `func` must be on PATH — check `func --version`.\n\n' +
        `--- api.log (tail) ---\n${tailLog('api')}`,
      )
    }
    process.stdout.write('[e2e] API healthy on :7071\n')
  }

  const admin = await createApiClient('admin')

  // The health probe only proves reads work. ContentRepository falls back to
  // read-only MockDataService when storage is unconfigured, and in that mode
  // every write returns 503 — which would show up as dozens of confusing
  // assertion failures instead of one clear message. Probe a write.
  const probe = await admin.put(`/drafts/e2eprobe${id}`, {
    type: 'article', title: 'E2E write probe', slug: '', summary: '',
    body: '', category: '', readingMinutes: 1,
  })
  // Read the body before anything else touches the context — an APIResponse is
  // only valid until the next request on it.
  const probeStatus = `${probe.status()} ${probe.statusText()}`
  const probeBody = probe.ok() ? '' : await probe.text().catch(() => '(body unavailable)')
  if (!probe.ok()) {
    await admin.dispose()
    killAll()
    throw new Error(
      `The API is up but rejected a write: ${probeStatus} — ${probeBody}\n\n` +
      'This normally means the API is serving read-only mock data because\n' +
      'Storage__ConnectionString is unset or Azurite is unreachable. The suite\n' +
      'would otherwise "pass" against data no test ever wrote.',
    )
  }
  await admin.del(`/drafts/e2eprobe${id}`)

  await ensureSeeded()

  const authors = await Promise.all(
    (['admin', 'admin2', 'contributor', 'contributor2'] as const).map(createApiClient),
  )
  const swept = await sweepLeftovers(admin, authors)
  if (swept > 0) process.stdout.write(`[e2e] swept ${swept} leftover row(s) from a previous run\n`)
  await Promise.all([admin.dispose(), ...authors.map((a) => a.dispose())])

  return async () => {
    if (started) killAll()
  }
}
