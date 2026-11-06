import { useRef, useState } from 'react'
import { api, download } from '../api'
import { amount, bomDisplayName, bomToText, bomTotals, money, priceAt, rateOf } from '../lib/parts'
import type { Bom, BomLine, BomSummary, Rates } from '../types'
import { CopyButton, ExtLink } from './common'
import { t } from '../i18n'

export function BomDrawer({ open, onClose, bom, bomId, list, currency, rates, onChange, onSelect, onCreate, onDelete, onImported }: {
  open: boolean; onClose: () => void; bom: Bom | null; bomId: number | null; list: BomSummary[]; currency: string; rates: Rates | null
  onChange: (b: Bom) => void; onSelect: (id: number) => void; onCreate: () => void; onDelete: () => void; onImported: (id: number) => void
}) {
  const file = useRef<HTMLInputElement>(null)
  const [undo, setUndo] = useState<{ index: number; line: BomLine } | null>(null)
  const [message, setMessage] = useState('')

  const setLine = (i: number, change: Partial<BomLine>) => bom && onChange({ ...bom, lines: bom.lines.map((l, n) => (n === i ? { ...l, ...change } : l)) })
  const remove = (i: number) => {
    if (!bom) return
    setUndo({ index: i, line: bom.lines[i] })
    onChange({ ...bom, lines: bom.lines.filter((_, n) => n !== i) })
  }
  const restore = () => {
    if (!bom || !undo) return
    const lines = [...bom.lines]
    lines.splice(Math.min(undo.index, lines.length), 0, undo.line)
    onChange({ ...bom, lines })
    setUndo(null)
  }
  const importFile = async (f: File) => {
    setMessage('')
    try {
      const r = await api.importBom(await f.text(), f.name.replace(/\.[^.]+$/, ''))
      setMessage(t('bom.imported', { n: r.lines }))
      onImported(r.id)
    } catch (e) {
      setMessage((e as Error).message)
    }
    if (file.current) file.current.value = ''
  }

  const totals = bom ? bomTotals(bom, currency, rates) : null
  const fileName = (bom?.name || 'BOM').replace(/[^\p{L}\p{N}_-]+/gu, '_')

  return (
    <aside className={'drawer' + (open ? ' open' : '')} aria-label="BOM" aria-hidden={!open}>
      <header className="drawer-head">
        <select aria-label={t('bom.open')} value={bomId ?? ''} onChange={e => e.target.value && onSelect(+e.target.value)}>
          {bomId == null && <option value="">{t('bom.none')}</option>}
          {list.map(b => <option key={b.id} value={b.id}>{bomDisplayName(b.name)}</option>)}
        </select>
        <button className="btn" onClick={onCreate}>{t('bom.new')}</button>
        <button className="btn" aria-label={t('bom.close')} onClick={onClose}>✕</button>
      </header>
      {!bom ? (
        <div className="empty">{t('bom.hint')}
          <div className="row-center"><button className="btn" onClick={() => file.current?.click()}>{t('bom.importCsv')}</button></div>
          <input ref={file} type="file" accept=".csv,text/csv" hidden onChange={e => e.target.files?.[0] && importFile(e.target.files[0])} />
          {message && <p>{message}</p>}
        </div>
      ) : (
        <>
          <div className="bom-meta">
            <input aria-label={t('bom.name')} placeholder={t('bom.name')} value={bom.name} onChange={e => onChange({ ...bom, name: e.target.value })} />
            <textarea aria-label={t('notes')} placeholder={t('notes')} rows={2} value={bom.notes} onChange={e => onChange({ ...bom, notes: e.target.value })} />
          </div>
          <div className="lines">
            {bom.lines.length === 0 && <div className="empty">{t('bom.empty')}</div>}
            {bom.lines.map((l, i) => {
              const unit = priceAt(l, l.qty)
              const r = currency ? rateOf(l.currency, currency, rates) : null
              const cur = r != null ? currency : l.currency
              return (
                <div className={'line' + (l.include ? '' : ' dnp')} key={`${l.supplier}|${l.supplierPartNumber}|${l.manufacturerPartNumber}|${i}`}>
                  <input type="checkbox" title={t('bom.dnpTitle')} aria-label={t('bom.count')} checked={l.include} onChange={e => setLine(i, { include: e.target.checked })} />
                  <div className="line-main">
                    <div className="mpn">{l.manufacturerPartNumber || l.supplierPartNumber || '—'}</div>
                    <div className="sub">{l.supplier} · <ExtLink href={l.productUrl}>{l.supplierPartNumber || '—'}</ExtLink> <CopyButton text={l.supplierPartNumber} label={t('copy.spn')} /></div>
                    <input className="refs" aria-label={t('bom.refs')} placeholder={t('bom.refsPlaceholder')} value={l.refs} onChange={e => setLine(i, { refs: e.target.value })} />
                  </div>
                  <div className="line-num">
                    <input type="number" min={1} aria-label={t('bom.qty')} value={l.qty} onChange={e => setLine(i, { qty: Math.max(1, Math.floor(+e.target.value) || 1) })} />
                    <div className="mono">{unit == null ? t('bom.noPrice') : `${amount(unit * (r ?? 1) * l.qty)} ${cur}`}</div>
                    <div className="sub mono">{unit != null && `× ${money(unit * (r ?? 1))}`}</div>
                  </div>
                  <button className="mini" aria-label={t('bom.removeLine')} onClick={() => remove(i)}>✕</button>
                </div>
              )
            })}
          </div>
          <div className="totals">
            {undo && <button className="linkbtn" onClick={restore}>{t('bom.undo', { name: undo.line.manufacturerPartNumber || t('bom.theLine') })}</button>}
            {totals!.bySupplier.map(s => (
              <div className="sub" key={s.supplier}><span>{s.supplier}</span><span className="mono">{Object.entries(s.amounts).map(([c, v]) => `${amount(v)} ${c}`).join(' + ')}</span></div>
            ))}
            {totals!.noPrice > 0 && <div className="sub"><span>{t('bom.noPriceLabel')}</span><span className="mono">{totals!.noPrice}</span></div>}
            <div className="all"><span>{t('total')}</span><span className="mono">{Object.entries(totals!.grand).map(([c, v]) => `${amount(v)} ${c}`).join(' + ') || '0'}</span></div>
            {totals!.converted && <p className="sub small">{t('bom.converted')}</p>}
            {message && <p className="sub small">{message}</p>}
            <div className="row-wrap">
              <button className="btn" disabled={bomId == null} onClick={() => bomId != null && (window.location.href = `/api/boms/${bomId}/csv`)}>CSV</button>
              <button className="btn" onClick={() => download(`${fileName}.txt`, bomToText(bom, currency, rates), 'text/plain;charset=utf-8')}>{t('bom.text')}</button>
              <button className="btn" onClick={() => file.current?.click()}>{t('bom.import')}</button>
              <button className="btn danger" onClick={() => { if (window.confirm(t('bom.confirmDelete', { name: bom.name || t('bom.thisBom') }))) onDelete() }}>{t('bom.delete')}</button>
              <input ref={file} type="file" accept=".csv,text/csv" hidden onChange={e => e.target.files?.[0] && importFile(e.target.files[0])} />
            </div>
          </div>
        </>
      )}
    </aside>
  )
}
