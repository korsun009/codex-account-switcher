import { contextBridge, ipcRenderer } from 'electron'
import type { BackendCommand, BridgeResponse, DesktopApi } from '../shared/contracts'
import {
  IPC_INVOKE,
  IPC_SELECT_CODEX_HOME,
  IPC_UPDATE_CHECK,
  IPC_UPDATE_DOWNLOAD,
  IPC_UPDATE_GET,
  IPC_UPDATE_INSTALL,
  IPC_UPDATE_OPEN_RELEASE
} from '../shared/ipc'

const api: DesktopApi = Object.freeze({
  invoke: <T>(command: BackendCommand, payload?: unknown): Promise<BridgeResponse<T>> =>
    ipcRenderer.invoke(IPC_INVOKE, command, payload) as Promise<BridgeResponse<T>>,
  selectCodexHome: (title?: string) => ipcRenderer.invoke(IPC_SELECT_CODEX_HOME, title),
  updates: Object.freeze({
    get: () => ipcRenderer.invoke(IPC_UPDATE_GET),
    check: () => ipcRenderer.invoke(IPC_UPDATE_CHECK),
    download: () => ipcRenderer.invoke(IPC_UPDATE_DOWNLOAD),
    install: () => ipcRenderer.invoke(IPC_UPDATE_INSTALL),
    openRelease: () => ipcRenderer.invoke(IPC_UPDATE_OPEN_RELEASE)
  })
})

contextBridge.exposeInMainWorld('codexSwitcher', api)
