import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import type { ApiClient } from './api-client'
import { API_BASE, SEED_DIR, SEED_DOCX, isWindows, storageConnectionString } from './backend'
import { E2E_PREFIX } from './ids'

/**
 * Baseline data + leftover cleanup.
 *
 * The `--demo` seed is deterministic and idempotent (ids `seedart01..10`,
 * `seedblog01..10`, `seedres01..05`, `evt-coffee-<date>`), so the suite treats
 * it as a READ-ONLY baseline: public-browsing specs assert against it, and
 * nothing ever mutates it. Anything a test creates carries the E2E_PREFIX and
 * is cleaned up by its own fixture.
 */

interface SeedableArticle { id: string; title: string; status?: string }

/** True once the demo content is present. */
async function alreadySeeded(): Promise<boolean> {
  try {
    const resp = await fetch(`${API_BASE}/articles?status=published`)
    if (!resp.ok) return false
    const articles = await resp.json() as SeedableArticle[]
    return articles.some((a) => a.id === 'seedart01')
  } catch {
    return false
  }
}

/**
 * Run the Slypn.Seed CLI with --demo, unless the baseline is already there.
 * Skipping keeps local reruns instant; on CI it costs a few seconds once.
 */
export async function ensureSeeded(): Promise<void> {
  if (await alreadySeeded()) return

  if (!existsSync(SEED_DOCX)) {
    throw new Error(`Seed document missing: ${SEED_DOCX}`)
  }
  const connectionString = storageConnectionString()

  await new Promise<void>((resolve, reject) => {
    const child = spawn(
      isWindows() ? 'dotnet.exe' : 'dotnet',
      [
        'run', '--configuration', 'Release', '--no-launch-profile', '--',
        SEED_DOCX, '--connection-string', connectionString, '--demo',
      ],
      { cwd: SEED_DIR, stdio: 'inherit', shell: false },
    )
    child.once('error', reject)
    child.once('exit', (code) => {
      if (code === 0) resolve()
      else reject(new Error(`Slypn.Seed exited with code ${code}. See the output above.`))
    })
  })
}

/**
 * Best-effort deletion of anything a previous crashed run left behind.
 *
 * Without this, an interrupted run leaves E2E-prefixed rows in the approvals
 * queue and the published list, where they poison the next run's "filter by my
 * uid" assumptions and inflate every list. Cheap (~10 requests) and makes the
 * suite self-healing.
 */
export async function sweepLeftovers(admin: ApiClient, authors: ApiClient[]): Promise<number> {
  let removed = 0
  const isOurs = (title?: string) => Boolean(title?.startsWith(E2E_PREFIX))

  // Only the two statuses the suite can actually create: publishAuthoredArticle
  // leaves items published, submitDraft leaves them in-review. Nothing produces
  // draft or rejected articles — those partitions exist in the model but no
  // endpoint writes them. In-review is read through the role-gated route now;
  // the public list is pinned to published and ignores ?status=.
  const sweepSources: [string, string][] = [
    ['published', '/articles'],
    ['published', '/blog'],
    ['in-review', '/review/articles'],
    ['in-review', '/review/blog'],
  ]
  for (const [status, path] of sweepSources) {
    const items = await safeJson<SeedableArticle>(admin, path)
    for (const item of items.filter((i) => isOurs(i.title))) {
      await admin.del(`/content/${item.id}?status=${status}`)
      removed += 1
    }
  }

  for (const author of authors) {
    const drafts = await safeJson<SeedableArticle>(author, '/drafts')
    for (const draft of drafts.filter((d) => isOurs(d.title))) {
      await author.del(`/drafts/${draft.id}`)
      removed += 1
    }
  }

  const events = await safeJson<{ id: string; title: string }>(admin, '/events')
  for (const event of events.filter((e) => isOurs(e.title))) {
    await admin.del(`/events/${event.id}`)
    removed += 1
  }

  const resources = await safeJson<{ id: string; title: string; category: string }>(admin, '/resources')
  for (const resource of resources.filter((r) => isOurs(r.title))) {
    await admin.del(`/resources/${resource.id}?category=${encodeURIComponent(resource.category)}`)
    removed += 1
  }

  const newsletters = await safeJson<{ id: string; title: string }>(admin, '/newsletters')
  for (const newsletter of newsletters.filter((n) => isOurs(n.title))) {
    await admin.del(`/newsletters/${newsletter.id}`)
    removed += 1
  }

  // Only e2e-generated addresses. The dev-persona rows seeded by
  // TableBootstrapper must survive — every spec depends on them.
  const members = await safeJson<{ id: string; email: string }>(admin, '/members')
  for (const member of members.filter((m) => m.email?.endsWith('@example.invalid'))) {
    await admin.del(`/members/${member.id}`)
    removed += 1
  }

  return removed
}

/** GET a list endpoint, treating any failure as "nothing to clean up". */
async function safeJson<T>(api: ApiClient, path: string): Promise<T[]> {
  try {
    const resp = await api.get(path)
    if (!resp.ok()) return []
    return await resp.json() as T[]
  } catch {
    return []
  }
}
