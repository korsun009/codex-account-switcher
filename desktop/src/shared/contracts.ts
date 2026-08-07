export const backendCommands = [
  'bootstrap',
  'listProfiles',
  'addProfile',
  'deleteProfile',
  'captureProfile',
  'prepareLogin',
  'switchProfile',
  'getLimits',
  'refreshLimits',
  'createBackup',
  'getBackups',
  'restoreBackup',
  'writeInventory',
  'ensureFileAuth',
  'getDiagnostics',
  'getSettings',
  'setCodexHome',
  'setLanguage',
  'setTheme',
  'openCodex',
  'openConfig',
  'listRemoteConnections',
  'createRemoteConnection',
  'testRemoteConnection',
  'deleteRemoteConnection'
] as const

export type BackendCommand = (typeof backendCommands)[number]

export interface ProfileSummary {
  name: string
  displayName: string
  active: boolean
  hasCredentials: boolean
  credentialStatus: 'ready' | 'missing' | 'invalid' | 'expired' | 'unknown'
}

export interface LimitWindow {
  percentLeft: number
  resetAt: string | null
}

export interface ProfileLimits {
  name: string
  displayName: string
  success: boolean
  fiveHour: LimitWindow | null
  weekly: LimitWindow | null
  fetchedAt: string | null
  error: string | null
}

export interface BackupSummary {
  id: string
  createdAt: string
  verified: boolean
}

export interface DiagnosticsSummary {
  backendVersion: string
  codexHome: string | null
  codexShells: number
  codexAppServers: number
  remoteApiConfigured: boolean
  warnings: string[]
}

export interface BootstrapData {
  profiles: ProfileSummary[]
  limits: ProfileLimits[]
  diagnostics: DiagnosticsSummary
  settings: {
    language: 'ru' | 'en' | 'zh'
    theme: 'system' | 'dark' | 'light'
  }
}

export interface BridgeRequest {
  id: string
  command: BackendCommand
  payload?: unknown
}

export interface BridgeResponse<T = unknown> {
  id: string
  ok: boolean
  data: T | null
  error: string | null
}

export interface UpdateState {
  supported: boolean
  phase: 'disabled' | 'idle' | 'checking' | 'available' | 'downloading' | 'downloaded' | 'up-to-date' | 'error'
  currentVersion: string
  availableVersion: string | null
  releaseDate: string | null
  downloadPercent: number | null
}

export interface RemoteConnectionSummary {
  id: string
  displayName: string
  type: 'generic' | 'telegram' | 'webhook'
  endpoint: string
  hasToken: boolean
  createdUtc: string
}

export interface RemoteConnectionTestResult {
  success: boolean
  statusCode: number | null
  message: string
}

export interface UpdateApi {
  get(): Promise<UpdateState>
  check(): Promise<UpdateState>
  download(): Promise<UpdateState>
  install(): Promise<void>
  openRelease(): Promise<void>
}

export interface DesktopApi {
  invoke<T>(command: BackendCommand, payload?: unknown): Promise<BridgeResponse<T>>
  selectCodexHome(title?: string): Promise<string | null>
  updates: UpdateApi
}

export function isBackendCommand(value: unknown): value is BackendCommand {
  return typeof value === 'string' && (backendCommands as readonly string[]).includes(value)
}
