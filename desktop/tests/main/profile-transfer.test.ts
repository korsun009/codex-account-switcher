import { beforeEach, expect, it, vi } from 'vitest'
const mocks = vi.hoisted(() => ({ handlers: new Map<string, (...args: any[]) => any>(), save: vi.fn(), open: vi.fn() }))
vi.mock('electron', () => ({ dialog: { showSaveDialog: mocks.save, showOpenDialog: mocks.open }, ipcMain: { handle: (name: string, fn: (...args: any[]) => any) => mocks.handlers.set(name, fn), removeHandler: vi.fn() }, nativeTheme: {} }))
import { registerIpc } from '../../src/main/ipc'
import type { BackendClient } from '../../src/main/backend-client'
import type { UpdateManager } from '../../src/main/update-manager'
const trusted = { senderFrame: { url: 'file:///app/index.html' } }
let invoke = vi.fn()
beforeEach(() => {
  invoke = vi.fn(async () => ({ ok: true, data: { count: 1 } }))
  mocks.save.mockReset(); mocks.open.mockReset()
  registerIpc({ invoke } as unknown as BackendClient, {} as UpdateManager, 'file:///app/index.html')
})
it('accepts legal legacy names but uses only the main-selected save path', async () => {
  mocks.save.mockResolvedValue({ canceled: false, filePath: 'X:/chosen.codexprofiles' })
  await mocks.handlers.get('switcher:export-profiles')!(trusted, ['legacy profile', 'legacy.profile_2026', 'Русский'], 'correct password', 'C:/ignored')
  expect(invoke).toHaveBeenCalledWith('exportProfiles', { names: ['legacy profile', 'legacy.profile_2026', 'Русский'], password: 'correct password', path: 'X:/chosen.codexprofiles' })
})
it('rejects untrusted, invalid and general-invoke transfer requests before opening a picker', async () => {
  const exportCall = mocks.handlers.get('switcher:export-profiles')!
  await expect(exportCall({ senderFrame: { url: 'https://evil.test/' } }, ['one'], 'correct password')).rejects.toThrow()
  await expect(exportCall(trusted, ['one'], 'short')).rejects.toThrow()
  await expect(exportCall(trusted, ['../auth.json'], 'correct password')).rejects.toThrow()
  await expect(mocks.handlers.get('switcher:invoke')!(trusted, 'importProfiles', { path: 'C:/auth.json' })).rejects.toThrow()
  expect(mocks.save).not.toHaveBeenCalled(); expect(invoke).not.toHaveBeenCalled()
})
it('does no backend operation after picker cancellation', async () => {
  mocks.open.mockResolvedValue({ canceled: true, filePaths: [] })
  expect(await mocks.handlers.get('switcher:import-profiles')!(trusted, 'correct password')).toEqual({ canceled: true })
  expect(invoke).not.toHaveBeenCalled()
})
