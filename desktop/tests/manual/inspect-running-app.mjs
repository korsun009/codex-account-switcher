import { chromium } from '@playwright/test'
import { resolve } from 'node:path'

const endpoint = process.argv[2] ?? 'http://127.0.0.1:9222'
const screenshotPath = resolve(process.argv[3] ?? 'test-results/electron-packaged.png')
const browser = await chromium.connectOverCDP(endpoint)
try {
  const context = browser.contexts()[0]
  const page = context?.pages()[0]
  if (!page) throw new Error('Electron renderer target was not found.')

  const rendererErrors = []
  page.on('pageerror', (error) => rendererErrors.push(error.message.slice(0, 300)))
  page.on('console', (message) => {
    if (message.type() === 'error') rendererErrors.push(message.text().slice(0, 300))
  })
  await page.reload()
  await page.waitForLoadState('domcontentloaded')
  await page.waitForSelector('.app-shell')
  await page.screenshot({ path: screenshotPath, fullPage: true })

  const result = await page.evaluate(async (screenshotPath) => {
    const bridgeAvailable = Boolean(window.codexSwitcher)
    const bridge = bridgeAvailable ? await window.codexSwitcher.invoke('bootstrap') : null
    const shell = document.querySelector('.app-shell')
    const sidebar = document.querySelector('.sidebar')
    const workspace = document.querySelector('.workspace')
    const box = (element) => {
      if (!element) return null
      const rect = element.getBoundingClientRect()
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
    }
    return {
      title: document.title,
      protocol: location.protocol,
      viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
      accountCards: document.querySelectorAll('.account-card').length,
      emptyState: Boolean(document.querySelector('.empty-state')),
      errorBanners: document.querySelectorAll('.error-banner').length,
      bridge: {
        available: bridgeAvailable,
        ok: bridge?.ok ?? false,
        profileCount: Array.isArray(bridge?.data?.profiles) ? bridge.data.profiles.length : null,
        profileKeys: bridge?.data?.profiles?.[0] ? Object.keys(bridge.data.profiles[0]).sort() : [],
        hasError: Boolean(bridge?.error)
      },
      shell: box(shell),
      sidebar: box(sidebar),
      workspace: box(workspace),
      bodyOverflowX: document.body.scrollWidth > document.body.clientWidth,
      bodyOverflowY: document.body.scrollHeight > document.body.clientHeight,
      screenshotPath
    }
  }, screenshotPath)
  result.rendererErrors = rendererErrors
  process.stdout.write(`${JSON.stringify(result)}\n`)
} finally {
  await browser.close()
}
