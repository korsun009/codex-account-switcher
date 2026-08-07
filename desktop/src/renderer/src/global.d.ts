import type { DesktopApi } from '../../shared/contracts'

declare module '*.css'

declare global {
  interface Window {
    codexSwitcher: DesktopApi
  }
}

export {}
