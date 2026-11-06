import { useEffect, useState } from 'react'
import { api } from '../api'
import type { AuthUser, Meta, Settings, SupplierInfo } from '../types'
import { Modal } from './common'
import { Key, t } from '../i18n'
import { AccountSection, UsersSection } from './AccountSections'

const KEYS: { supplier: string; link: string; fields: { name: string; label: string; labelKey?: Key }[]; help: Key }[] = [
  { supplier: 'DigiKey', link: 'https://developer.digikey.com/', help: 'set.helpDigiKey',
    fields: [{ name: 'DigiKeyClientId', label: 'Client ID' }, { name: 'DigiKeyClientSecret', label: 'Client Secret' }] },
  { supplier: 'Mouser', link: 'https://www.mouser.ca/api-hub/', help: 'set.helpMouser',
    fields: [{ name: 'MouserApiKey', label: '', labelKey: 'set.keyMouser' }] },
  { supplier: 'Newark', link: 'https://partner.element14.com/', help: 'set.helpNewark',
    fields: [{ name: 'Element14ApiKey', label: '', labelKey: 'set.keyElement14' }] },
  { supplier: 'TME', link: 'https://developers.tme.eu/en', help: 'set.helpTme',
    fields: [{ name: 'TmeToken', label: 'Token' }, { name: 'TmeSecret', label: 'Application secret' }] },
]
const QUOTA = 1000

export function SettingsDialog({ meta, user, onClose, onSaved, onLogout }: { meta: Meta; user: AuthUser; onClose: () => void; onSaved: () => void; onLogout: () => void }) {
  const [settings, setSettings] = useState<Settings | null>(null)
  const [secretsSet, setSecretsSet] = useState<Record<string, boolean>>({})
  const [unreadable, setUnreadable] = useState<string[]>([])
  const [urls, setUrls] = useState<Record<string, string>>({})
  const [secrets, setSecrets] = useState<Record<string, string>>({})
  const [suppliers, setSuppliers] = useState<SupplierInfo[]>([])
  const [tests, setTests] = useState<Record<string, { ok: boolean; message: string } | 'running'>>({})
  const [error, setError] = useState('')
  const [saved, setSaved] = useState('')

  const load = async () => {
    const [s, list] = await Promise.all([api.settings(), api.suppliers()])
    setSettings(s.settings)
    setSecretsSet(s.secretsSet)
    setUnreadable(s.unreadableSecrets)
    setUrls(s.searchUrls)
    setSuppliers(list)
  }
  useEffect(() => { load().catch(e => setError(e.message)) }, [])

  if (!settings) return <Modal title={t('set.title')} onClose={onClose}><p className="sub">{error || t('loading')}</p></Modal>
  const set = <K extends keyof Settings>(k: K, v: Settings[K]) => setSettings(s => ({ ...s!, [k]: v }))

  const save = async () => {
    setError('')
    setSaved('')
    try {
      const custom = settings.customSuppliers.filter(c => c.name.trim() || c.searchUrl.trim())
      await api.saveSettings({ ...settings, customSuppliers: custom })
      const changed = Object.fromEntries(Object.entries(secrets).filter(([, v]) => v.trim()))
      if (Object.keys(changed).length) await api.saveSecrets(changed)
      setSecrets({})
      await load()
      setSaved(t('set.saved'))
      onSaved()
    } catch (e) {
      setError((e as Error).message)
    }
  }
  const clearSecret = async (name: string) => {
    setError('')
    try {
      await api.saveSecrets({ [name]: '' })
      await load()
      onSaved()
    } catch (e) {
      setError((e as Error).message)
    }
  }
  const test = async (name: string) => {
    setTests(t => ({ ...t, [name]: 'running' }))
    try {
      const r = await api.testSupplier(name)
      setTests(t => ({ ...t, [name]: r }))
      setSuppliers(await api.suppliers())
    } catch (e) {
      setTests(t => ({ ...t, [name]: { ok: false, message: (e as Error).message } }))
    }
  }
  const options = (values: string[]) => values.map(v => <option key={v}>{v}</option>)
  const isEnabled = (s: SupplierInfo) => settings.enabledSuppliers[s.name] ?? s.enabled

  return (
    <Modal title={t('set.title')} onClose={onClose} wide>
      <div className="settings">
        <AccountSection user={user} />
        {user.isAdmin && <UsersSection me={user} />}

        <section>
          <h3>{t('set.keys')}</h3>
          <p className="sub">{t('set.keysHelp')}</p>
          {meta.demo && <p className="warn">{t('set.demo')}</p>}
          {unreadable.length > 0 && <p className="error">{t('set.unreadable', { names: unreadable.join(', ') })}</p>}
          {KEYS.map(k => {
            const info = suppliers.find(s => s.name === k.supplier)
            const result = tests[k.supplier]
            return (
              <div className="keycard" key={k.supplier}>
                <div className="keyhead">
                  <strong>{k.supplier === 'Newark' ? 'element14 (Newark, Farnell, CPC)' : k.supplier}</strong>
                  <a href={k.link} target="_blank" rel="noopener noreferrer">{t('set.getKey')}</a>
                  <span className="sub mono" title={t('set.quotaTitle')}>{t('set.quota', { n: info?.callsToday ?? 0, max: QUOTA })}</span>
                  <button className="btn" disabled={result === 'running' || !info?.configured} onClick={() => test(k.supplier)}>{result === 'running' ? t('set.testing') : t('set.test')}</button>
                </div>
                <p className="sub small">{t(k.help)}</p>
                <div className="keyfields">{k.fields.map(f => (
                  <label key={f.name}>{f.labelKey ? t(f.labelKey) : f.label} {secretsSet[f.name] && <span className="ok">{t('set.keySet')}</span>}
                    <span className="inline">
                      <input type="password" autoComplete="off" placeholder={secretsSet[f.name] ? t('set.keyKeep') : ''} value={secrets[f.name] ?? ''} onChange={e => setSecrets(s => ({ ...s, [f.name]: e.target.value }))} />
                      {secretsSet[f.name] && <button type="button" className="btn" onClick={() => clearSecret(f.name)}>{t('clear')}</button>}
                    </span>
                  </label>
                ))}</div>
                {result && result !== 'running' && <p className={result.ok ? 'ok' : 'error'} role="status">{result.message}</p>}
              </div>
            )
          })}
        </section>

        <section>
          <h3>{t('set.region')}</h3>
          <div className="grid2">
            <label>{t('set.digiKeyCountry')}<select value={settings.digiKeySite} onChange={e => set('digiKeySite', e.target.value)}>{options(meta.digiKeySites)}</select></label>
            <label>{t('set.digiKeyCurrency')}<select value={settings.digiKeyCurrency} onChange={e => set('digiKeyCurrency', e.target.value)}>{options(meta.digiKeyCurrencies)}</select></label>
            <label>{t('set.tmeCountry')}<select value={settings.tmeCountry} onChange={e => set('tmeCountry', e.target.value)}>{options(meta.tmeCountries)}</select></label>
            <label>{t('set.tmeCurrency')}<select value={settings.tmeCurrency} onChange={e => set('tmeCurrency', e.target.value)}>{options(meta.tmeCurrencies)}</select></label>
            <label>{t('set.farnellStore')}<select value={settings.farnellStore} onChange={e => set('farnellStore', e.target.value)}>{meta.farnellStores.map(s => <option key={s.id} value={s.id}>{s.country} ({s.currency})</option>)}</select></label>
            <label>{t('set.displayCurrency')}<select value={settings.convertCurrency} onChange={e => set('convertCurrency', e.target.value)}>{options(meta.convertCurrencies)}</select></label>
          </div>
        </section>

        <section>
          <h3>{t('set.defaultSuppliers')}</h3>
          <div className="checks cols">{suppliers.map(s => (
            <label key={s.name}><input type="checkbox" checked={isEnabled(s)} onChange={e => set('enabledSuppliers', { ...settings.enabledSuppliers, [s.name]: e.target.checked })} /> {s.name} <span className="sub">{s.configured ? 'API' : s.kind === 'api' ? t('set.apiNoKey') : t('set.link')}</span></label>
          ))}</div>
        </section>

        <section>
          <h3>{t('set.urls')}</h3>
          <p className="sub">{t('set.urlsHelp', { token: '{0}' })}</p>
          <div className="urls">{Object.entries(urls).filter(([n]) => !settings.customSuppliers.some(c => c.name === n)).map(([name, url]) => (
            <label key={name}>{name}<input className="mono" value={settings.searchUrls[name] ?? url} onChange={e => set('searchUrls', { ...settings.searchUrls, [name]: e.target.value })} /></label>
          ))}</div>
          <h4>{t('set.custom')}</h4>
          {settings.customSuppliers.map((c, i) => (
            <div className="inline custom" key={i}>
              <input aria-label={t('name')} placeholder={t('name')} value={c.name} onChange={e => set('customSuppliers', settings.customSuppliers.map((x, n) => (n === i ? { ...x, name: e.target.value } : x)))} />
              <input aria-label={t('set.customUrl')} className="mono" placeholder={t('set.customPlaceholder')} value={c.searchUrl} onChange={e => set('customSuppliers', settings.customSuppliers.map((x, n) => (n === i ? { ...x, searchUrl: e.target.value } : x)))} />
              <button className="btn" aria-label={t('remove')} onClick={() => set('customSuppliers', settings.customSuppliers.filter((_, n) => n !== i))}>✕</button>
            </div>
          ))}
          <button className="btn" onClick={() => set('customSuppliers', [...settings.customSuppliers, { name: '', searchUrl: '' }])}>{t('set.addSupplier')}</button>
        </section>

        <section>
          <h3>{t('set.misc')}</h3>
          <div className="row-wrap">
            <button className="btn" onClick={async () => { await api.clearHistory(); setSaved(t('set.historyCleared')); onSaved() }}>{t('set.clearHistory')}</button>
            <button className="btn" onClick={onLogout}>{t('set.logout')}</button>
          </div>
          <p className="sub small">{t('set.about1', { v: meta.version })}<a href="https://www.ianjohnston.com" target="_blank" rel="noopener noreferrer">ianjohnston.com</a>{t('set.about2')}</p>
        </section>

        <div className="sticky-save">
          {error && <span className="error" role="alert">{error}</span>}
          {saved && <span className="ok" role="status">{saved}</span>}
          <button className="btn primary" onClick={save}>{t('save')}</button>
        </div>
      </div>
    </Modal>
  )
}
