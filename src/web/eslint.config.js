import pluginVue from 'eslint-plugin-vue'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'

export default defineConfigWithVueTs(
  {
    name: 'slypn/files-to-lint',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },
  {
    name: 'slypn/files-to-ignore',
    // Playwright's report/trace output lands in the working tree after a run and
    // is gitignored; it must not be linted (it contains generated bundles).
    ignores: [
      '**/dist/**', '**/node_modules/**', '**/.vite/**',
      '**/playwright-report/**', '**/test-results/**', '**/blob-report/**',
      '**/*.cjs', '**/*.mjs',
    ],
  },
  pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,
  {
    name: 'slypn/rules',
    rules: {
      'vue/multi-word-component-names': 'off',
    },
  },
)
