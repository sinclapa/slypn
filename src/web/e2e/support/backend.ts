/**
 * Process orchestration for the e2e backend: Azurite (blob/queue/table) and the
 * .NET Functions host. Knows nothing about tests.
 *
 * This exists because the suite is only meaningful against a live API. The old
 * PowerShell path (`Start-E2eBackend` in scripts/_lib.ps1) warned and continued
 * when Docker or `func` was missing, so a "passing" e2e run could mean the
 * browser had rendered MockDataService output with no backend at all. Every
 * failure path here throws.
 */
import { spawn, type ChildProcess } from 'node:child_process'
import { copyFileSync, createWriteStream, existsSync, mkdirSync, readFileSync } from 'node:fs'
import { createConnection } from 'node:net'
import os from 'node:os'
import path from 'node:path'

export const API_ORIGIN = 'http://localhost:7071'
export const API_BASE = `${API_ORIGIN}/api`

const REPO_ROOT = path.resolve(import.meta.dirname, '../../../..')
export const API_DIR = path.join(REPO_ROOT, 'src/api/Slypn.Api')
export const SEED_DIR = path.join(REPO_ROOT, 'src/api/Slypn.Seed')
export const SEED_DOCX = path.join(REPO_ROOT, 'brief/SLYPN_Newsletter_MAY_2026.docx')

const AZURITE_BLOB_PORT = 10_000
const AZURITE_QUEUE_PORT = 10_001
const AZURITE_TABLE_PORT = 10_002

/** Where func/azurite stdout lands. testLocal.ps1 points this at .testresults/. */
export function logDir(): string {
  const dir = process.env.E2E_LOG_DIR ?? path.join(REPO_ROOT, '.testresults/e2e-backend')
  mkdirSync(dir, { recursive: true })
  return dir
}

export function isWindows(): boolean {
  return process.platform === 'win32'
}

/** Resolve a `func` executable, mirroring the lookup in scripts/_lib.ps1. */
export function resolveFunc(): string {
  const localAppData = process.env.LOCALAPPDATA
  if (isWindows() && localAppData) {
    const bundled = path.join(localAppData, 'AzureFunctionsCoreTools', 'func.exe')
    if (existsSync(bundled)) return bundled
  }
  // Fall back to PATH; if it isn't there the spawn errors and we surface it.
  return isWindows() ? 'func.cmd' : 'func'
}

export function tcpPortOpen(port: number, host = '127.0.0.1'): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = createConnection({ port, host })
    const done = (open: boolean) => {
      socket.destroy()
      resolve(open)
    }
    socket.setTimeout(1_000)
    socket.once('connect', () => done(true))
    socket.once('timeout', () => done(false))
    socket.once('error', () => done(false))
  })
}

// ── lifecycle ────────────────────────────────────────────────────────────────

const spawned: ChildProcess[] = []
let cleanupHooked = false

function registerForCleanup(child: ChildProcess) {
  spawned.push(child)
  if (cleanupHooked) return
  cleanupHooked = true
  process.once('exit', killAll)
  process.once('SIGINT', () => { killAll(); process.exit(130) })
  process.once('SIGTERM', () => { killAll(); process.exit(143) })
}

/**
 * Kill everything we started. On Windows `func` forks a `dotnet` child that
 * survives a plain kill and keeps port 7071 bound, so use taskkill /T.
 */
export function killAll(): void {
  while (spawned.length > 0) {
    const child = spawned.pop()
    if (!child?.pid || child.exitCode !== null) continue
    try {
      if (isWindows()) {
        spawn('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' })
      } else {
        process.kill(-child.pid, 'SIGTERM')
      }
    } catch {
      /* already gone */
    }
  }
}

/**
 * Spawn a child with its output tee'd to a log file. Detached on posix so the
 * whole process group can be signalled at once.
 */
function spawnLogged(
  name: string,
  command: string,
  args: string[],
  cwd: string,
): ChildProcess {
  const stream = createWriteStream(path.join(logDir(), `${name}.log`), { flags: 'w' })
  const child = spawn(command, args, {
    cwd,
    env: process.env,
    detached: !isWindows(),
    shell: false,
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  child.stdout?.pipe(stream)
  child.stderr?.pipe(stream)
  child.once('error', (err) => stream.write(`\n[${name}] spawn error: ${err.message}\n`))
  registerForCleanup(child)
  return child
}

/** Last N lines of a child's log, for error messages. */
export function tailLog(name: string, lines = 60): string {
  const file = path.join(logDir(), `${name}.log`)
  if (!existsSync(file)) return `(no log at ${file})`
  return readFileSync(file, 'utf8').split(/\r?\n/).slice(-lines).join('\n')
}

async function waitForPort(port: number, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (await tcpPortOpen(port)) return true
    await new Promise((resolve) => setTimeout(resolve, 300))
  }
  return false
}

// ── azurite ──────────────────────────────────────────────────────────────────

/**
 * Start Azurite unless something is already listening — which covers the
 * `slypn-azurite` Docker container that scripts/startLocal.ps1 runs.
 *
 * The host MUST be 127.0.0.1: the BlobEndpoint in local.settings.sample.json is
 * http://127.0.0.1:10000/..., and media SAS URLs are handed to the browser
 * verbatim, so a mismatched bind address breaks the image assertions.
 */
export async function startAzurite(): Promise<ChildProcess | null> {
  if (await tcpPortOpen(AZURITE_TABLE_PORT)) return null

  const location = path.join(os.tmpdir(), 'slypn-e2e-azurite')
  mkdirSync(location, { recursive: true })

  const child = spawnLogged(
    'azurite',
    isWindows() ? 'npx.cmd' : 'npx',
    [
      'azurite',
      '--silent',
      '--location', location,
      '--blobHost', '127.0.0.1', '--blobPort', String(AZURITE_BLOB_PORT),
      '--queueHost', '127.0.0.1', '--queuePort', String(AZURITE_QUEUE_PORT),
      '--tableHost', '127.0.0.1', '--tablePort', String(AZURITE_TABLE_PORT),
    ],
    REPO_ROOT,
  )

  if (!await waitForPort(AZURITE_TABLE_PORT, 60_000)) {
    throw new Error(
      `Azurite did not open port ${AZURITE_TABLE_PORT} within 60s.\n` +
      'Run `npm ci` in src/web (azurite is a devDependency), or start the Docker\n' +
      `emulator with scripts/startLocal.ps1.\n\n--- azurite.log ---\n${tailLog('azurite')}`,
    )
  }
  return child
}

// ── functions host ───────────────────────────────────────────────────────────

/**
 * Ensure local.settings.json exists, then assert it points at a storage
 * emulator with auth skipped. Without both, the API degrades silently:
 * no connection string means ContentRepository serves read-only MockDataService
 * output, and SkipAuth=false means every persona request 401s.
 */
export function assertLocalSettings(): void {
  const settingsPath = path.join(API_DIR, 'local.settings.json')
  if (!existsSync(settingsPath)) {
    const sample = path.join(API_DIR, 'local.settings.sample.json')
    if (!existsSync(sample)) {
      throw new Error(`Neither local.settings.json nor local.settings.sample.json exists in ${API_DIR}`)
    }
    copyFileSync(sample, settingsPath)
  }

  const values = readSettings()

  if (!values.Storage__ConnectionString) {
    throw new Error(
      `${settingsPath} has no Storage__ConnectionString.\n` +
      'The API would fall back to read-only mock data and every write in the e2e\n' +
      'suite would fail with 503. Copy the value from local.settings.sample.json.',
    )
  }
  if (values.AzureAd__SkipAuth !== 'true') {
    throw new Error(
      `${settingsPath} has AzureAd__SkipAuth="${values.AzureAd__SkipAuth}".\n` +
      'The e2e suite authenticates with the X-Slypn-Dev-User persona header, which\n' +
      'JwtMiddleware only honours when SkipAuth is true. Set it to "true".',
    )
  }
}

function readSettings(): Record<string, string> {
  const settingsPath = path.join(API_DIR, 'local.settings.json')
  const parsed = JSON.parse(readFileSync(settingsPath, 'utf8')) as { Values?: Record<string, string> }
  return parsed.Values ?? {}
}

export function storageConnectionString(): string {
  return readSettings().Storage__ConnectionString ?? ''
}

export function startFunc(): ChildProcess {
  return spawnLogged('api', resolveFunc(), ['start', '--port', '7071'], API_DIR)
}

/**
 * Poll a public endpoint that reads Table Storage, so a 200 proves the host is
 * up AND storage is reachable — and warms the cold-start JIT while we wait.
 */
export async function waitForApiHealthy(timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      const resp = await fetch(`${API_BASE}/events`, { signal: AbortSignal.timeout(5_000) })
      if (resp.ok) return true
    } catch {
      /* not up yet */
    }
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  return false
}
