import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { ProfileTransfer } from '../../src/renderer/src/ProfileTransfer'
import { createTranslator } from '../../src/renderer/src/locales/i18n'
import type { DesktopApi, ProfileSummary } from '../../src/shared/contracts'
const profiles: ProfileSummary[] = [
  { name: 'saved', displayName: 'Saved', credentialStatus: 'ready', active: false, hasCredentials: true },
  { name: 'missing', displayName: 'Missing', credentialStatus: 'missing', active: false, hasCredentials: false }
]
beforeEach(() => {
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
})
it('requires selection, a strong matching password, and sends only selected names', async () => {
  const exportProfiles = vi.fn(async () => ({ canceled: false, count: 1 }))
  window.codexSwitcher = { exportProfiles } as unknown as DesktopApi
  const refresh = vi.fn(async () => {})
  render(<ProfileTransfer profiles={profiles} refresh={refresh} t={createTranslator('en')} />)
  fireEvent.click(screen.getByRole('button', { name: 'Export profiles' }))
  expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  expect(screen.getByRole('checkbox', { name: /Missing/ })).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Saved' }))
  fireEvent.click(screen.getByRole('button', { name: 'Next' }))
  fireEvent.change(screen.getByLabelText('Archive password'), { target: { value: 'correct password' } })
  expect(screen.getByRole('button', { name: 'Choose file' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Confirm password'), { target: { value: 'correct password' } })
  fireEvent.click(screen.getByRole('button', { name: 'Choose file' }))
  await waitFor(() => expect(exportProfiles).toHaveBeenCalledWith(['saved'], 'correct password'))
  expect(await screen.findByRole('status')).toHaveTextContent('Exported profiles: 1.')
  expect(refresh).toHaveBeenCalledOnce()
})
it('clears the password after authentication failure and permits retry', async () => {
  const importProfiles = vi.fn().mockResolvedValueOnce({ canceled: false, error: 'transfer.authenticationFailed' }).mockResolvedValueOnce({ canceled: false, count: 1 })
  window.codexSwitcher = { importProfiles } as unknown as DesktopApi
  render(<ProfileTransfer profiles={profiles} refresh={async () => {}} t={createTranslator('en')} />)
  fireEvent.click(screen.getByRole('button', { name: 'Import profiles' }))
  fireEvent.change(screen.getByLabelText('Archive password'), { target: { value: 'wrong password!' } })
  fireEvent.click(screen.getByRole('button', { name: 'Choose file' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Incorrect password or damaged file.')
  expect(screen.getByLabelText('Archive password')).toHaveValue('')
  fireEvent.change(screen.getByLabelText('Archive password'), { target: { value: 'correct password' } })
  fireEvent.click(screen.getByRole('button', { name: 'Choose file' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Imported profiles: 1.')
})
