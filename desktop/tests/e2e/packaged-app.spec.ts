import { chromium, expect, test, type Browser, type Page } from '@playwright/test'

const endpoint = process.env.ELECTRON_CDP_URL ?? 'http://127.0.0.1:9224'

let browser: Browser
let page: Page

test.beforeAll(async () => {
  browser = await chromium.connectOverCDP(endpoint)
  const context = browser.contexts()[0]
  page = context?.pages()[0]
  if (!page) throw new Error('Packaged Electron renderer was not found.')
  await page.waitForLoadState('domcontentloaded')
  await page.locator('.app-shell').waitFor()
})

test.afterAll(async () => {
  await browser?.close()
})

test('keeps the renderer sandboxed and exposes only metadata', async () => {
  const result = await page.evaluate(async () => {
    const globals = window as unknown as Record<string, unknown>
    const api = globals.codexSwitcher as { invoke(command: string): Promise<{ ok: boolean; data?: { profiles?: unknown[] }; error?: string }> }
    const response = await api.invoke('bootstrap')
    return {
      hasBridge: Boolean(api),
      hasNodeProcess: typeof globals.process !== 'undefined',
      hasRequire: typeof globals.require !== 'undefined',
      ok: response.ok,
      profileCount: response.data?.profiles?.length ?? 0,
      serialized: JSON.stringify(response)
    }
  })

  expect(result.hasBridge).toBe(true)
  expect(result.hasNodeProcess).toBe(false)
  expect(result.hasRequire).toBe(false)
  expect(result.ok).toBe(true)
  expect(result.profileCount).toBeGreaterThanOrEqual(0)
  expect(result.serialized).not.toContain('access_token')
  expect(result.serialized).not.toContain('refresh_token')
})

test('renders every primary page without overflow or renderer errors', async () => {
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  for (const name of ['Аккаунты', 'Лимиты Codex', 'Резервные копии', 'Удаленное управление', 'Настройки']) {
    await page.getByRole('button', { name }).click()
    await expect(page.locator('.page-stack')).toBeVisible()
  }

  const layout = await page.evaluate(() => ({
    bodyOverflowX: document.body.scrollWidth > document.body.clientWidth,
    appWidth: document.querySelector('.app-shell')?.getBoundingClientRect().width ?? 0,
    viewportWidth: innerWidth,
    errorBanners: document.querySelectorAll('.error-banner').length
  }))
  expect(layout.bodyOverflowX).toBe(false)
  expect(Math.abs(layout.appWidth - layout.viewportWidth)).toBeLessThan(2)
  expect(layout.errorBanners).toBe(0)
  expect(errors).toEqual([])
  await page.screenshot({ path: 'test-results/packaged-app-settings.png', fullPage: true })
})

test('centers status text inside pills in row layouts', async () => {
  const geometry = await page.evaluate(() => {
    const host = document.querySelector('.page-stack')
    if (!host) throw new Error('Page host was not found.')
    const row = document.createElement('article')
    row.className = 'backup-row'
    row.innerHTML = '<div class="setting-icon"></div><div><strong>pre-switch-20260807-050000-000</strong><span>07.08.2026, 05:00</span></div><span class="status-pill status-pill--ready">Манифест найден</span><button class="button">Восстановить</button>'
    host.append(row)
    const pill = row.querySelector('.status-pill') as HTMLElement
    const range = document.createRange()
    range.selectNodeContents(pill)
    const pillBox = pill.getBoundingClientRect()
    const textBox = range.getBoundingClientRect()
    return {
      display: getComputedStyle(pill).display,
      horizontalDelta: Math.abs((pillBox.left + pillBox.right) / 2 - (textBox.left + textBox.right) / 2),
      verticalDelta: Math.abs((pillBox.top + pillBox.bottom) / 2 - (textBox.top + textBox.bottom) / 2)
    }
  })

  // A direct grid item blockifies inline-flex to flex while preserving its inner flex layout.
  expect(['inline-flex', 'flex']).toContain(geometry.display)
  expect(geometry.horizontalDelta).toBeLessThanOrEqual(1)
  expect(geometry.verticalDelta).toBeLessThanOrEqual(1)
  await page.screenshot({ path: 'test-results/packaged-app-status-pill-centering.png', fullPage: true })
})

test('checks and downloads a newer package from the explicit E2E feed', async () => {
  const expectedVersion = process.env.CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION
  test.skip(!expectedVersion, 'No E2E update feed was configured.')

  const checked = await page.evaluate(async () => await window.codexSwitcher.updates.check())
  expect(checked).toMatchObject({
    supported: true,
    phase: 'available',
    availableVersion: expectedVersion
  })

  const downloaded = await page.evaluate(async () => await window.codexSwitcher.updates.download())
  expect(downloaded).toMatchObject({
    supported: true,
    phase: 'downloaded',
    availableVersion: expectedVersion,
    downloadPercent: 100
  })
})

test('switches languages immediately and renders the light theme at supported sizes', async () => {
  await page.getByRole('button', { name: 'Настройки' }).click()
  const settingsSelects = page.locator('.settings-list select')
  await expect(settingsSelects).toHaveCount(2)

  await settingsSelects.nth(0).selectOption('en')
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.lang)).toBe('en')

  await settingsSelects.nth(1).selectOption('light')
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')

  for (const size of [
    { width: 1360, height: 820, name: '1360x820' },
    { width: 1100, height: 700, name: '1100x700' },
    { width: 980, height: 660, name: '980x660' }
  ]) {
    await page.setViewportSize(size)
    const layout = await page.evaluate(() => ({
      overflowX: document.body.scrollWidth > document.body.clientWidth,
      surface: getComputedStyle(document.documentElement).getPropertyValue('--surface').trim(),
      background: getComputedStyle(document.documentElement).getPropertyValue('--app-bg').trim()
    }))
    expect(layout.overflowX).toBe(false)
    expect(layout.surface).not.toBe('')
    expect(layout.background).not.toBe('')
    await page.screenshot({ path: `test-results/packaged-app-light-${size.name}.png`, fullPage: true })
  }

  await settingsSelects.nth(0).selectOption('zh')
  await expect(page.getByRole('heading', { name: '设置' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.lang)).toBe('zh-CN')
  await page.screenshot({ path: 'test-results/packaged-app-light-zh.png', fullPage: true })
})
