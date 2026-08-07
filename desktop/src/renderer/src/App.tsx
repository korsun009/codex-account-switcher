import { useEffect, useMemo, useState } from 'react'
import {
  ArchiveRestore,
  CircleGauge,
  CloudCog,
  DatabaseBackup,
  FileCheck2,
  FileScan,
  FolderCog,
  KeyRound,
  Languages,
  LayoutGrid,
  MonitorCog,
  Moon,
  Play,
  Plus,
  RefreshCw,
  ScrollText,
  Settings,
  ShieldCheck,
  Sun,
  Trash2,
  UserRoundCheck
} from 'lucide-react'
import type { BackendCommand, BackupSummary, BootstrapData, BridgeResponse, ProfileSummary, RemoteConnectionSummary, RemoteConnectionTestResult, UpdateState } from '../../shared/contracts'
import logoUrl from './assets/logo.png'
import { createTranslator, formatDateTime, type Language, type TranslationKey, type Translator } from './locales/i18n'

type Page = 'accounts' | 'limits' | 'backups' | 'remote' | 'settings'

const emptyData: BootstrapData = {
  profiles: [],
  limits: [],
  diagnostics: {
    backendVersion: '2.0.0',
    codexHome: null,
    codexShells: 0,
    codexAppServers: 0,
    remoteApiConfigured: false,
    warnings: []
  },
  settings: { language: 'ru', theme: 'system' }
}

const navigation = [
  { id: 'accounts' as const, label: 'nav.accounts' as TranslationKey, icon: LayoutGrid },
  { id: 'limits' as const, label: 'nav.limits' as TranslationKey, icon: CircleGauge },
  { id: 'backups' as const, label: 'nav.backups' as TranslationKey, icon: ArchiveRestore },
  { id: 'remote' as const, label: 'nav.remote' as TranslationKey, icon: CloudCog },
  { id: 'settings' as const, label: 'nav.settings' as TranslationKey, icon: Settings }
]

type RunCommand = <T = unknown>(command: BackendCommand, payload?: unknown, successMessage?: string) => Promise<T | null>

function AccountCard({ profile, onRefresh, runCommand, t }: { profile: ProfileSummary; onRefresh: () => Promise<void>; runCommand: RunCommand; t: Translator }) {
  const [busy, setBusy] = useState(false)
  const credentialLabel = {
    ready: t('credentials.ready'),
    missing: t('credentials.missing'),
    invalid: t('credentials.invalid'),
    expired: t('credentials.expired'),
    unknown: t('credentials.unknown')
  }[profile.credentialStatus]

  async function run(command: 'switchProfile' | 'captureProfile' | 'deleteProfile'): Promise<void> {
    if (command === 'deleteProfile' && !window.confirm(t('accounts.deleteConfirm', { name: profile.displayName }))) {
      return
    }
    setBusy(true)
    try {
      const result = await runCommand(command, { name: profile.name })
      if (result) await onRefresh()
    } finally {
      setBusy(false)
    }
  }

  return (
    <article className={`account-card ${profile.active ? 'is-active' : ''}`}>
      <div className="account-card__top">
        <div className="account-avatar"><KeyRound size={19} /></div>
        <div>
          <p className="eyebrow">{profile.active ? t('accounts.activeProfile') : t('accounts.codexProfile')}</p>
          <h3>{profile.displayName}</h3>
        </div>
        <span className={`status-pill status-pill--${profile.credentialStatus}`}>
          {credentialLabel}
        </span>
      </div>
      <div className="account-card__actions">
        <button className="button button--primary" disabled={busy || profile.active || profile.credentialStatus !== 'ready'} onClick={() => void run('switchProfile')}>
          <UserRoundCheck size={16} /> {t('accounts.switch')}
        </button>
        <button className="button" disabled={busy} onClick={() => void run('captureProfile')}>{t('accounts.capture')}</button>
        <button className="icon-button icon-button--danger" aria-label={t('accounts.delete')} title={t('accounts.delete')} disabled={busy} onClick={() => void run('deleteProfile')}>
          <Trash2 size={17} />
        </button>
      </div>
    </article>
  )
}

function LimitsPage({ data, t, language }: { data: BootstrapData; t: Translator; language: Language }) {
  return (
    <section className="page-stack">
      <header className="page-header">
        <div><p className="eyebrow">{t('limits.eyebrow')}</p><h1>{t('limits.title')}</h1></div>
      </header>
      <div className="limit-list">
        {data.limits.length === 0 && <div className="empty-inline">{t('limits.empty')}</div>}
        {data.limits.map((item) => (
          <article className="limit-row" key={item.name}>
            <div className="limit-row__name"><strong>{item.displayName}</strong><span>{item.success ? t('common.updated') : t('common.error')}</span></div>
            {item.fiveHour && <Limit title={t('limits.fiveHour')} percent={item.fiveHour.percentLeft} resetAt={item.fiveHour.resetAt} t={t} language={language} />}
            {item.weekly && <Limit title={t('limits.weekly')} percent={item.weekly.percentLeft} resetAt={item.weekly.resetAt} t={t} language={language} />}
            {!item.fiveHour && !item.weekly && <span className="muted">{t('limits.noWindows')}</span>}
          </article>
        ))}
      </div>
    </section>
  )
}

function Limit({ title, percent, resetAt, t, language }: { title: string; percent: number; resetAt: string | null; t: Translator; language: Language }) {
  const boundedPercent = Math.max(0, Math.min(100, percent))
  return (
    <div className="limit-meter">
      <div className="limit-meter__label"><span>{title}</span><strong>{percent}%</strong></div>
      <div className="limit-meter__track" role="progressbar" aria-label={t('limits.progressLabel', { title, percent })} aria-valuemin={0} aria-valuemax={100} aria-valuenow={boundedPercent}><span style={{ width: `${boundedPercent}%` }} /></div>
      <small>{resetAt ? t('limits.resetAt', { date: formatDateTime(resetAt, language) }) : t('limits.resetUnknown')}</small>
    </div>
  )
}

export default function App() {
  const [page, setPage] = useState<Page>('accounts')
  const [data, setData] = useState<BootstrapData>(emptyData)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [newProfile, setNewProfile] = useState('')
  const [limitsLoading, setLimitsLoading] = useState(false)
  const [backups, setBackups] = useState<BackupSummary[]>([])
  const [backupsLoading, setBackupsLoading] = useState(false)
  const [busyCommand, setBusyCommand] = useState<BackendCommand | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [updateState, setUpdateState] = useState<UpdateState | null>(null)
  const [connections, setConnections] = useState<RemoteConnectionSummary[]>([])
  const [connectionsLoading, setConnectionsLoading] = useState(false)
  const language = data.settings.language as Language
  const t = useMemo(() => createTranslator(language), [language])

  async function refresh(): Promise<void> {
    setError(null)
    try {
      if (!window.codexSwitcher) throw new Error(t('common.bridgeUnavailable'))
      const response = await window.codexSwitcher.invoke<BootstrapData>('bootstrap')
      if (response.ok && response.data) {
        setData(response.data)
      } else {
        setError(response.error ?? t('common.backendUnavailable'))
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('common.backendUnavailable'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void refresh() }, [])

  useEffect(() => {
    let cancelled = false
    void window.codexSwitcher.updates.get().then((state) => {
      if (!cancelled) setUpdateState(state)
    }).catch(() => {
      if (!cancelled) setUpdateState(null)
    })
    return () => { cancelled = true }
  }, [])

  useEffect(() => {
    const root = document.documentElement
    root.lang = data.settings.language === 'zh' ? 'zh-CN' : data.settings.language
    const media = window.matchMedia?.('(prefers-color-scheme: light)')
    const applyTheme = () => {
      const theme = data.settings.theme === 'system' ? (media?.matches ? 'light' : 'dark') : data.settings.theme
      root.dataset.theme = theme
    }
    applyTheme()
    media?.addEventListener?.('change', applyTheme)
    return () => media?.removeEventListener?.('change', applyTheme)
  }, [data.settings.language, data.settings.theme])

  useEffect(() => {
    if (page !== 'limits') return
    let cancelled = false
    setLimitsLoading(true)
    void window.codexSwitcher.invoke<BootstrapData['limits']>('getLimits').then((response) => {
      if (!cancelled && response.ok && response.data) {
        setData((current) => ({ ...current, limits: response.data ?? [] }))
      } else if (!cancelled && !response.ok) {
        setError(response.error ?? t('limits.loadError'))
      }
    }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason.message : t('limits.loadError'))
    }).finally(() => {
      if (!cancelled) setLimitsLoading(false)
    })
    return () => { cancelled = true }
  }, [page, t])

  useEffect(() => {
    if (page !== 'backups') return
    let cancelled = false
    setBackupsLoading(true)
    void window.codexSwitcher.invoke<BackupSummary[]>('getBackups').then((response) => {
      if (cancelled) return
      if (response.ok && response.data) setBackups(response.data)
      else setError(response.error ?? t('backups.loadError'))
    }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason.message : t('backups.loadError'))
    }).finally(() => {
      if (!cancelled) setBackupsLoading(false)
    })
    return () => { cancelled = true }
  }, [page, t])

  useEffect(() => {
    if (page !== 'remote') return
    let cancelled = false
    setConnectionsLoading(true)
    void window.codexSwitcher.invoke<RemoteConnectionSummary[]>('listRemoteConnections').then((response) => {
      if (cancelled) return
      if (response.ok && response.data) setConnections(response.data)
      else setError(response.error ?? t('remote.connectionsLoadError'))
    }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason.message : t('remote.connectionsLoadError'))
    }).finally(() => {
      if (!cancelled) setConnectionsLoading(false)
    })
    return () => { cancelled = true }
  }, [page, t])

  const activeProfile = useMemo(() => data.profiles.find((profile) => profile.active), [data.profiles])

  const runCommand: RunCommand = async <T,>(command: BackendCommand, payload?: unknown, successMessage?: string): Promise<T | null> => {
    setBusyCommand(command)
    setError(null)
    setNotice(null)
    try {
      const response: BridgeResponse<T> = await window.codexSwitcher.invoke<T>(command, payload)
      if (!response.ok) {
        setError(response.error ?? t('common.operationFailed'))
        return null
      }
      const message = successMessage ?? (typeof response.data === 'object' && response.data !== null && 'message' in response.data
        ? String((response.data as { message?: unknown }).message ?? '')
        : '')
      if (message) setNotice(message)
      return response.data
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('common.operationFailed'))
      return null
    } finally {
      setBusyCommand(null)
    }
  }

  async function addProfile(): Promise<void> {
    const displayName = newProfile.trim()
    if (!displayName) return
    const result = await runCommand('addProfile', { displayName }, t('accounts.added', { name: displayName }))
    if (!result) return
    setNewProfile('')
    await refresh()
  }

  async function chooseCodexHome(): Promise<void> {
    const path = await window.codexSwitcher.selectCodexHome(t('firstRun.chooseFolder'))
    if (!path) return
    const result = await runCommand('setCodexHome', { path }, t('settings.codexHomeUpdated'))
    if (!result) return
    await refresh()
  }

  async function loadBackups(): Promise<void> {
    setBackupsLoading(true)
    try {
      try {
        const response = await window.codexSwitcher.invoke<BackupSummary[]>('getBackups')
        if (response.ok && response.data) setBackups(response.data)
        else setError(response.error ?? t('backups.loadError'))
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : t('backups.loadError'))
      }
    } finally {
      setBackupsLoading(false)
    }
  }

  async function checkForUpdates(): Promise<void> {
    setUpdateState((current) => current ? { ...current, phase: 'checking' } : current)
    try {
      setUpdateState(await window.codexSwitcher.updates.check())
    } catch {
      setUpdateState((current) => current ? { ...current, phase: 'error' } : current)
    }
  }

  async function downloadUpdate(): Promise<void> {
    setUpdateState((current) => current ? { ...current, phase: 'downloading', downloadPercent: 0 } : current)
    try {
      setUpdateState(await window.codexSwitcher.updates.download())
    } catch {
      setUpdateState((current) => current ? { ...current, phase: 'error', downloadPercent: null } : current)
    }
  }

  async function installUpdate(): Promise<void> {
    try {
      await window.codexSwitcher.updates.install()
    } catch {
      setUpdateState((current) => current ? { ...current, phase: 'error', downloadPercent: null } : current)
    }
  }

  function updateStatus(): string {
    if (!updateState || updateState.phase === 'disabled') return t('settings.updateDisabled')
    if (updateState.phase === 'checking') return t('settings.updateChecking')
    if (updateState.phase === 'available') return t('settings.updateAvailable', { version: updateState.availableVersion ?? '' })
    if (updateState.phase === 'downloading') return t('settings.updateDownloading', { percent: Math.round(updateState.downloadPercent ?? 0) })
    if (updateState.phase === 'downloaded') return t('settings.updateDownloaded', { version: updateState.availableVersion ?? '' })
    if (updateState.phase === 'up-to-date') return t('settings.updateCurrent', { version: updateState.currentVersion })
    if (updateState.phase === 'error') return t('settings.updateError')
    return t('settings.updateIdle', { version: updateState.currentVersion })
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand"><div className="brand__mark"><img src={logoUrl} alt={t('brand.logoAlt')} /></div><div><strong>Codex</strong><span>Account Switcher</span></div></div>
        <nav aria-label={t('nav.ariaLabel')}>
          {navigation.map(({ id, label, icon: Icon }) => (
            <button key={id} aria-current={page === id ? 'page' : undefined} className={page === id ? 'nav-item is-active' : 'nav-item'} onClick={() => setPage(id)}>
              <Icon size={18} /><span>{t(label)}</span>
            </button>
          ))}
        </nav>
        <div className="sidebar__status">
          <span className={data.diagnostics.codexShells > 0 ? 'status-dot is-online' : 'status-dot'} />
          <div><strong>{data.diagnostics.codexShells > 0 ? t('sidebar.codexRunning') : t('sidebar.codexStopped')}</strong><span>{activeProfile?.displayName ?? t('sidebar.noProfile')}</span></div>
        </div>
      </aside>

      <main className="workspace" aria-busy={loading || busyCommand !== null}>
        {error && <div className="error-banner" role="alert"><ShieldCheck size={18} /><span>{error}</span><button onClick={() => void refresh()}><RefreshCw size={16} /> {t('common.retry')}</button></div>}
        {notice && <div className="notice-banner" role="status"><FileCheck2 size={18} /><span>{notice}</span><button aria-label={t('common.closeNotice')} onClick={() => setNotice(null)}>×</button></div>}
        {loading ? <div className="loading-state" role="status"><RefreshCw className="spin" /> {t('common.loadingBackend')}</div> : null}

        {!loading && !data.diagnostics.codexHome && (
          <section className="first-run">
            <div className="empty-state__icon"><FolderCog size={24} /></div>
            <p className="eyebrow">{t('firstRun.eyebrow')}</p>
            <h1>{t('firstRun.title')}</h1>
            <p>{t('firstRun.description')}</p>
            {data.diagnostics.warnings.map((warning) => <span className="first-run__warning" key={warning}>{warning}</span>)}
            <button className="button button--primary" onClick={() => void chooseCodexHome()}><FolderCog size={16} /> {t('firstRun.chooseFolder')}</button>
          </section>
        )}

        {!loading && data.diagnostics.codexHome && page === 'accounts' && (
          <section className="page-stack">
            <header className="page-header">
              <div><p className="eyebrow">{t('accounts.eyebrow')}</p><h1>{t('accounts.title')}</h1><p>{t('accounts.description')}</p></div>
              <div className="header-actions">
                <button className="button" disabled={busyCommand !== null} onClick={() => void runCommand('prepareLogin', undefined, t('accounts.loginPrepared'))}><KeyRound size={16} /> {t('accounts.newLogin')}</button>
                <button className="button" onClick={() => void refresh()}><RefreshCw size={16} /> {t('common.refresh')}</button>
              </div>
            </header>
            <div className="quick-add">
              <div><Plus size={18} /><div><strong>{t('accounts.addTitle')}</strong><span>{t('accounts.addDescription')}</span></div></div>
              <div className="quick-add__form"><input aria-label={t('accounts.nameLabel')} value={newProfile} onChange={(event) => setNewProfile(event.target.value)} placeholder={t('accounts.namePlaceholder')} onKeyDown={(event) => { if (event.key === 'Enter') void addProfile() }} /><button className="button button--primary" onClick={() => void addProfile()}>{t('accounts.add')}</button></div>
            </div>
            {data.profiles.length === 0 ? (
              <div className="empty-state"><div className="empty-state__icon"><KeyRound size={24} /></div><h2>{t('accounts.emptyTitle')}</h2><p>{t('accounts.emptyDescription')}</p></div>
            ) : (
              <div className="account-grid">{data.profiles.map((profile) => <AccountCard key={profile.name} profile={profile} onRefresh={refresh} runCommand={runCommand} t={t} />)}</div>
            )}
          </section>
        )}

        {!loading && data.diagnostics.codexHome && page === 'limits' && (
          <>
            {limitsLoading && <div className="loading-strip"><RefreshCw className="spin" size={15} /> {t('limits.loading')}</div>}
            <LimitsPage data={data} t={t} language={language} />
          </>
        )}
        {!loading && data.diagnostics.codexHome && page === 'backups' && (
          <BackupsPage
            backups={backups}
            loading={backupsLoading}
            busy={busyCommand !== null}
            t={t}
            language={language}
            onRefresh={loadBackups}
            onCreate={async () => {
              const result = await runCommand('createBackup', undefined, t('backups.created'))
              if (result) await loadBackups()
            }}
            onRestore={async (backup) => {
              if (!window.confirm(t('backups.restoreConfirm', { id: backup.id }))) return
              const result = await runCommand('restoreBackup', { id: backup.id }, t('backups.restored'))
              if (result) await refresh()
            }}
          />
        )}
        {!loading && data.diagnostics.codexHome && page === 'remote' && (
          <RemotePage
            data={data}
            busy={busyCommand !== null}
            t={t}
            connections={connections}
            connectionsLoading={connectionsLoading}
            onRefresh={refresh}
            onStart={() => void runCommand('openCodex', undefined, t('remote.started'))}
            onOpenConfig={() => void runCommand('openConfig', undefined, t('remote.configOpened'))}
            onCreateConnection={async (input) => {
              const result = await runCommand<RemoteConnectionSummary>('createRemoteConnection', input, t('remote.connectionAdded', { name: input.displayName }))
              if (result) setConnections((current) => [...current, result])
              return result !== null
            }}
            onTestConnection={async (id) => {
              const result = await runCommand<RemoteConnectionTestResult>('testRemoteConnection', { id })
              if (result) setNotice(result.success ? t('remote.connectionTestOk') : t('remote.connectionTestFailed'))
            }}
            onDeleteConnection={async (connection) => {
              if (!window.confirm(t('remote.deleteConnectionConfirm', { name: connection.displayName }))) return
              const result = await runCommand('deleteRemoteConnection', { id: connection.id }, t('remote.connectionDeleted'))
              if (result) setConnections((current) => current.filter((item) => item.id !== connection.id))
            }}
          />
        )}
        {!loading && data.diagnostics.codexHome && page === 'settings' && (
          <section className="page-stack">
            <header className="page-header"><div><p className="eyebrow">{t('settings.eyebrow')}</p><h1>{t('settings.title')}</h1></div></header>
            <div className="settings-list">
              <div className="setting-row"><div className="setting-icon"><FolderCog size={19} /></div><div><strong>Codex Home</strong><span>{data.diagnostics.codexHome ?? t('settings.codexHomeMissing')}</span></div><button className="button" onClick={() => void chooseCodexHome()}>{t('common.change')}</button></div>
              <div className="setting-row"><div className="setting-icon"><ShieldCheck size={19} /></div><div><strong>{t('settings.profileProtection')}</strong><span>{t('settings.profileProtectionDescription')}</span></div><span className="status-pill status-pill--ready">{t('common.enabled')}</span></div>
              <div className="setting-row"><div className="setting-icon"><Languages size={19} /></div><div><strong>{t('settings.language')}</strong><span>{t('settings.languageDescription')}</span></div><select aria-label={t('settings.languageAria')} value={data.settings.language} onChange={(event) => {
                const language = event.target.value as BootstrapData['settings']['language']
                void runCommand('setLanguage', { language }).then((result) => {
                  if (result) setData((current) => ({ ...current, settings: { ...current.settings, language } }))
                })
              }}><option value="ru">{t('languages.ru')}</option><option value="en">{t('languages.en')}</option><option value="zh">{t('languages.zh')}</option></select></div>
              <div className="setting-row"><div className="setting-icon">{data.settings.theme === 'light' ? <Sun size={19} /> : <Moon size={19} />}</div><div><strong>{t('settings.theme')}</strong><span>{t('settings.themeDescription')}</span></div><select aria-label={t('settings.themeAria')} value={data.settings.theme} onChange={(event) => {
                const theme = event.target.value as BootstrapData['settings']['theme']
                void runCommand('setTheme', { theme }).then((result) => {
                  if (result) setData((current) => ({ ...current, settings: { ...current.settings, theme } }))
                })
              }}><option value="system">{t('themes.system')}</option><option value="dark">{t('themes.dark')}</option><option value="light">{t('themes.light')}</option></select></div>
              <div className="setting-row"><div className="setting-icon"><FileCheck2 size={19} /></div><div><strong>{t('settings.fileAuth')}</strong><span>{t('settings.fileAuthDescription')}</span></div><button className="button" disabled={busyCommand !== null} onClick={() => void runCommand('ensureFileAuth')}>{t('settings.checkFileAuth')}</button></div>
              <div className="setting-row"><div className="setting-icon"><FileScan size={19} /></div><div><strong>{t('settings.inventory')}</strong><span>{t('settings.inventoryDescription')}</span></div><button className="button" disabled={busyCommand !== null} onClick={() => void runCommand('writeInventory', undefined, t('settings.reportCreated'))}>{t('settings.createReport')}</button></div>
              <div className="setting-row"><div className="setting-icon"><RefreshCw className={updateState?.phase === 'checking' || updateState?.phase === 'downloading' ? 'spin' : ''} size={19} /></div><div><strong>{t('settings.updates')}</strong><span>{t('settings.updatesDescription')} {updateStatus()}</span></div><div className="update-actions"><button className="button" disabled={!updateState?.supported || updateState.phase === 'checking' || updateState.phase === 'downloading'} onClick={() => void checkForUpdates()}>{t('settings.checkUpdates')}</button>{updateState?.phase === 'available' && <button className="button button--primary" onClick={() => void downloadUpdate()}>{t('settings.downloadUpdate')}</button>}{updateState?.phase === 'downloaded' && <button className="button button--primary" onClick={() => void installUpdate()}>{t('settings.installUpdate')}</button>}{(updateState?.phase === 'available' || updateState?.phase === 'error') && <button className="button" onClick={() => void window.codexSwitcher.updates.openRelease()}>{t('settings.openRelease')}</button>}</div></div>
            </div>
          </section>
        )}
      </main>
    </div>
  )
}

function BackupsPage({ backups, loading, busy, t, language, onRefresh, onCreate, onRestore }: {
  backups: BackupSummary[]
  loading: boolean
  busy: boolean
  t: Translator
  language: Language
  onRefresh: () => Promise<void>
  onCreate: () => Promise<void>
  onRestore: (backup: BackupSummary) => Promise<void>
}) {
  return (
    <section className="page-stack">
      <header className="page-header">
        <div><p className="eyebrow">{t('backups.eyebrow')}</p><h1>{t('backups.title')}</h1><p>{t('backups.description')}</p></div>
        <div className="header-actions">
          <button className="button" disabled={loading} onClick={() => void onRefresh()}><RefreshCw className={loading ? 'spin' : ''} size={16} /> {t('common.refresh')}</button>
          <button className="button button--primary" disabled={busy} onClick={() => void onCreate()}><DatabaseBackup size={16} /> {t('backups.create')}</button>
        </div>
      </header>
      {backups.length === 0 && !loading ? <div className="empty-inline">{t('backups.empty')}</div> : (
        <div className="backup-list">
          {backups.map((backup) => (
            <article className="backup-row" key={backup.id}>
              <div className="setting-icon"><ArchiveRestore size={19} /></div>
              <div><strong>{backup.id}</strong><span>{formatDateTime(backup.createdAt, language)}</span></div>
              <span className={`status-pill ${backup.verified ? 'status-pill--ready' : 'status-pill--missing'}`}>{backup.verified ? t('backups.manifestFound') : t('backups.verificationRequired')}</span>
              <button className="button" disabled={busy || !backup.verified} onClick={() => void onRestore(backup)}>{t('backups.restore')}</button>
            </article>
          ))}
        </div>
      )}
    </section>
  )
}

type RemoteConnectionInput = { displayName: string; type: RemoteConnectionSummary['type']; endpoint: string; token: string }

function RemotePage({ data, busy, connections, connectionsLoading, onRefresh, onStart, onOpenConfig, onCreateConnection, onTestConnection, onDeleteConnection, t }: {
  data: BootstrapData
  busy: boolean
  t: Translator
  connections: RemoteConnectionSummary[]
  connectionsLoading: boolean
  onRefresh: () => Promise<void>
  onStart: () => void
  onOpenConfig: () => void
  onCreateConnection: (input: RemoteConnectionInput) => Promise<boolean>
  onTestConnection: (id: string) => Promise<void>
  onDeleteConnection: (connection: RemoteConnectionSummary) => Promise<void>
}) {
  const diagnostics = data.diagnostics
  const [displayName, setDisplayName] = useState('')
  const [type, setType] = useState<RemoteConnectionSummary['type']>('generic')
  const [endpoint, setEndpoint] = useState('')
  const [token, setToken] = useState('')

  async function submitConnection(): Promise<void> {
    if (!displayName.trim() || !endpoint.trim() || !token.trim()) return
    if (await onCreateConnection({ displayName: displayName.trim(), type, endpoint: endpoint.trim(), token: token.trim() })) {
      setDisplayName('')
      setEndpoint('')
      setToken('')
    }
  }

  return (
    <section className="page-stack">
      <header className="page-header">
        <div><p className="eyebrow">{t('remote.eyebrow')}</p><h1>{t('remote.title')}</h1><p>{t('remote.description')}</p></div>
        <button className="button" onClick={() => void onRefresh()}><RefreshCw size={16} /> {t('common.refresh')}</button>
      </header>
      <div className="diagnostic-grid">
        <DiagnosticCard icon={MonitorCog} label="Codex" value={diagnostics.codexAppServers > 0 ? t('common.running') : t('common.notRunning')} good={diagnostics.codexAppServers > 0} />
        <DiagnosticCard icon={CloudCog} label={t('remote.windowsApi')} value={diagnostics.remoteApiConfigured ? t('common.configured') : t('remote.tokenMissing')} good={diagnostics.remoteApiConfigured} />
        <DiagnosticCard icon={ShieldCheck} label={t('remote.backend')} value={t('common.version', { version: diagnostics.backendVersion })} good />
      </div>
      <div className="settings-list">
        <div className="setting-row"><div className="setting-icon"><Play size={19} /></div><div><strong>{t('remote.codexDesktop')}</strong><span>{t('remote.codexDesktopDescription')}</span></div><button className="button button--primary" disabled={busy || diagnostics.codexAppServers > 0} onClick={onStart}>{t('remote.start')}</button></div>
        <div className="setting-row"><div className="setting-icon"><ScrollText size={19} /></div><div><strong>config.toml</strong><span>{t('remote.configDescription')}</span></div><button className="button" disabled={busy || !diagnostics.codexHome} onClick={onOpenConfig}>{t('common.open')}</button></div>
      </div>
      <section className="integration-panel">
        <div><p className="eyebrow">API</p><h2>{t('remote.connectionsTitle')}</h2><p>{t('remote.connectionsDescription')}</p></div>
        <div className="connection-form">
          <input aria-label={t('remote.connectionName')} placeholder={t('remote.connectionName')} value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
          <select aria-label={t('remote.connectionType')} value={type} onChange={(event) => setType(event.target.value as RemoteConnectionSummary['type'])}>
            <option value="generic">{t('remote.typeGeneric')}</option><option value="telegram">{t('remote.typeTelegram')}</option><option value="webhook">{t('remote.typeWebhook')}</option>
          </select>
          <input aria-label={t('remote.connectionEndpoint')} placeholder="https://service.example/health" value={endpoint} onChange={(event) => setEndpoint(event.target.value)} />
          <input aria-label={t('remote.connectionToken')} placeholder={t('remote.connectionToken')} type="password" autoComplete="new-password" value={token} onChange={(event) => setToken(event.target.value)} />
          <button className="button button--primary" disabled={busy || !displayName.trim() || !endpoint.trim() || token.trim().length < 8} onClick={() => void submitConnection()}><Plus size={16} /> {t('remote.addConnection')}</button>
        </div>
        {connectionsLoading ? <div className="loading-strip"><RefreshCw className="spin" size={15} /> {t('common.loadingBackend')}</div> : connections.length === 0 ? <div className="empty-inline">{t('remote.noConnections')}</div> : (
          <div className="connection-list">
            {connections.map((connection) => (
              <article className="connection-row" key={connection.id}>
                <div className="setting-icon"><CloudCog size={19} /></div>
                <div><strong>{connection.displayName}</strong><span>{connection.endpoint}</span></div>
                <span className="status-pill status-pill--ready">{t('remote.tokenProtected')}</span>
                <button className="button" disabled={busy} onClick={() => void onTestConnection(connection.id)}>{t('remote.testConnection')}</button>
                <button className="icon-button icon-button--danger" aria-label={t('remote.deleteConnection')} title={t('remote.deleteConnection')} disabled={busy} onClick={() => void onDeleteConnection(connection)}><Trash2 size={17} /></button>
              </article>
            ))}
          </div>
        )}
      </section>
      {diagnostics.warnings.length > 0 && <div className="warning-list">{diagnostics.warnings.map((warning) => <p key={warning}>{warning}</p>)}</div>}
    </section>
  )
}

function DiagnosticCard({ icon: Icon, label, value, good }: { icon: typeof CloudCog; label: string; value: string; good: boolean }) {
  return (
    <article className="diagnostic-card">
      <div className="feature-panel__icon"><Icon size={21} /></div>
      <div><span>{label}</span><strong>{value}</strong></div>
      <span className={good ? 'status-dot is-online' : 'status-dot'} />
    </article>
  )
}
