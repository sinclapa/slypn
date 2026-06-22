import { defineConfig, devices } from '@playwright/test'

/**
 * E2E config. Boots the Vite dev server in dev-skip mode so tests can pick a
 * test persona (admin/contributor/member) via localStorage instead of signing
 * in through Entra. See e2e/helpers.ts.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'list' : 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      // Force the dev-skip escape hatch on regardless of local .env files.
      VITE_DEV_SKIP_AUTH: 'true',
    },
  },
})
