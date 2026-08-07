import type { BrowserWindowConstructorOptions } from 'electron'

export function createWindowOptions(preloadPath: string, backgroundColor = '#0d0f0e'): BrowserWindowConstructorOptions {
  return {
    width: 1360,
    height: 820,
    minWidth: 980,
    minHeight: 660,
    show: false,
    autoHideMenuBar: true,
    backgroundColor,
    title: 'Codex Account Switcher',
    webPreferences: {
      preload: preloadPath,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      allowRunningInsecureContent: false,
      spellcheck: false
    }
  }
}

export function isTrustedSender(senderUrl: string, trustedEntryUrl: string): boolean {
  try {
    const sender = new URL(senderUrl)
    const trusted = new URL(trustedEntryUrl)
    return sender.protocol === trusted.protocol &&
      sender.host === trusted.host &&
      sender.pathname === trusted.pathname &&
      sender.search === trusted.search
  } catch {
    return false
  }
}
