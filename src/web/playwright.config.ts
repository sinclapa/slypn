import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end config. The suite drives a real browser against the real .NET
 * Functions host on real Azurite storage — `e2e/global-setup.ts` brings that
 * backend up (or reuses one you already started) and aborts the run if writes
 * do not work, so a pass can never mean "rendered mock data with no API".
 *
 * Two dev servers, because `isDevSkipAuth` in src/lib/msal.ts is resolved at
 * transform time and `useAuthStore.initialize()` short-circuits to a synthetic
 * account whenever it is true:
 *   :5173  VITE_DEV_SKIP_AUTH=true   — always signed in; persona chosen from
 *                                      localStorage (see e2e/support/fixtures.ts)
 *   :5174  VITE_DEV_SKIP_AUTH=false  — genuinely anonymous, so the
 *                                      /login?returnTo= guard path is reachable
 * Both proxy /api to :7071 via vite.config.ts, so both talk to the live API.
 */
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // Every worker shares one Functions host and one Azurite, so this scales with
  // contention rather than cores. 3 on CI (4-vCPU runners) was measured against
  // 2: the test phase is the largest single slice of the job, but the ceiling is
  // low — beyond roughly 20s faster the SonarCloud job becomes the critical path
  // instead, so there is nothing to win by pushing it higher.
  workers: process.env.CI ? 3 : 4,
  timeout: 60_000,
  expect: { timeout: 10_000 },

  // scripts/testLocal.ps1 overrides this from the CLI with `--reporter=line,json`
  // and sets PLAYWRIGHT_JSON_OUTPUT_NAME; a CLI reporter wins over this value,
  // so that contract keeps working. Don't replace this with a bare string.
  reporter: process.env.CI
    ? [['github'], ['list'], ['html', { open: 'never' }]]
    : [['list'], ['html', { open: 'never' }]],

  use: {
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },

  projects: [
    {
      name: 'app',
      testDir: './e2e/app',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
    },
    {
      name: 'anon',
      testDir: './e2e/anon',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5174' },
    },
  ],

  webServer: [
    {
      command: 'npm run dev -- --port 5173 --strictPort',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: { VITE_DEV_SKIP_AUTH: 'true' },
    },
    {
      command: 'npm run dev -- --port 5174 --strictPort',
      url: 'http://localhost:5174',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      // Auth is left unconfigured on purpose: msalInstance stays null,
      // initialize() leaves `account` null, and apiFetch omits the persona
      // header — a genuinely anonymous client.
      env: { VITE_DEV_SKIP_AUTH: 'false' },
    },
  ],
})
