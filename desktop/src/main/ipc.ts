import { dialog, ipcMain, nativeTheme, type IpcMainInvokeEvent } from 'electron'
import { isBackendCommand, type DesktopApi } from '../shared/contracts'
import {
  allowedIpcChannels,
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
  IPC_INVOKE,
  IPC_SELECT_CODEX_HOME,
  IPC_UPDATE_CHECK,
  IPC_UPDATE_DOWNLOAD,
  IPC_UPDATE_GET,
  IPC_UPDATE_INSTALL,
  IPC_UPDATE_OPEN_RELEASE
}
