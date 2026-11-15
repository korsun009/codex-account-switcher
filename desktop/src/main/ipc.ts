import { dialog, ipcMain, nativeTheme, type IpcMainInvokeEvent } from 'electron'
import { isBackendCommand, type DesktopApi } from '../shared/contracts'
import {
  allowedIpcChannels,
  IPC_EXPORT_PROFILES,
  IPC_IMPORT_PROFILES,
  IPC_INVOKE,
  IPC_SELECT_CODEX_HOME,
  IPC_UPDATE_CHECK,
  IPC_UPDATE_DOWNLOAD,
  IPC_UPDATE_GET,
  IPC_UPDATE_INSTALL,
  IPC_UPDATE_OPEN_RELEASE
} from '../shared/ipc'
import { BackendClient } from './backend-client'
import type { UpdateManager } from './update-manager'
import { isTrustedSender } from './window-security'

function assertTrusted(event: IpcMainInvokeEvent, trustedEntryUrl: string): void {
  if (!event.senderFrame || !isTrustedSender(event.senderFrame.url, trustedEntryUrl)) {
    throw new Error('Untrusted renderer request.')
  }
}

export function registerIpc(backend: BackendClient, updates: UpdateManager, trustedEntryUrl: string): void {
  ipcMain.handle(IPC_EXPORT_PROFILES, async (event, names: unknown, password: unknown) => {
    assertTrusted(event, trustedEntryUrl)
    validateTransferPassword(password)
    if (!Array.isArray(names) || names.length < 1 || names.length > 1000 ||
        names.some(n => typeof n !== 'string' || n.length < 1 || n.length > 200 || /[\u0000-\u001f\u007f\\/]/.test(n)) || new Set(names).size !== names.length)
      throw new Error('transfer.invalidSelection')
    const result = await dialog.showSaveDialog({ defaultPath: 'Codex.codexprofiles', filters: [{ name: 'Codex profiles', extensions: ['codexprofiles'] }], properties: ['dontAddToRecent'] })
    if (result.canceled || !result.filePath) return { canceled: true }
    const response = await backend.invoke<{count: number}>('exportProfiles', { names, password, path: result.filePath })
    return response.ok ? { canceled: false, count: response.data?.count } : { canceled: false, error: response.error }
  })
  ipcMain.handle(IPC_IMPORT_PROFILES, async (event, password: unknown) => {
    assertTrusted(event, trustedEntryUrl)
    validateTransferPassword(password)
    const result = await dialog.showOpenDialog({ filters: [{ name: 'Codex profiles', extensions: ['codexprofiles'] }], properties: ['openFile', 'dontAddToRecent'] })
    if (result.canceled || !result.filePaths[0]) return { canceled: true }
    const response = await backend.invoke<{count: number}>('importProfiles', { password, path: result.filePaths[0] })
    return response.ok ? { canceled: false, count: response.data?.count } : { canceled: false, error: response.error }
  })
  ipcMain.handle(IPC_INVOKE, async (event, command: unknown, payload?: unknown) => {
    assertTrusted(event, trustedEntryUrl)
    if (!isBackendCommand(command)) {
      throw new Error('Unsupported desktop command.')
    }
    const response = await backend.invoke(command, payload)
    if (command === 'setTheme' && response.ok && payload && typeof payload === 'object' && 'theme' in payload) {
      const theme = (payload as { theme?: unknown }).theme
      if (theme === 'system' || theme === 'dark' || theme === 'light') {
        nativeTheme.themeSource = theme
      }
    }
    return response
  })

  ipcMain.handle(IPC_SELECT_CODEX_HOME, async (event, requestedTitle?: unknown) => {
    assertTrusted(event, trustedEntryUrl)
    const title = typeof requestedTitle === 'string' && requestedTitle.length <= 120 && !/[\u0000-\u001f\u007f]/.test(requestedTitle)
      ? requestedTitle
      : 'Select Codex Home'
    const result = await dialog.showOpenDialog({
      title,
      properties: ['openDirectory', 'dontAddToRecent']
    })
    return result.canceled ? null : (result.filePaths[0] ?? null)
  })

  ipcMain.handle(IPC_UPDATE_GET, (event) => {
    assertTrusted(event, trustedEntryUrl)
    return updates.get()
  })

  ipcMain.handle(IPC_UPDATE_CHECK, async (event) => {
    assertTrusted(event, trustedEntryUrl)
    return await updates.check()
  })

  ipcMain.handle(IPC_UPDATE_DOWNLOAD, async (event) => {
    assertTrusted(event, trustedEntryUrl)
    return await updates.download()
  })

  ipcMain.handle(IPC_UPDATE_INSTALL, async (event) => {
    assertTrusted(event, trustedEntryUrl)
    await updates.install()
  })

  ipcMain.handle(IPC_UPDATE_OPEN_RELEASE, async (event) => {
    assertTrusted(event, trustedEntryUrl)
    await updates.openRelease()
  })
}

export function unregisterIpc(): void {
  for (const channel of allowedIpcChannels) {
    ipcMain.removeHandler(channel)
  }
}

export type { DesktopApi }
export {
  allowedIpcChannels,
  IPC_EXPORT_PROFILES,
  IPC_IMPORT_PROFILES,
  IPC_INVOKE,
  IPC_SELECT_CODEX_HOME,
  IPC_UPDATE_CHECK,
  IPC_UPDATE_DOWNLOAD,
  IPC_UPDATE_GET,
  IPC_UPDATE_INSTALL,
  IPC_UPDATE_OPEN_RELEASE
}

function validateTransferPassword(value: unknown): asserts value is string {
  if (typeof value !== 'string' || value.length < 12 || value.length > 1024) throw new Error('transfer.passwordInvalid')
}
