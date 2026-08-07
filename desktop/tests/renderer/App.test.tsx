import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../src/renderer/src/App'
import type { BootstrapData, BridgeResponse, DesktopApi, ProfileLimits, UpdateState } from '../../src/shared/contracts'

const bootstrap: BootstrapData = {
  profiles: [],
  limits: [],
  diagnostics: {
    backendVersion: '2.0.0',
    codexHome: 'C:\\Users\\test\\.codex',
    codexShells: 1,
    codexAppServers: 1,
    remoteApiConfigured: true,
    warnings: []
  },
  settings: { language: 'ru', theme: 'dark' }
}

const disabledUpdateState: UpdateState = { supported: false, phase: 'disabled', currentVersion: '2.0.0', availableVersion: null, releaseDate: null, downloadPercent: null }
const updates: DesktopApi['updates'] = {
  get: vi.fn(async () => disabledUpdateState),
  check: vi.fn(async () => disabledUpdateState),
  download: vi.fn(async () => disabledUpdateState),
  install: vi.fn(async () => undefined),
  openRelease: vi.fn(async () => undefined)
}

describe('App', () => {
  beforeEach(() => {
    const invoke: DesktopApi['invoke'] = async <T,>(): Promise<BridgeResponse<T>> => ({
      id: 'test',
      ok: true,
      data: bootstrap as T,
      error: null
    })
    const api: DesktopApi = {
      invoke,
      selectCodexHome: vi.fn(async () => null),
      updates
    }
    window.codexSwitcher = api
  })

  it('shows a clean empty state without seeded personal profiles', async () => {
    render(<App />)

    expect(await screen.findByText('Аккаунтов пока нет')).toBeInTheDocument()
    expect(screen.getByText(/больше не добавляет демонстрационные или чужие профили/i)).toBeInTheDocument()
    expect(document.querySelectorAll('.profile-card')).toHaveLength(0)
  })

  it('reports the active Codex process state from backend metadata', async () => {
    render(<App />)

    await waitFor(() => expect(screen.getByText('Codex запущен')).toBeInTheDocument())
    expect(screen.getByText('Профиль не выбран')).toBeInTheDocument()
  })

  it('loads real backups and exposes maintenance settings', async () => {
    const invoke = vi.fn(async (command: string): Promise<BridgeResponse<unknown>> => ({
      id: 'test',
      ok: true,
      data: (command === 'getBackups'
        ? [{ id: 'pre-switch-20260805-120000-000', createdAt: '2026-08-05T12:00:00Z', verified: true }]
        : bootstrap),
      error: null
    })) as unknown as DesktopApi['invoke']
    window.codexSwitcher = { invoke, selectCodexHome: vi.fn(async () => null), updates }
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /резервные копии/i }))
    expect(await screen.findByText('pre-switch-20260805-120000-000')).toBeInTheDocument()
    expect(invoke).toHaveBeenCalledWith('getBackups')

    fireEvent.click(screen.getByRole('button', { name: /настройки/i }))
    expect(await screen.findByLabelText('Язык интерфейса')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /проверить файловое хранение/i })).toBeInTheDocument()
    expect(screen.getByText('Обновления')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Проверить' })).toBeDisabled()
  })

  it('shows an actionable first-run screen when Codex Home is not found', async () => {
    const withoutHome = { ...bootstrap, diagnostics: { ...bootstrap.diagnostics, codexHome: null, warnings: ['Codex Home не найден.'] } }
    const invoke: DesktopApi['invoke'] = async <T,>(): Promise<BridgeResponse<T>> => ({ id: 'test', ok: true, data: withoutHome as T, error: null })
    window.codexSwitcher = { invoke, selectCodexHome: vi.fn(async () => null), updates }

    render(<App />)

    expect(await screen.findByText('Укажите Codex Home')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Выбрать папку' })).toBeInTheDocument()
    expect(screen.queryByText('Аккаунтов пока нет')).not.toBeInTheDocument()
  })

  it('switches the complete interface language immediately', async () => {
    const invoke = vi.fn(async (command: string): Promise<BridgeResponse<unknown>> => ({
      id: 'test',
      ok: true,
      data: command === 'setLanguage' ? { language: 'en' } : bootstrap,
      error: null
    })) as unknown as DesktopApi['invoke']
    window.codexSwitcher = { invoke, selectCodexHome: vi.fn(async () => null), updates }
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /настройки/i }))
    fireEvent.change(screen.getByLabelText('Язык интерфейса'), { target: { value: 'en' } })

    expect(await screen.findByRole('button', { name: 'Accounts' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument()
    expect(screen.getByLabelText('Interface language')).toHaveValue('en')
    expect(document.documentElement.lang).toBe('en')
    expect(invoke).toHaveBeenCalledWith('setLanguage', { language: 'en' })
  })

  it('renders the five-hour and weekly limit windows independently', async () => {
    const limits: ProfileLimits[] = [{
      name: 'profile-1',
      displayName: 'Test profile',
      success: true,
      fiveHour: { percentLeft: 27.5, resetAt: '2026-08-07T12:30:00Z' },
      weekly: { percentLeft: 64, resetAt: '2026-08-12T08:15:00Z' },
      fetchedAt: '2026-08-07T10:00:00Z',
      error: null
    }]
    const invoke = vi.fn(async (command: string): Promise<BridgeResponse<unknown>> => ({
      id: 'test',
      ok: true,
      data: command === 'getLimits' ? limits : { ...bootstrap, limits },
      error: null
    })) as unknown as DesktopApi['invoke']
    window.codexSwitcher = { invoke, selectCodexHome: vi.fn(async () => null), updates }
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /лимиты codex/i }))

    const progressbars = await screen.findAllByRole('progressbar')
    expect(progressbars).toHaveLength(2)
    expect(screen.getByText('5 часов')).toBeInTheDocument()
    expect(screen.getByText('Неделя')).toBeInTheDocument()
    expect(screen.getByText('27.5%')).toBeInTheDocument()
    expect(screen.getByText('64%')).toBeInTheDocument()
    expect(progressbars[0]).toHaveAttribute('aria-valuenow', '27.5')
    expect(progressbars[1]).toHaveAttribute('aria-valuenow', '64')
  })

  it('uses the supplied program logo instead of a text placeholder', async () => {
    render(<App />)

    const logo = await screen.findByRole('img', { name: 'Codex Account Switcher' })
    expect(logo).toHaveAttribute('src', expect.stringMatching(/logo/i))
    expect(document.querySelector('.brand__mark')).not.toHaveTextContent(/^C$/)
  })

  it('creates a public Remote connection without exposing its token', async () => {
    const created = {
      id: 'connection-1234567890abcdef1234567890abcdef',
      displayName: 'Office gateway',
      type: 'telegram',
      endpoint: 'https://gateway.example/health',
      hasToken: true,
      createdUtc: '2026-08-07T10:00:00Z'
    }
    const invoke = vi.fn(async (command: string): Promise<BridgeResponse<unknown>> => ({
      id: 'test', ok: true,
      data: command === 'listRemoteConnections' ? [] : command === 'createRemoteConnection' ? created : bootstrap,
      error: null
    })) as unknown as DesktopApi['invoke']
    window.codexSwitcher = { invoke, selectCodexHome: vi.fn(async () => null), updates }
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: 'Удаленное управление' }))
    fireEvent.change(await screen.findByLabelText('Название подключения'), { target: { value: 'Office gateway' } })
    fireEvent.change(screen.getByLabelText('Тип подключения'), { target: { value: 'telegram' } })
    fireEvent.change(screen.getByLabelText('URL проверки состояния'), { target: { value: 'https://gateway.example/health' } })
    fireEvent.change(screen.getByLabelText('Секретный токен'), { target: { value: 'private-test-token' } })
    fireEvent.click(screen.getByRole('button', { name: /добавить подключение/i }))

    expect(await screen.findByText('Office gateway')).toBeInTheDocument()
    expect(screen.getByText('Токен защищен')).toBeInTheDocument()
    expect(screen.queryByText('private-test-token')).not.toBeInTheDocument()
  })
})
