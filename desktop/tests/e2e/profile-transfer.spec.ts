import { _electron as electron, expect, test } from '@playwright/test'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join } from 'node:path'

test('packaged Settings export/import preserves profiles and rejects wrong passwords', async () => {
  test.setTimeout(120_000)
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
  const app = await electron.launch({ executablePath, env: { ...process.env, CODEX_HOME: home, CODEX_SWITCHER_DATA_DIR: join(root, 'database'), CODEX_SWITCHER_E2E: '1', CODEX_SWITCHER_E2E_USER_DATA: join(root, 'electron') } })
  try {
    // Only OS picker results are controlled; the renderer, preload, IPC, backend,
    // archive encryption and target DPAPI writes are the packaged production code.
    await app.evaluate(({ dialog }, path) => {
      dialog.showSaveDialog = async () => ({ canceled: false, filePath: path })
      dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [path] })
    }, archive)
    const page = await app.firstWindow()
    await page.getByRole('button', { name: 'Настройки', exact: true }).click()
    await page.getByRole('button', { name: 'Экспорт профилей', exact: true }).click()
    await page.getByRole('checkbox', { name: 'Synthetic transfer', exact: true }).check()
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
  } finally { await app.close() }
})
