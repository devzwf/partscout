import { FormEvent, useState } from 'react'
import type { Advanced as Adv, Mode, SupplierInfo } from '../types'
import { Modal } from './common'
import { t } from '../i18n'

export const defaultAdvanced = (): Adv => ({ inStockOnly: false, rohsOnly: false, maxResults: 25, manufacturerContains: '', partNoContains: '', mustContain: '', maxUnitPrice: null, containsIsQuery: false })

export function AdvancedDialog({ initial, query, mode, suppliers, chosen, onSearch, onClose }: {
  initial: Adv; query: string; mode: Mode; suppliers: SupplierInfo[]; chosen: string[]
  onSearch: (query: string, mode: Mode, a: Adv, suppliers: string[]) => void; onClose: () => void
}) {
  const [a, setA] = useState(initial)
  const [q, setQ] = useState(query)
  const [m, setM] = useState(mode)
  const [price, setPrice] = useState(initial.maxUnitPrice?.toString() ?? '')
  const [picked, setPicked] = useState(new Set(chosen.length ? chosen : suppliers.filter(s => s.enabled).map(s => s.name)))
  const [error, setError] = useState('')
  const set = <K extends keyof Adv>(k: K, v: Adv[K]) => setA(x => ({ ...x, [k]: v }))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    let query2 = q.trim()
    let mode2 = m
    let containsIsQuery = false
    if (!query2) {
      // Sans texte de recherche, on cherche le fragment de numéro et les mots : les fournisseurs les comparent à leur façon.
      query2 = [a.partNoContains.trim(), a.mustContain.trim()].filter(Boolean).join(' ')
      containsIsQuery = true
      mode2 = 'Keyword'
    }
    if (query2.length < 2) { setError(t('adv.errShort')); return }
    let maxUnitPrice: number | null = null
    if (price.trim()) {
      maxUnitPrice = Number(price.replace(',', '.').replace(/[^\d.]/g, ''))
      if (!(maxUnitPrice > 0)) { setError(t('adv.errPrice')); return }
    }
    if (picked.size === 0) { setError(t('adv.errSupplier')); return }
    onSearch(query2, mode2, { ...a, maxUnitPrice, containsIsQuery }, [...picked])
  }

  return (
    <Modal title={t('adv.title')} onClose={onClose}>
      <form className="form" onSubmit={submit}>
        <label>{t('search')}<input value={q} onChange={e => setQ(e.target.value)} autoFocus className="mono" /></label>
        <label>{t('adv.mode')}
          <select value={m} onChange={e => setM(e.target.value as Mode)}>
            <option value="Keyword">{t('mode.keyword')}</option><option value="PartNumber">{t('mode.partNumberLong')}</option>
            <option value="ExactPartNumber">{t('mode.exactLong')}</option><option value="SupplierCode">{t('mode.code')}</option>
          </select>
        </label>
        <label>{t('adv.partNo')}<input value={a.partNoContains} onChange={e => set('partNoContains', e.target.value)} placeholder="931G" className="mono" /></label>
        <label>{t('adv.description')}<input value={a.mustContain} onChange={e => set('mustContain', e.target.value)} placeholder={t('adv.descriptionPlaceholder')} /></label>
        <label>{t('adv.maker')}<input value={a.manufacturerContains} onChange={e => set('manufacturerContains', e.target.value)} /></label>
        <label>{t('adv.maxPrice')}<input value={price} onChange={e => setPrice(e.target.value)} inputMode="decimal" placeholder={t('adv.maxPricePlaceholder')} /></label>
        <label>{t('adv.maxResults')}<input type="number" min={1} max={50} value={a.maxResults} onChange={e => set('maxResults', Math.min(50, Math.max(1, +e.target.value || 25)))} /></label>
        <div className="checks">
          <label><input type="checkbox" checked={a.inStockOnly} onChange={e => set('inStockOnly', e.target.checked)} /> {t('adv.inStock')}</label>
          <label title={t('adv.rohsTitle')}><input type="checkbox" checked={a.rohsOnly} onChange={e => set('rohsOnly', e.target.checked)} /> {t('adv.rohs')}</label>
        </div>
        <fieldset>
          <legend>{t('adv.suppliers')}</legend>
          <div className="checks cols">{suppliers.map(s => (
            <label key={s.name}><input type="checkbox" checked={picked.has(s.name)} onChange={e => setPicked(p => { const n = new Set(p); if (e.target.checked) n.add(s.name); else n.delete(s.name); return n })} /> {s.name}</label>
          ))}</div>
        </fieldset>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row-end">
          <button type="button" className="btn" onClick={() => { setA(defaultAdvanced()); setPrice('') }}>{t('clear')}</button>
          <button className="btn primary">{t('search')}</button>
        </div>
      </form>
    </Modal>
  )
}
