import js from '@eslint/js'
import { defineConfig } from 'eslint/config'
import prettier from 'eslint-config-prettier'
import vue from 'eslint-plugin-vue'
import globals from 'globals'
import ts from 'typescript-eslint'

export default defineConfig(
  { ignores: ['dist/**', 'node_modules/**', 'coverage/**'] },
  {
    files: ['**/*.{ts,vue}'],
    extends: [
      js.configs.recommended,
      ts.configs.recommendedTypeChecked,
      vue.configs['flat/recommended'],
    ],
    languageOptions: {
      globals: globals.browser,
      parserOptions: {
        parser: ts.parser,
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
        extraFileExtensions: ['.vue'],
      },
    },
    rules: {
      'prefer-const': 'error',
      '@typescript-eslint/consistent-type-imports': 'error',
      '@typescript-eslint/prefer-readonly': 'error',
      'vue/multi-word-component-names': ['error', { ignores: ['App'] }],
    },
  },
  {
    files: ['*.js'],
    extends: [js.configs.recommended],
    languageOptions: { globals: globals.node },
  },
  prettier,
)
