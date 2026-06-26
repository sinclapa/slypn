import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import faroUploader from '@grafana/faro-rollup-plugin'
import path from 'node:path'
import { readFileSync } from 'node:fs'

const pkg = JSON.parse(readFileSync(path.resolve(__dirname, 'package.json'), 'utf-8')) as { version: string }

// Source map upload is skipped when FARO_SOURCEMAP_ENDPOINT is unset (local dev,
// CI runs without the secret). Values come from Grafana Cloud → Frontend
// Observability → Settings → Source Maps → "Configure source map uploads".
const sourcemapEndpoint = process.env.FARO_SOURCEMAP_ENDPOINT

export default defineConfig({
  plugins: [
    vue(),
    ...(sourcemapEndpoint
      ? [
          faroUploader({
            appName: process.env.VITE_FARO_APP_NAME ?? 'slypn-web',
            endpoint: sourcemapEndpoint,
            apiKey: process.env.FARO_SOURCEMAP_API_KEY ?? '',
            appId: process.env.FARO_SOURCEMAP_APP_ID ?? '',
            stackId: process.env.FARO_SOURCEMAP_STACK_ID ?? '',
            bundleId: pkg.version,
            gzipContents: true,
            keepSourcemaps: false,
            gitHash: process.env.GITHUB_SHA,
          }),
        ]
      : []),
  ],
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
