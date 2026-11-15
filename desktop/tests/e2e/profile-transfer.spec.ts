import { chromium, expect, test, type Browser } from '@playwright/test'
import { spawn } from 'node:child_process'
import { createServer } from 'node:net'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'

test('packaged Settings export/import preserves profiles and rejects wrong passwords', async () => {
  test.setTimeout(240_000)
  const executablePath = process.env.CODEX_SWITCHER_TRANSFER_EXE
  test.skip(!executablePath, 'Set CODEX_SWITCHER_TRANSFER_EXE to an isolated packaged executable.')
  const rootBase = process.env.CODEX_SWITCHER_TRANSFER_TEST_ROOT ?? 'X:/DevData/Temp'
  await mkdir(rootBase, { recursive: true })
  const root = await mkdtemp(join(rootBase, 'profile-transfer-e2e-'))
  const home = join(root, '.codex')
  const profileDir = join(home, '_account_profiles', 'synthetic')
  await mkdir(profileDir, { recursive: true })
  const live = '{"token":"synthetic-live-unchanged"}'
  await writeFile(join(home, 'auth.json'), live)
  await writeFile(join(profileDir, 'auth.json'), '{"token":"synthetic-saved-transfer"}')
  await writeFile(join(home, '_account_profiles', 'profiles.json'), JSON.stringify({ profiles: [{ name: 'synthetic', displayName: 'Synthetic transfer' }] }))
  const archive = join(root, 'transfer.codexprofiles')
  const mainPort = await freePort()
  const rendererPort = await freePort()
  const env: NodeJS.ProcessEnv = { ...process.env, CODEX_HOME: home, CODEX_SWITCHER_DATA_DIR: join(root, 'database'), CODEX_SWITCHER_E2E: '1', CODEX_SWITCHER_E2E_USER_DATA: join(root, 'electron') }
  delete env.NODE_OPTIONS
  delete env.ELECTRON_RUN_AS_NODE
  const child = spawn(executablePath!, [`--inspect=127.0.0.1:${mainPort}`, `--remote-debugging-port=${rendererPort}`, '--remote-debugging-address=127.0.0.1'], { env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  let processLog = ''
  child.stdout.on('data', chunk => { processLog = (processLog + String(chunk)).slice(-16000) })
  child.stderr.on('data', chunk => { processLog = (processLog + String(chunk)).slice(-16000) })
  let browser: Browser | undefined
  let inspector: Inspector | undefined
  try {
    const targets = await pollJson<{ webSocketDebuggerUrl: string }[]>(`http://127.0.0.1:${mainPort}/json/list`)
    inspector = await Inspector.connect(targets[0].webSocketDebuggerUrl)
    // Only the two OS picker results are controlled through the main-process
    // inspector. All production renderer/IPC/archive/DPAPI code remains intact.
    await inspector.evaluate(`(() => {
      const { dialog } = require('electron');
      dialog.showSaveDialog = async () => ({ canceled: false, filePath: ${JSON.stringify(archive)} });
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [${JSON.stringify(archive)}] });
      return true;
    })()`)
    await pollJson(`http://127.0.0.1:${rendererPort}/json/version`)
    browser = await chromium.connectOverCDP(`http://127.0.0.1:${rendererPort}`, { timeout: 60_000 })
    const context = browser.contexts()[0]
    const page = context.pages().find(p => p.url().startsWith('file:')) ?? context.pages()[0] ?? await context.waitForEvent('page')
    const diagnostics: string[] = []
    page.on('pageerror', error => diagnostics.push(error.message))
    page.on('console', message => { if (message.type() === 'error') diagnostics.push(message.text()) })
    try {
      await page.waitForLoadState('domcontentloaded', { timeout: 60_000 })
      await page.locator('.app-shell').waitFor({ state: 'visible', timeout: 60_000 })
    } catch (error) {
      await writeFile(join(root, 'startup-diagnostics.json'), JSON.stringify({ url: page.url(), windows: context.pages().map(w => w.url()), diagnostics, html: await page.content() }, null, 2))
      await page.screenshot({ path: join(root, 'startup-failure.png') })
      throw error
    }
    await page.getByRole('button', { name: 'Настройки', exact: true }).click()
    await page.screenshot({ path: join(root, 'settings.png') })
    await page.getByRole('button', { name: 'Экспорт профилей', exact: true }).click()
    await page.getByRole('checkbox', { name: 'Synthetic transfer', exact: true }).check()
    await page.screenshot({ path: join(root, 'selection.png') })
    await page.getByRole('button', { name: 'Далее', exact: true }).click()
    await page.getByLabel('Пароль архива', { exact: true }).fill('correct test password')
    await page.getByLabel('Повтори пароль', { exact: true }).fill('correct test password')
    await page.getByRole('button', { name: 'Выбрать файл', exact: true }).click()
    await expect(page.getByRole('status')).toContainText('Экспортировано профилей: 1.')
    expect((await readFile(archive)).includes(Buffer.from('synthetic-saved-transfer'))).toBe(false)
    await page.getByRole('button', { name: 'Импорт профилей', exact: true }).click()
    await page.getByLabel('Пароль архива', { exact: true }).fill('incorrect test password')
    await page.getByRole('button', { name: 'Выбрать файл', exact: true }).click()
    await expect(page.getByRole('alert')).toContainText('Неверный пароль')
    expect(await page.evaluate(async () => (await window.codexSwitcher.invoke<{ name: string }[]>('listProfiles')).data?.length)).toBe(1)
    await page.getByLabel('Пароль архива', { exact: true }).fill('correct test password')
    await page.getByRole('button', { name: 'Выбрать файл', exact: true }).click()
    await expect(page.getByRole('status')).toContainText('Импортировано профилей: 1.')
    await page.getByRole('button', { name: 'Аккаунты', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Synthetic transfer', exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Synthetic transfer (2)', exact: true })).toBeVisible()
    expect(await readFile(join(home, 'auth.json'), 'utf8')).toBe(live)
    await page.screenshot({ path: join(root, 'transfer-result.png') })
  } finally {
    await writeFile(join(root, 'process.log'), processLog)
    await browser?.close().catch(() => {})
    await inspector?.evaluate("require('electron').app.quit()").catch(() => {})
    inspector?.close()
    if (child.exitCode === null) await Promise.race([new Promise<void>(resolve => child.once('exit', () => resolve())), new Promise<void>(resolve => setTimeout(resolve, 5000))])
    if (child.exitCode === null && child.pid) {
      // Exact spawned test PID only; never enumerate or stop the user's Codex.
      const killer = spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' })
      await Promise.race([new Promise<void>(resolve => killer.once('exit', () => resolve())), new Promise<void>(resolve => setTimeout(resolve, 5000))])
    }
  }
})

async function freePort(): Promise<number> {
  const server = createServer()
  await new Promise<void>((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve) })
  const port = (server.address() as { port: number }).port
  await new Promise<void>(resolve => server.close(() => resolve()))
  return port
}
async function pollJson<T = unknown>(url: string): Promise<T> {
  const deadline = Date.now() + 90_000
  while (Date.now() < deadline) {
    try { const response = await fetch(url, { signal: AbortSignal.timeout(2000) }); if (response.ok) return await response.json() as T } catch { /* wait for our child to listen */ }
    await new Promise(resolve => setTimeout(resolve, 250))
  }
  throw new Error(`Test application debugger did not start: ${url}`)
}
class Inspector {
  private sequence = 0
  private constructor(private readonly socket: WebSocket) {}
  static async connect(url: string): Promise<Inspector> {
    const socket = new WebSocket(url)
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Main inspector connection timed out')), 10_000)
      socket.addEventListener('open', () => { clearTimeout(timer); resolve() }, { once: true })
      socket.addEventListener('error', () => { clearTimeout(timer); reject(new Error('Main inspector connection failed')) }, { once: true })
    })
    return new Inspector(socket)
  }
  async evaluate(expression: string): Promise<void> {
    const id = ++this.sequence
    await new Promise<void>((resolve, reject) => {
      const cleanup = () => { clearTimeout(timer); this.socket.removeEventListener('message', receive) }
      const timer = setTimeout(() => { cleanup(); reject(new Error('Main inspector evaluation timed out')) }, 10_000)
      const receive = (event: MessageEvent) => {
        const result = JSON.parse(String(event.data)) as { id?: number; error?: unknown; result?: { exceptionDetails?: unknown } }
        if (result.id !== id) return
        cleanup()
        if (result.error || result.result?.exceptionDetails) reject(new Error('Main inspector evaluation failed: ' + JSON.stringify(result)))
        else resolve()
      }
      this.socket.addEventListener('message', receive)
      this.socket.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression, includeCommandLineAPI: true, awaitPromise: true, returnByValue: true } }))
    })
  }
  close() { this.socket.close() }
}

