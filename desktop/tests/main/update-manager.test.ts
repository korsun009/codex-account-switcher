import { EventEmitter } from 'node:events'
import { describe, expect, it, vi } from 'vitest'
import {
  OFFICIAL_RELEASES_URL,
  UpdateManager,
  type SafeAutoUpdater
} from '../../src/main/update-manager'

class FakeAutoUpdater extends EventEmitter implements SafeAutoUpdater {
  autoDownload = true
  autoInstallOnAppQuit = true
  allowPrerelease = false
  channel: string | null = null
  checkForUpdates = vi.fn(async () => null)
  downloadUpdate = vi.fn(async () => [])
  quitAndInstall = vi.fn()
}

describe('UpdateManager', () => {
  it('disables checks outside a packaged application', async () => {
    const updater = new FakeAutoUpdater()
    const openExternal = vi.fn(async () => undefined)
    const manager = new UpdateManager({
      isPackaged: false,
      currentVersion: '2.0.0',
      updater,
      openExternal
    })

    expect(manager.get()).toEqual({
      supported: false,
      phase: 'disabled',
      currentVersion: '2.0.0',
      availableVersion: null,
      releaseDate: null,
      downloadPercent: null
    })
    await expect(manager.check()).resolves.toEqual(manager.get())
    expect(updater.checkForUpdates).not.toHaveBeenCalled()
  })

  it('checks the stable channel without downloading until the user confirms', async () => {
    const updater = new FakeAutoUpdater()
    updater.checkForUpdates.mockImplementation(async () => {
      updater.emit('checking-for-update')
      updater.emit('update-available', {
        version: '2.0.1',
        releaseDate: '2026-08-08T10:00:00.000Z'
      })
      return null
    })
    const manager = new UpdateManager({
      isPackaged: true,
      currentVersion: '2.0.0',
      updater,
      openExternal: vi.fn(async () => undefined)
    })

    expect(updater.autoDownload).toBe(false)
    expect(updater.autoInstallOnAppQuit).toBe(false)
    expect(updater.allowPrerelease).toBe(false)
    expect(updater.channel).toBe('latest')

    await expect(manager.check()).resolves.toMatchObject({
      supported: true,
      phase: 'available',
      currentVersion: '2.0.0',
      availableVersion: '2.0.1'
    })
    expect(updater.checkForUpdates).toHaveBeenCalledOnce()
    expect(updater.downloadUpdate).not.toHaveBeenCalled()
    expect(updater.quitAndInstall).not.toHaveBeenCalled()
  })

  it('downloads and installs an available update only after explicit user actions', async () => {
    const updater = new FakeAutoUpdater()
    const manager = new UpdateManager({
      isPackaged: true,
      currentVersion: '2.0.0',
      updater,
      openExternal: vi.fn(async () => undefined)
    })
    updater.emit('update-available', { version: '2.0.1', releaseDate: '2026-08-08T10:00:00.000Z' })
    updater.downloadUpdate.mockImplementation(async () => {
      updater.emit('update-downloaded', { version: '2.0.1' })
      return []
    })

    await expect(manager.download()).resolves.toMatchObject({ phase: 'downloaded', availableVersion: '2.0.1' })
    expect(updater.downloadUpdate).toHaveBeenCalledOnce()

    await manager.install()
    expect(updater.quitAndInstall).toHaveBeenCalledWith(false, true)
  })

  it('never exposes raw updater errors to the renderer', async () => {
    const updater = new FakeAutoUpdater()
    updater.checkForUpdates.mockImplementation(async () => {
      updater.emit('error', new Error('request failed with secret query data'))
      throw new Error('request failed with secret query data')
    })
    const manager = new UpdateManager({
      isPackaged: true,
      currentVersion: '2.0.0',
      updater,
      openExternal: vi.fn(async () => undefined)
    })

    const state = await manager.check()
    expect(state.phase).toBe('error')
    expect(JSON.stringify(state)).not.toContain('secret query data')
  })

  it('opens only the fixed official HTTPS release page', async () => {
    const updater = new FakeAutoUpdater()
    const openExternal = vi.fn(async () => undefined)
    const manager = new UpdateManager({
      isPackaged: true,
      currentVersion: '2.0.0',
      updater,
      openExternal
    })

    await manager.openRelease()

    expect(OFFICIAL_RELEASES_URL).toBe('https://github.com/korsun009/codex-account-switcher/releases')
    expect(openExternal).toHaveBeenCalledOnce()
    expect(openExternal).toHaveBeenCalledWith(OFFICIAL_RELEASES_URL)
    expect(updater.downloadUpdate).not.toHaveBeenCalled()
    expect(updater.quitAndInstall).not.toHaveBeenCalled()
  })
})
