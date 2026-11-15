import { useEffect, useRef, useState } from 'react'
import type { ProfileSummary } from '../../shared/contracts'
import type { Translator, TranslationKey } from './locales/i18n'

export function ProfileTransfer({ profiles, refresh, t }: { profiles: ProfileSummary[]; refresh: () => Promise<void>; t: Translator }) {
  const [mode, setMode] = useState<'select' | 'export' | 'import' | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { if (mode) dialog.current?.showModal(); else dialog.current?.close() }, [mode])
  function close() { setPassword(''); setConfirmation(''); setError(''); setMode(null) }
  function open(next: 'select' | 'import') { setSelected([]); setNotice(''); setError(''); setMode(next) }
  async function transfer() {
    setBusy(true); setError('')
    try {
      const result = mode === 'export'
        ? await window.codexSwitcher.exportProfiles(selected, password)
        : await window.codexSwitcher.importProfiles(password)
      if (result.error) {
        const key = ['transfer.passwordInvalid', 'transfer.invalidArchive', 'transfer.authenticationFailed', 'transfer.invalidSelection', 'transfer.busy'].includes(result.error) ? result.error : 'transfer.failed'
        setError(t(key as TranslationKey))
        return
      }
      if (result.canceled) return
      setNotice(t(mode === 'export' ? 'transfer.exported' : 'transfer.imported', { count: result.count ?? 0 }))
      close()
      await refresh()
    } catch { setError(t('transfer.failed')) }
    finally { setBusy(false); setPassword(''); setConfirmation('') }
  }
  const validPassword = password.length >= 12 && password.length <= 1024
  return <>
    <div className="setting-row">
      <div className="setting-icon" aria-hidden="true">↔</div>
      <div><strong>{t('transfer.title')}</strong><span>{t('transfer.description')}</span></div>
      <div className="transfer-actions">
        <button className="button" onClick={() => open('select')}>{t('transfer.export')}</button>
        <button className="button" onClick={() => open('import')}>{t('transfer.import')}</button>
      </div>
    </div>
    {notice && <p role="status">{notice}</p>}
    <dialog ref={dialog} className="transfer-dialog" aria-labelledby="transfer-title" onCancel={e => { e.preventDefault(); if (!busy) close() }}>
      <h2 id="transfer-title">{t(mode === 'select' ? 'transfer.select' : mode === 'export' ? 'transfer.export' : 'transfer.import')}</h2>
      {mode === 'select' ? <>
        <p>{t('transfer.savedOnly')}</p>
        <div className="transfer-profiles">{profiles.map(profile => <label key={profile.name}>
          <input type="checkbox" disabled={profile.credentialStatus !== 'ready'} checked={selected.includes(profile.name)} onChange={e => setSelected(current => e.target.checked ? [...current, profile.name] : current.filter(n => n !== profile.name))} />
          {profile.displayName}{profile.credentialStatus !== 'ready' && <small> — {t('transfer.unavailable')}</small>}
        </label>)}</div>
        {profiles.length === 0 && <p>{t('transfer.empty')}</p>}
      </> : <form id="transfer-form" onSubmit={e => { e.preventDefault(); if (!busy && validPassword && (mode !== 'export' || confirmation === password)) void transfer() }}>
        <p>{t(mode === 'export' ? 'transfer.passwordHelp' : 'transfer.importHelp')}</p>
        <label>{t('transfer.password')}<input autoFocus type="password" autoComplete="off" minLength={12} maxLength={1024} value={password} disabled={busy} onChange={e => setPassword(e.target.value)} /></label>
        {mode === 'export' && <label>{t('transfer.confirm')}<input type="password" autoComplete="off" maxLength={1024} value={confirmation} disabled={busy} onChange={e => setConfirmation(e.target.value)} /></label>}
      </form>}
      {error && <p role="alert">{error}</p>}
      <div className="transfer-actions">
        <button className="button" disabled={busy} onClick={close}>{t('transfer.cancel')}</button>
        {mode === 'select' ? <button className="button button--primary" disabled={!selected.length || selected.length > 1000} onClick={() => setMode('export')}>{t('transfer.next')}</button>
          : <button className="button button--primary" type="submit" form="transfer-form" disabled={busy || !validPassword || (mode === 'export' && password !== confirmation)}>{t(busy ? 'transfer.working' : 'transfer.chooseFile')}</button>}
      </div>
    </dialog>
  </>
}

