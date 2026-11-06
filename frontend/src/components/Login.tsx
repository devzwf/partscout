import { FormEvent, useState } from 'react'
import { api } from '../api'
import { changeLang, otherLang, t } from '../i18n'

export function Login({ setup, tokenRequired, onDone }: { setup: boolean; tokenRequired: boolean; onDone: () => void }) {
  const [name, setName] = useState(setup ? 'admin' : localStorage.getItem('ps.name') ?? '')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [token, setToken] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')
    if (setup && password !== confirm) { setError(t('login.mismatch')); return }
    setBusy(true)
    try {
      if (setup) await api.setup(name, password, token)
      else await api.login(name, password)
      localStorage.setItem('ps.name', name.trim())
      onDone()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <form onSubmit={submit}>
        <div className="logo big">Part<b>Scout</b></div>
        <p className="sub">{setup ? t('login.setup') : t('login.title')}</p>
        {setup && tokenRequired && (
          <label>{t('login.token')} (<code>PARTSCOUT_SETUP_TOKEN</code>)
            <input value={token} onChange={e => setToken(e.target.value)} autoComplete="off" required />
          </label>
        )}
        <label>{setup ? t('login.setupName') : t('login.name')}
          <input value={name} onChange={e => setName(e.target.value)} autoFocus={!name} required autoCapitalize="none" autoComplete="username" />
        </label>
        <label>{t('login.password')}{setup ? t('login.passwordMin') : ''}
          <input type="password" value={password} onChange={e => setPassword(e.target.value)} autoFocus={!!name} required autoComplete={setup ? 'new-password' : 'current-password'} />
        </label>
        {setup && <label>{t('login.confirm')}<input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} required autoComplete="new-password" /></label>}
        {error && <p className="error" role="alert">{error}</p>}
        <button className="btn primary" disabled={busy}>{setup ? t('login.create') : t('login.submit')}</button>
        <button type="button" className="linkbtn lang-switch" title={t('lang.switch')} onClick={() => changeLang(otherLang())}>{otherLang() === 'fr' ? 'Français' : 'English'}</button>
      </form>
    </main>
  )
}
