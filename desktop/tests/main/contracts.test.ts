import { describe, expect, it } from 'vitest'
import { backendCommands, isBackendCommand } from '../../src/shared/contracts'
import { allowedIpcChannels } from '../../src/main/ipc'
import { backendCommandTimeout } from '../../src/shared/timeouts'

describe('desktop contracts', () => {
  it('uses a fixed IPC channel allowlist', () => {
    expect(allowedIpcChannels).toEqual([
      'switcher:invoke',
      'switcher:select-codex-home',
      'switcher:update:get',
      'switcher:update:check',
      'switcher:update:download',
      'switcher:update:install',
      'switcher:update:open-release'
    ])
  })

  it('rejects commands outside the backend allowlist', () => {
    expect(isBackendCommand('bootstrap')).toBe(true)
    expect(backendCommands).toEqual(expect.arrayContaining(['createBackup', 'writeInventory', 'ensureFileAuth']))
    expect(isBackendCommand('readAuthJson')).toBe(false)
    expect(isBackendCommand('../auth.json')).toBe(false)
    expect(new Set(backendCommands).size).toBe(backendCommands.length)
  })

  it('allows multi-account usage refresh to outlive individual HTTP timeouts', () => {
    expect(backendCommandTimeout('getLimits')).toBeGreaterThanOrEqual(120_000)
    expect(backendCommandTimeout('switchProfile')).toBeGreaterThan(30_000)
    expect(backendCommandTimeout('bootstrap')).toBe(30_000)
  })
})
