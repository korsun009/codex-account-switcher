import { describe, expect, it } from 'vitest'
import { createWindowOptions, isTrustedSender } from '../../src/main/window-security'

describe('window security', () => {
  it('enforces renderer isolation and sandboxing', () => {
    const options = createWindowOptions('C:\\app\\preload.js')
    expect(options.webPreferences).toMatchObject({
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      allowRunningInsecureContent: false
    })
  })

  it('uses the persisted theme color before the renderer is shown', () => {
    expect(createWindowOptions('C:\\app\\preload.js', '#f3f5f2').backgroundColor).toBe('#f3f5f2')
  })

  it('accepts only the exact renderer entry and ignores hash routing', () => {
    expect(isTrustedSender('file:///C:/app/renderer/index.html#accounts', 'file:///C:/app/renderer/index.html')).toBe(true)
    expect(isTrustedSender('file:///C:/app/renderer/other.html', 'file:///C:/app/renderer/index.html')).toBe(false)
    expect(isTrustedSender('https://evil.example/index.html', 'file:///C:/app/renderer/index.html')).toBe(false)
  })
})
