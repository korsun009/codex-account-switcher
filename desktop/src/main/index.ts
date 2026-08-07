import { app, BrowserWindow, nativeTheme, shell } from 'electron'
import electronUpdater from 'electron-updater'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { pathToFileURL } from 'node:url'
import { BackendClient } from './backend-client'
import { registerIpc, unregisterIpc } from './ipc'
import { UpdateManager } from './update-manager'
import { configureE2EUpdateFeed } from './update-feed'
import { createWindowOptions } from './window-security'

const currentDirectory = dirname(fileURLToPath(import.meta.url))
const { autoUpdater } = electronUpdater
const isE2E = process.env.CODEX_SWITCHER_E2E === '1'
configureE2EUpdateFeed(autoUpdater, isE2E, process.env.CODEX_SWITCHER_E2E_UPDATE_URL)
let mainWindow: BrowserWindow | null = null
const backend = new BackendClient()
const updates = new UpdateManager({
  isPackaged: app.isPackaged,
  currentVersion: app.getVersion(),
  updater: autoUpdater,
  openExternal: (url) => shell.openExternal(url)
})

if (isE2E && process.env.CODEX_SWITCHER_E2E_USER_DATA) {
  app.setPath('userData', join(process.env.CODEX_SWITCHER_E2E_USER_DATA))
}

function rendererEntry(): string {
  if (!app.isPackaged && process.env.ELECTRON_RENDERER_URL) {
    return process.env.ELECTRON_RENDERER_URL
  }
  return pathToFileURL(join(currentDirectory, '../renderer/index.html')).toString()
}

async function createMainWindow(): Promise<void> {
  const entry = rendererEntry()
  const preload = join(currentDirectory, '../preload/index.cjs')
  nativeTheme.themeSource = 'system'
  mainWindow = new BrowserWindow(createWindowOptions(preload, nativeTheme.shouldUseDarkColors ? '#0d0f0e' : '#f3f5f2'))

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://github.com/korsun009/codex-account-switcher')) {
      void shell.openExternal(url)
    }
    return { action: 'deny' }
  })
  mainWindow.webContents.on('will-navigate', (event, url) => {
    if (url !== entry) {
      event.preventDefault()
    }
  })
  mainWindow.once('ready-to-show', () => mainWindow?.show())
  mainWindow.on('closed', () => {
    mainWindow = null
  })

  registerIpc(backend, updates, entry)
  if (!app.isPackaged && process.env.ELECTRON_RENDERER_URL) {
    await mainWindow.loadURL(entry)
  } else {
    await mainWindow.loadFile(join(currentDirectory, '../renderer/index.html'))
  }

  void backend.invoke<{ theme?: 'system' | 'dark' | 'light' }>('getSettings').then((response) => {
    const theme = response.ok ? response.data?.theme : undefined
    nativeTheme.themeSource = theme === 'dark' || theme === 'light' ? theme : 'system'
  }).catch(() => {
    nativeTheme.themeSource = 'system'
  })
}

const hasLock = isE2E || app.requestSingleInstanceLock()
if (!hasLock) {
  app.quit()
} else {
  app.on('second-instance', () => {
    if (!mainWindow) {
      return
    }
    if (mainWindow.isMinimized()) {
      mainWindow.restore()
    }
    mainWindow.show()
    mainWindow.focus()
  })

  app.whenReady().then(createMainWindow)
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      void createMainWindow()
    }
  })
}

app.on('before-quit', () => {
  unregisterIpc()
  backend.dispose()
})

app.on('window-all-closed', () => app.quit())
