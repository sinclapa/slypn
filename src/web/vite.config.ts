import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'node:path'
import { readFileSync } from 'node:fs'

const pkg = JSON.parse(readFileSync(path.resolve(__dirname, 'package.json'), 'utf-8')) as { version: string }

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
  },
  build: {
    rollupOptions: {
      output: {
        // Split heavy vendor libs into their own chunks so the public bundle
        // (Home / Articles / Events / …) stays lean. MSAL is needed by the
        // nav user menu so it loads on every page, but parsing it as its own
        // chunk lets the HTTP cache reuse it across SPA navigations.
        manualChunks: {
          msal: ['@azure/msal-browser'],
          faro: ['@grafana/faro-web-sdk', '@grafana/faro-web-tracing'],
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:7071', changeOrigin: true },
    },
  },
})
