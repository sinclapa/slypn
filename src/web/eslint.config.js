import pluginVue from 'eslint-plugin-vue'
import vueTsEslintConfig from '@vue/eslint-config-typescript'

export default [
  {
    name: 'slypn/files-to-lint',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },
  {
    name: 'slypn/files-to-ignore',
    ignores: ['**/dist/**', '**/node_modules/**', '**/.vite/**', '**/*.cjs', '**/*.mjs'],
  },
  ...pluginVue.configs['flat/essential'],
  ...vueTsEslintConfig(),
  {
    name: 'slypn/rules',
    rules: {
      'vue/multi-word-component-names': 'off',
    },
  },
]
