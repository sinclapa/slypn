import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

// Unit/component tests for the Vue app. Kept separate from the Playwright e2e
// suite in ./e2e (those use @playwright/test and are excluded here via `include`).
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  test: {
    // threads avoids the Windows EPERM/timeout issue with the forks pool.
    pool: 'threads',
    environment: 'happy-dom',
    include: ['src/**/*.{test,spec}.ts'],
    setupFiles: ['./src/test/setup.ts'],
    // Run unit tests as if the dev-skip persona switcher is active.
    env: { VITE_DEV_SKIP_AUTH: 'true' },
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,vue}'],
      exclude: [
        'src/**/*.{test,spec}.ts',
        'src/test/**',
        'src/main.ts',
        'src/env.d.ts',
        'src/**/*.d.ts',
      ],
    },
  },
})
