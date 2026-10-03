/// <reference types="vite/client" />

// The ESLint TypeScript service needs a module shape for Vue imports.
// vue-tsc independently checks the actual component scripts and templates.
declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent
  export default component
}
