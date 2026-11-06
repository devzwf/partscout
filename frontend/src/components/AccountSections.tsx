import { FormEvent, useEffect, useState } from 'react'
import { api, ManagedUser } from '../api'
import { changeLang, getLang, Lang, t } from '../i18n'
import type { AuthUser } from '../types'

/** Mon compte : langue et mot de passe. La langue est enregistrée sur le compte, elle suit la personne d'un appareil à l'autre. */
export function AccountSection({ user }: { user: AuthUser }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState('')

  const language = async (lang: Lang) => {
    try { await api.setLanguage(lang) } catch { /* la langue s'applique quand même sur cet appareil */ }
    changeLang(lang)
  }
  const password = async (e: FormEvent) => {
    e.preventDefault()
    setError('')
    setDone('')
    try {
      await api.changePassword(current, next)
      setCurrent('')
      setNext('')
      setDone(t('account.passwordChanged'))
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <section>
      <h3>{t('account.title')}</h3>
      <p className="sub">{t('account.signedInAs', { name: user.name })}</p>
      <label className="lang">{t('set.language')}
        <select aria-label={t('set.language')} value={getLang()} onChange={e => language(e.target.value as Lang)}>
          <option value="fr">Français</option>
          <option value="en">English</option>
        </select>
      </label>
      <form className="grid2" onSubmit={password}>
        <label>{t('account.currentPassword')}<input type="password" autoComplete="current-password" required value={current} onChange={e => setCurrent(e.target.value)} /></label>
        <label>{t('account.newPassword')}<input type="password" autoComplete="new-password" required minLength={10} value={next} onChange={e => setNext(e.target.value)} /></label>
        <div className="row-wrap">
          <button className="btn">{t('account.changePassword')}</button>
          {error && <span className="error" role="alert">{error}</span>}
          {done && <span className="ok" role="status">{done}</span>}
        </div>
      </form>
    </section>
  )
}

/** Comptes de l'instance, pour les administrateurs. Pas d'inscription libre : c'est ici qu'on en crée. */
export function UsersSection({ me }: { me: AuthUser }) {
  const [users, setUsers] = useState<ManagedUser[]>([])
  const [name, setName] = useState('')
  const [password, setPassword] = useState('')
  const [isAdmin, setIsAdmin] = useState(false)
  const [newLang, setNewLang] = useState<Lang>(getLang())
  const [resetFor, setResetFor] = useState<number | null>(null)
  const [resetPassword, setResetPassword] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState('')

  const load = () => api.users().then(setUsers).catch(e => setError(e.message))
  useEffect(() => { load() }, [])

  const run = async (action: () => Promise<unknown>, message: string) => {
    setError('')
    setDone('')
    try {
      await action()
      setDone(message)
      await load()
      return true
    } catch (e) {
      setError((e as Error).message)
      return false
    }
  }
  const create = async (e: FormEvent) => {
    e.preventDefault()
    const created = name.trim()
    if (await run(() => api.createUser(created, password, isAdmin, newLang), t('users.created', { name: created }))) {
      setName('')
      setPassword('')
      setIsAdmin(false)
    }
  }
  const reset = async (e: FormEvent, u: ManagedUser) => {
    e.preventDefault()
    if (await run(() => api.resetPassword(u.id, resetPassword), t('users.passwordReset', { name: u.name }))) {
      setResetFor(null)
      setResetPassword('')
    }
  }
  const remove = (u: ManagedUser) => {
    if (window.confirm(t('users.confirmDelete', { name: u.name }))) run(() => api.deleteUser(u.id), t('users.deleted', { name: u.name }))
  }

  return (
    <section>
      <h3>{t('users.title')}</h3>
      <p className="sub">{t('users.help')}</p>
      <div className="users">
        {users.map(u => (
          <div className="keycard user" key={u.id}>
            <div className="keyhead">
              <strong>{u.name}</strong>
              <span className="sub">{u.isAdmin ? t('users.admin') : t('users.member')}</span>
              {u.id !== me.id && (
                <span className="row-wrap user-actions">
                  <button className="btn" onClick={() => { setResetFor(resetFor === u.id ? null : u.id); setResetPassword('') }}>{t('users.reset')}</button>
                  <button className="btn danger" onClick={() => remove(u)}>{t('bom.delete')}</button>
                </span>
              )}
            </div>
            {resetFor === u.id && (
              <form className="inline" onSubmit={e => reset(e, u)}>
                <input type="password" autoComplete="new-password" required minLength={10} aria-label={t('account.newPassword')} placeholder={t('account.newPassword')} value={resetPassword} onChange={e => setResetPassword(e.target.value)} autoFocus />
                <button className="btn">{t('save')}</button>
              </form>
            )}
          </div>
        ))}
      </div>
      <h4>{t('users.add')}</h4>
      <form className="grid2" onSubmit={create}>
        <label>{t('login.name')}<input autoComplete="off" required value={name} onChange={e => setName(e.target.value)} /></label>
        <label>{t('login.password')}{t('login.passwordMin')}<input type="password" autoComplete="new-password" required minLength={10} value={password} onChange={e => setPassword(e.target.value)} /></label>
        <label>{t('set.language')}
          <select value={newLang} onChange={e => setNewLang(e.target.value as Lang)}>
            <option value="fr">Français</option>
            <option value="en">English</option>
          </select>
        </label>
        <div className="checks"><label><input type="checkbox" checked={isAdmin} onChange={e => setIsAdmin(e.target.checked)} /> {t('users.makeAdmin')}</label></div>
        <div className="row-wrap">
          <button className="btn">{t('users.create')}</button>
          {error && <span className="error" role="alert">{error}</span>}
          {done && <span className="ok" role="status">{done}</span>}
        </div>
      </form>
    </section>
  )
}
