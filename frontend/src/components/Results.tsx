import { Fragment, useState } from 'react'
import type { DragEvent, KeyboardEvent, ReactNode } from 'react'
import { amount, buyable, lifecycleOf, money, Offer, offerPrice, OfferPrice, PartGroup, priceAt, rateOf, SortKey, sortBy } from '../lib/parts'
import type { Rates } from '../types'
import { CopyButton, ExtLink, LifeBadge, Thumb } from './common'
import { locale, t } from '../i18n'
import { moveColumn, stepColumn } from '../lib/columns'

interface Common {
  qty: number
  currency: string
  rates: Rates | null
  sort: SortKey[]
  onSort: (field: string, add: boolean) => void
  onAdd: (o: Offer) => void
  onRemove: (o: Offer) => void
  onImage: (url: string, alt: string) => void
  /** Ordre des colonnes déplaçables (identifiants de PART_COLUMNS ou OFFER_COLUMNS). */
  columns: string[]
  onColumns: (order: string[]) => void
}

/** Colonnes déplaçables, dans l'ordre par défaut. */
export const PART_COLUMNS = ['part', 'desc', 'pkg', 'life', 'stock', 'offers', 'price']
export const OFFER_COLUMNS = ['seq', 'photo', 'supplier', 'spn', 'mpn', 'maker', 'type', 'pkg', 'stock', 'price', 'moq', 'status']

interface Column<T> {
  id: string
  /** Clé de tri ; sans elle, l'en-tête ne trie pas. */
  field?: string
  label: string
  /** En-tête sans texte visible (photo) : le libellé sert alors d'aria-label. */
  hideLabel?: boolean
  num?: boolean
  className?: string
  cell: (row: T) => ReactNode
  cellClass?: string
  cellTitle?: (row: T) => string | undefined
}

/** Glisser-déposer des en-têtes, plus Alt+Maj+← / → au clavier. */
function useColumnDrag(order: string[], onOrder: Common['onColumns']) {
  const [dragging, setDragging] = useState<string | null>(null)
  const [over, setOver] = useState<string | null>(null)
  const end = () => { setDragging(null); setOver(null) }
  return {
    dragging, over,
    props: (id: string) => ({
      draggable: true,
      onDragStart: (e: DragEvent) => { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', id); setDragging(id) },
      onDragOver: (e: DragEvent) => { if (!dragging) return; e.preventDefault(); e.dataTransfer.dropEffect = 'move'; if (over !== id) setOver(id) },
      onDragLeave: () => { if (over === id) setOver(null) },
      onDrop: (e: DragEvent) => { e.preventDefault(); if (dragging) onOrder(moveColumn(order, dragging, id)); end() },
      onDragEnd: end,
    }),
    onKey: (id: string, e: KeyboardEvent) => {
      if (!e.altKey || !e.shiftKey || (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight')) return
      e.preventDefault()
      onOrder(stepColumn(order, id, e.key === 'ArrowLeft' ? -1 : 1))
    },
  }
}

function Heads<T>({ cols, sort, onSort, order, onOrder }: { cols: Column<T>[]; sort: SortKey[]; onSort: Common['onSort']; order: string[]; onOrder: Common['onColumns'] }) {
  const drag = useColumnDrag(order, onOrder)
  return <>{cols.map(col => {
    const i = col.field ? sort.findIndex(k => k.field === col.field) : -1
    const arrow = i < 0 ? '' : (sort[i].desc ? ' ▾' : ' ▴') + (sort.length > 1 ? String(i + 1) : '')
    const cls = [col.num && 'num', col.className, 'movable', drag.dragging === col.id && 'dragging', drag.over === col.id && drag.dragging !== col.id && (order.indexOf(drag.dragging ?? '') < order.indexOf(col.id) ? 'drop-after' : 'drop-here')].filter(Boolean).join(' ')
    return (
      <th key={col.id} className={cls} title={t('table.move')} aria-label={col.hideLabel ? col.label : undefined}
        aria-sort={col.field ? (i < 0 ? 'none' : sort[i].desc ? 'descending' : 'ascending') : undefined} {...drag.props(col.id)}>
        {col.field
          ? <button className="th" title={t('table.sort') + ' ' + t('table.move')} onClick={e => onSort(col.field!, e.shiftKey)} onKeyDown={e => drag.onKey(col.id, e)}>{col.label}{arrow}</button>
          : !col.hideLabel && <span className="th-plain" tabIndex={0} onKeyDown={e => drag.onKey(col.id, e)}>{col.label}</span>}
      </th>
    )
  })}</>
}

const ordered = <T,>(cols: Column<T>[], order: string[]) => order.map(id => cols.find(c => c.id === id)).filter((c): c is Column<T> => !!c)

const Cells = <T,>({ cols, row }: { cols: Column<T>[]; row: T }) => <>{cols.map(col => (
  <td key={col.id} className={[col.num && 'num', col.cellClass].filter(Boolean).join(' ') || undefined} title={col.cellTitle?.(row)}>{col.cell(row)}</td>
))}</>

const unitTitle = (o: Offer, qty: number, p: OfferPrice | null) => {
  const own = priceAt(o, qty)
  return p && own != null && p.currency !== o.currency ? t('table.supplierPrice', { price: money(own), cur: o.currency }) : undefined
}

function Breaks({ o, qty, currency, rates }: { o: Offer; qty: number; currency: string; rates: Rates | null }) {
  const sorted = [...o.priceBreaks].sort((a, b) => a.quantity - b.quantity)
  const current = priceAt(o, qty)
  const r = (currency && rateOf(o.currency, currency, rates)) || 1
  let marked = false
  const lastAtOrBelow = [...sorted].reverse().find(b => b.quantity <= qty) ?? sorted[0]
  return (
    <span className="breaks">
      {sorted.map((b, i) => {
        const on = !marked && b === lastAtOrBelow && b.price === current
        if (on) marked = true
        return <Fragment key={b.quantity}>{i > 0 && ' · '}{on ? <b>{b.quantity}+ {money(b.price * r)}</b> : `${b.quantity}+ ${money(b.price * r)}`}</Fragment>
      })}
    </span>
  )
}

function OfferActions({ o, onAdd, onRemove }: Pick<Common, 'onAdd' | 'onRemove'> & { o: Offer }) {
  return (
    <div className="actions">
      {o.datasheetUrl && <ExtLink href={o.datasheetUrl} className="ds" title={t('offer.datasheet')}>Datasheet</ExtLink>}
      <CopyButton text={o.supplierPartNumber} label={t('copy.spn')} />
      <button className="mini" title={t('offer.hide')} onClick={e => { e.stopPropagation(); onRemove(o) }}>⊘</button>
      <button className="mini add" title={t('offer.addBom')} onClick={e => { e.stopPropagation(); onAdd(o) }}>+ BOM</button>
    </div>
  )
}

export function GroupedTable({ groups, ...c }: Common & { groups: PartGroup[] }) {
  const [open, setOpen] = useState<Set<string>>(new Set())
  const toggle = (k: string) => setOpen(s => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n })
  const rows = sortBy(groups, c.sort, (g, f) => ({ mpn: g.mpn, maker: g.manufacturer, pkg: g.package, life: g.lifecycle, stock: g.stock, offers: g.offers.length, price: g.best?.unit })[f])
  const single = rows.length === 1
  const cols = ordered<PartGroup>([
    { id: 'part', field: 'mpn', label: t('table.part'), cell: g => <><div className="mpn">{g.mpn}</div><div className="sub">{g.manufacturer}</div></> },
    { id: 'desc', label: t('table.description'), className: 'hide-m', cellClass: 'hide-m', cell: g => <div className="desc" title={g.description}>{g.description}</div> },
    { id: 'pkg', field: 'pkg', label: t('table.package'), className: 'hide-s', cellClass: 'hide-s mono', cell: g => g.package },
    { id: 'life', field: 'life', label: t('table.lifecycle'), className: 'hide-s', cellClass: 'hide-s', cell: g => <LifeBadge life={g.lifecycle} title={[...new Set(g.offers.map(o => o.status).filter(Boolean))].join(', ')} /> },
    { id: 'stock', field: 'stock', label: t('table.stock'), num: true, className: 'col-stock', cellClass: 'mono col-stock', cell: g => g.stock.toLocaleString(locale()) },
    { id: 'offers', field: 'offers', label: t('table.offers'), num: true, className: 'col-offers', cellClass: 'mono col-offers', cell: g => g.offers.length },
    { id: 'price', field: 'price', label: t('table.bestPrice', { n: c.qty }), num: true, cell: g => g.best
      ? <><div className="best"><span title={unitTitle(g.best.offer, c.qty, g.best)}>{money(g.best.unit)} {g.best.currency}</span></div><div className="sub">{g.best.offer.supplier}</div></>
      : <span className="sub" title={t('table.unavailableTitle')}>{t('table.unavailable')}</span> },
  ], c.columns)

  return (
    <table className="grid">
      <thead><tr>
        <th aria-label={t('table.details')} /><th className="col-thumb" aria-label={t('table.photo')} />
        <Heads cols={cols} sort={c.sort} onSort={c.onSort} order={c.columns} onOrder={c.onColumns} />
      </tr></thead>
      {rows.map(g => {
        const isOpen = open.has(g.key) !== single // une seule pièce : ouverte d'office
        return (
          <tbody key={g.key} className={'part' + (isOpen ? ' open' : '')}>
            <tr className="head" tabIndex={0} aria-expanded={isOpen} onClick={() => toggle(g.key)} onKeyDown={e => { if (e.key === 'Enter') toggle(g.key) }}>
              <td><span className="chev">▸</span></td>
              <td className="col-thumb"><Thumb url={g.imageUrl} large={g.largeImageUrl} alt={g.mpn} onOpen={c.onImage} /></td>
              <Cells cols={cols} row={g} />
            </tr>
            {isOpen && (
              <tr className="offers"><td colSpan={9}>
                <table className="offer-table">
                  <thead><tr><th>{t('table.supplier')}</th><th className="o-pn">{t('table.spn')}</th><th className="num">{t('table.stock')}</th><th className="num o-moq">{t('table.moq')}</th><th className="o-br">{t('table.breaks')}</th><th className="num">{t('table.unit', { n: c.qty })}</th><th className="num o-tot">{t('total')}</th><th /></tr></thead>
                  <tbody>{g.offers.map(o => {
                    const p = offerPrice(o, c.qty, c.currency, c.rates)
                    const top = g.best?.offer.id === o.id
                    const can = buyable(o, c.qty)
                    return (
                      <tr key={o.id} className={top ? 'top' : can ? '' : 'cannot'}>
                        <td>{o.supplier}</td>
                        <td className="mono o-pn"><ExtLink href={o.productUrl} title={t('offer.product')}>{o.supplierPartNumber || '—'}</ExtLink></td>
                        <td className="num mono">{o.stock?.toLocaleString(locale()) ?? '?'}{o.stockInfo && <div className="sub info" title={o.stockInfo}>{o.stockInfo}</div>}</td>
                        <td className="num mono o-moq">{o.minOrderQty ?? '?'}</td>
                        <td className="o-br"><Breaks o={o} qty={c.qty} currency={c.currency} rates={c.rates} /></td>
                        <td className="num mono" title={unitTitle(o, c.qty, p)}>{p ? `${money(p.unit)} ${p.currency}` : '—'}</td>
                        <td className="num mono o-tot">{p ? amount(p.total) : '—'}</td>
                        <td className="num"><OfferActions o={o} onAdd={c.onAdd} onRemove={c.onRemove} /></td>
                      </tr>
                    )
                  })}</tbody>
                </table>
              </td></tr>
            )}
          </tbody>
        )
      })}
    </table>
  )
}

export function OffersTable({ offers, ...c }: Common & { offers: Offer[] }) {
  const value = (o: Offer, f: string) => {
    switch (f) {
      case 'seq': return o.seq
      case 'supplier': return o.supplier
      case 'spn': return o.supplierPartNumber
      case 'mpn': return o.manufacturerPartNumber
      case 'maker': return o.manufacturer
      case 'type': return o.category
      case 'pkg': return o.package
      case 'stock': return o.stock
      case 'moq': return o.minOrderQty
      case 'status': return o.status
      case 'price': return offerPrice(o, c.qty, c.currency, c.rates)?.unit
    }
  }
  const price = (o: Offer) => offerPrice(o, c.qty, c.currency, c.rates)
  const cols = ordered<Offer>([
    { id: 'seq', field: 'seq', label: '#', num: true, cellClass: 'mono sub', cell: o => o.seq },
    { id: 'photo', label: t('table.photo'), hideLabel: true, className: 'col-thumb', cellClass: 'col-thumb', cell: o => <Thumb url={o.imageUrl} large={o.largeImageUrl} alt={o.manufacturerPartNumber} onOpen={c.onImage} /> },
    { id: 'supplier', field: 'supplier', label: t('table.supplier'), cell: o => o.supplier },
    { id: 'spn', field: 'spn', label: t('table.spn'), className: 'hide-s', cellClass: 'mono hide-s', cell: o => <ExtLink href={o.productUrl}>{o.supplierPartNumber || '—'}</ExtLink> },
    { id: 'mpn', field: 'mpn', label: t('table.mpn'), cellClass: 'mpn', cell: o => o.manufacturerPartNumber },
    { id: 'maker', field: 'maker', label: t('table.maker'), className: 'hide-m', cellClass: 'hide-m', cell: o => o.manufacturer },
    { id: 'type', field: 'type', label: t('table.type'), className: 'hide-m col-type', cellClass: 'hide-m col-type', cell: o => <div className="desc narrow" title={o.description}>{o.category}</div> },
    { id: 'pkg', field: 'pkg', label: t('table.package'), className: 'hide-s', cellClass: 'mono hide-s', cellTitle: o => o.packageFrom ? t('table.packageFrom', { s: o.packageFrom }) : undefined, cell: o => o.package },
    { id: 'stock', field: 'stock', label: t('table.stock'), num: true, cellClass: 'mono', cell: o => o.stock?.toLocaleString(locale()) ?? '?' },
    { id: 'price', field: 'price', label: t('table.price', { n: c.qty }), num: true, cellClass: 'mono price', cellTitle: o => unitTitle(o, c.qty, price(o)), cell: o => { const p = price(o); return p ? `${money(p.unit)} ${p.currency}` : '—' } },
    { id: 'moq', field: 'moq', label: t('table.moq'), num: true, className: 'hide-s', cellClass: 'mono hide-s', cell: o => o.minOrderQty ?? '?' },
    { id: 'status', field: 'status', label: t('table.status'), className: 'hide-m', cellClass: 'hide-m', cell: o => <LifeBadge life={lifecycleOf(o.status)} title={o.status} /> },
  ], c.columns)
  return (
    <table className="grid flat">
      <thead><tr>
        <Heads cols={cols} sort={c.sort} onSort={c.onSort} order={c.columns} onOrder={c.onColumns} />
        <th className="acts" />
      </tr></thead>
      <tbody>{sortBy(offers, c.sort, value).map(o => (
        <tr key={o.id}>
          <Cells cols={cols} row={o} />
          <td className="num acts"><OfferActions o={o} onAdd={c.onAdd} onRemove={c.onRemove} /></td>
        </tr>
      ))}</tbody>
    </table>
  )
}
