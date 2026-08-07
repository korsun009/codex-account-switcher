import type { UpdateState } from '../shared/contracts'

export const OFFICIAL_RELEASES_URL = 'https://github.com/korsun009/codex-account-switcher/releases'
const UPDATE_CHANNEL = 'latest'

export interface SafeAutoUpdater {
  autoDownload: boolean
  autoInstallOnAppQuit: boolean
  allowPrerelease: boolean
  channel: string | null
  checkForUpdates(): Promise<unknown>
  downloadUpdate(): Promise<unknown>
  quitAndInstall(isSilent?: boolean, isForceRunAfter?: boolean): void
  on(event: string, listener: (...args: any[]) => void): unknown
}

interface UpdateManagerOptions {
  isPackaged: boolean
  currentVersion: string
  updater: SafeAutoUpdater
  openExternal(url: string): Promise<unknown>
}

function updateInfoValue(info: unknown, key: 'version' | 'releaseDate'): string | null {
  if (!info || typeof info !== 'object') {
    return null
  }

  const value = (info as Record<string, unknown>)[key]
  return typeof value === 'string' && value.trim().length > 0 ? value : null
}

export class UpdateManager {
  private readonly isPackaged: boolean
  private readonly currentVersion: string
  private readonly updater: SafeAutoUpdater
  private readonly openExternal: (url: string) => Promise<unknown>
  private state: UpdateState
  private checkInFlight: Promise<UpdateState> | null = null
  private downloadInFlight: Promise<UpdateState> | null = null

  constructor(options: UpdateManagerOptions) {
    this.isPackaged = options.isPackaged
    this.currentVersion = options.currentVersion
    this.updater = options.updater
    this.openExternal = options.openExternal
    this.state = this.createState(this.isPackaged ? 'idle' : 'disabled')

    if (!this.isPackaged) {
      return
    }

    // Downloads and installation require separate explicit actions from the settings page.
    this.updater.autoDownload = false
    this.updater.autoInstallOnAppQuit = false
    this.updater.allowPrerelease = false
    this.updater.channel = UPDATE_CHANNEL
    this.bindUpdaterEvents()
  }

  get(): UpdateState {
    return { ...this.state }
  }

  async check(): Promise<UpdateState> {
    if (!this.isPackaged) {
      return this.get()
    }
    if (this.checkInFlight) {
      return await this.checkInFlight
    }

    const check = async (): Promise<UpdateState> => {
      this.state = this.createState('checking')
      try {
        await this.updater.checkForUpdates()
        if (this.state.phase === 'checking') {
          this.state = this.createState('up-to-date')
        }
      } catch {
        this.state = this.createState('error')
      }
      return this.get()
    }

    this.checkInFlight = check().finally(() => {
      this.checkInFlight = null
    })
    return await this.checkInFlight
  }

  async openRelease(): Promise<void> {
    await this.openExternal(OFFICIAL_RELEASES_URL)
  }

  async download(): Promise<UpdateState> {
    if (!this.isPackaged || this.state.phase !== 'available') {
      return this.get()
    }
    if (this.downloadInFlight) {
      return await this.downloadInFlight
    }

    const download = async (): Promise<UpdateState> => {
      this.state = { ...this.state, phase: 'downloading', downloadPercent: 0 }
      try {
        await this.updater.downloadUpdate()
        if (this.state.phase === 'downloading') {
          this.state = { ...this.state, phase: 'downloaded', downloadPercent: 100 }
        }
      } catch {
        this.state = this.createState('error')
      }
      return this.get()
    }

    this.downloadInFlight = download().finally(() => {
      this.downloadInFlight = null
    })
    return await this.downloadInFlight
  }

  async install(): Promise<void> {
    if (!this.isPackaged || this.state.phase !== 'downloaded') {
      return
    }
    this.updater.quitAndInstall(false, true)
  }

  private bindUpdaterEvents(): void {
    this.updater.on('checking-for-update', () => {
      this.state = this.createState('checking')
    })
    this.updater.on('update-available', (info: unknown) => {
      this.state = {
        ...this.createState('available'),
        availableVersion: updateInfoValue(info, 'version'),
        releaseDate: updateInfoValue(info, 'releaseDate')
      }
    })
    this.updater.on('update-not-available', () => {
      this.state = this.createState('up-to-date')
    })
    this.updater.on('download-progress', (progress: unknown) => {
      const raw = progress && typeof progress === 'object'
        ? (progress as Record<string, unknown>).percent
        : null
      const percent = typeof raw === 'number' && Number.isFinite(raw)
        ? Math.max(0, Math.min(100, raw))
        : null
      this.state = { ...this.state, phase: 'downloading', downloadPercent: percent }
    })
    this.updater.on('update-downloaded', (info: unknown) => {
      this.state = {
        ...this.state,
        phase: 'downloaded',
        availableVersion: updateInfoValue(info, 'version') ?? this.state.availableVersion,
        downloadPercent: 100
      }
    })
    this.updater.on('error', () => {
      this.state = this.createState('error')
    })
  }

  private createState(phase: UpdateState['phase']): UpdateState {
    return {
      supported: this.isPackaged,
      phase,
      currentVersion: this.currentVersion,
      availableVersion: null,
      releaseDate: null,
      downloadPercent: null
    }
  }
}
