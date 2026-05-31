/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_MSAL_AUTHORITY?: string
  readonly VITE_MSAL_CLIENT_ID?: string
  readonly VITE_API_SCOPE?: string
  readonly VITE_FARO_URL?: string
  readonly VITE_FARO_APP_NAME?: string
  readonly VITE_FARO_ENV?: string
  readonly VITE_DEV_SKIP_AUTH?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

/** Injected by Vite at build time — the web package's version from package.json. */
declare const __APP_VERSION__: string
