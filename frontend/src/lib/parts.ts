import type { Advanced, Bom, BomLine, Part, Rates } from '../types'
import { locale, t } from '../i18n'

export type Lifecycle = 'active' | 'nrnd' | 'obsolete' | 'unknown'
export const lifecycleLabel = (l: Lifecycle) => t(`life.${l}`)

/** Offre affichée : la pièce du fournisseur, avec son numéro d'arrivée et ce qu'elle emprunte à ses voisines. */
export interface Offer extends Part {
  id: string
  seq: number
  packageFrom?: string
  imageFrom?: string
}

export interface PartGroup {
  key: string
  mpn: string
  manufacturer: string
  description: string
  package: string
  imageUrl: string
  largeImageUrl: string
  lifecycle: Lifecycle
  stock: number
  offers: Offer[]
  best: OfferPrice | null
}

export interface OfferPrice { offer: Offer; unit: number; total: number; currency: string }

export const mpnKey = (s: string) => s.replace(/\s+/g, '').toUpperCase()

const norm = (s: string) => s.toLowerCase().replace(/[_-]+/g, ' ').trim()

/** Regroupe les mots de statut de chaque fournisseur. Le texte d'origine reste affiché au survol. */
export function lifecycleOf(status: string): Lifecycle {
  const s = norm(status)
  if (!s) return 'unknown'
  if (['obsolete', 'discontinued', 'no longer', 'end of life', 'eol'].some(w => s.includes(w))) return 'obsolete'
  if (['nrnd', 'not recommended', 'not for new', 'last time'].some(w => s.includes(w))) return 'nrnd'
  if (['active', 'stocked', 'available', 'new product', 'production'].some(w => s.includes(w))) return 'active'
  return 'unknown'
}

const ALIASES: Record<string, string> = {
  ti: 'texasinstruments', stmicro: 'stmicroelectronics', st: 'stmicroelectronics', onsemiconductor: 'onsemi',
  adi: 'analogdevices', nxpsemiconductors: 'nxp', infineontechnologies: 'infineon', vishaydale: 'vishay',
}

export function makerKey(name: string): string {
  const k = name.toLowerCase().replace(/[^a-z0-9]+/g, ' ').replace(/\b(inc|incorporated|corp|corporation|ltd|limited|llc|gmbh|co)\b/g, '').replace(/\s+/g, '')
  return ALIASES[k] ?? k
}

/** Prix unitaire pour une quantité : le palier le plus haut atteint, sinon le premier. */
export function priceAt(part: Pick<Part, 'priceBreaks' | 'unitPrice'>, qty: number): number | null {
  if (part.priceBreaks.length === 0) return part.unitPrice ?? null
  const ordered = [...part.priceBreaks].sort((a, b) => a.quantity - b.quantity)
  let best = ordered[0]
  for (const b of ordered) if (b.quantity <= qty) best = b
  return best.price
}

/** Taux de conversion, ou null s'il manque un taux. Une devise vide reste sans conversion possible. */
export function rateOf(from: string, to: string, rates: Rates | null): number | null {
  if (!from || !to) return null
  if (from.toUpperCase() === to.toUpperCase()) return 1
  const f = rates?.perEuro[from.toUpperCase()]
  const t = rates?.perEuro[to.toUpperCase()]
  return f && t ? t / f : null
}

/** Prix d'une offre pour une quantité, dans la devise demandée ('' = celle du fournisseur). */
export function offerPrice(o: Offer, qty: number, currency: string, rates: Rates | null): OfferPrice | null {
  const p = priceAt(o, qty)
  if (p == null) return null
  if (!currency) return { offer: o, unit: p, total: p * qty, currency: o.currency }
  const r = rateOf(o.currency, currency, rates)
  if (r == null) return { offer: o, unit: p, total: p * qty, currency: o.currency }
  return { offer: o, unit: p * r, total: p * r * qty, currency }
}

/** Peut-on acheter cette quantité ici et maintenant ? */
export const buyable = (o: Part, qty: number) => (o.minOrderQty ?? 1) <= qty && (o.stock ?? 0) >= qty

/**
 * Offre la moins chère parmi celles qu'on peut acheter maintenant. On ne compare que ce qui est dans la même devise :
 * sans conversion possible, une offre en USD et une en CAD ne se départagent pas, seule la devise demandée compte.
 */
export function bestOffer(offers: Offer[], qty: number, currency: string, rates: Rates | null): OfferPrice | null {
  const candidates = offers.filter(o => buyable(o, qty)).map(o => offerPrice(o, qty, currency, rates)).filter((x): x is OfferPrice => x != null)
  if (candidates.length === 0) return null
  const target = currency || candidates[0].currency
  const same = candidates.filter(c => c.currency === target)
  return (same.length ? same : candidates).sort((a, b) => a.unit - b.unit)[0]
}

export interface Filters {
  inStock: boolean
  lifecycles: Set<Lifecycle>
  types: Set<string>
  packages: Set<string>
  makers: Set<string>
  text: string
  hiddenSuppliers: Set<string>
  removed: Set<string>
}

export const emptyFilters = (): Filters => ({
  inStock: false, lifecycles: new Set(), types: new Set(), packages: new Set(), makers: new Set(), text: '',
  hiddenSuppliers: new Set(), removed: new Set(),
})

/** Filtres de la recherche avancée (dans l'app d'origine : AdvancedSearchOptions.Matches). */
export function matchesAdvanced(o: Offer, a: Advanced, currency: string, rates: Rates | null, qty: number): boolean {
  const has = (hay: string, needle: string) => hay.toLowerCase().includes(needle.toLowerCase())
  if (a.manufacturerContains.trim() && !has(o.manufacturer, a.manufacturerContains.trim())) return false
  if (a.partNoContains.trim() && !has(o.manufacturerPartNumber.replace(/ /g, ''), a.partNoContains.replace(/ /g, ''))) return false
  if (!a.containsIsQuery) {
    for (const w of a.mustContain.split(/[ ,]+/).filter(Boolean)) {
      if (!has(o.description, w) && !has(o.manufacturerPartNumber, w)) return false
    }
  }
  if (a.maxUnitPrice != null) {
    const p = offerPrice(o, qty, currency, rates)
    if (!p || p.unit > a.maxUnitPrice) return false // sans prix affiché, on ne peut pas dire qu'il est sous le plafond
  }
  return true
}

export function offerVisible(o: Offer, f: Filters): boolean {
  if (f.removed.has(o.id) || f.hiddenSuppliers.has(o.supplier)) return false
  if (f.inStock && !((o.stock ?? 0) > 0)) return false
  if (f.lifecycles.size && !f.lifecycles.has(lifecycleOf(o.status))) return false
  if (f.types.size && !f.types.has(o.category.trim())) return false
  if (f.packages.size && !f.packages.has(o.package.trim())) return false
  if (f.makers.size && !f.makers.has(o.manufacturer.trim())) return false
  const t = f.text.trim().toLowerCase()
  if (t && ![o.manufacturerPartNumber, o.description, o.category, o.package, o.manufacturer, o.supplier, o.supplierPartNumber].some(v => v.toLowerCase().includes(t))) return false
  return true
}

/** Photo et boîtier manquants : empruntés à une autre offre de même MPN (Mouser n'en donne pas). */
export function withBorrowed(offers: Offer[]): Offer[] {
  const images = new Map<string, Offer>()
  const packages = new Map<string, Offer>()
  for (const o of offers) {
    const k = mpnKey(o.manufacturerPartNumber)
    if (!k) continue
    if (o.imageUrl && !images.has(k)) images.set(k, o)
    if (o.package && !packages.has(k)) packages.set(k, o)
  }
  return offers.map(o => {
    const k = mpnKey(o.manufacturerPartNumber)
    const img = !o.imageUrl ? images.get(k) : undefined
    const pkg = !o.package ? packages.get(k) : undefined
    if (!img && !pkg) return o
    return {
      ...o,
      imageUrl: img?.imageUrl ?? o.imageUrl,
      largeImageUrl: img ? (img.largeImageUrl || img.imageUrl) : o.largeImageUrl,
      imageFrom: img?.supplier,
      package: pkg?.package ?? o.package,
      packageFrom: pkg?.supplier,
    }
  })
}

const LIFE_RANK: Record<Lifecycle, number> = { obsolete: 3, nrnd: 2, active: 1, unknown: 0 }

/**
 * Une ligne par pièce. Le fabricant fait partie de la clé : plusieurs fabricants vendent un « LM358 ».
 * En cas de doute on laisse deux lignes : une fusion fausse ferait comparer des pièces différentes.
 */
export function groupOffers(offers: Offer[], qty: number, currency: string, rates: Rates | null): PartGroup[] {
  const groups = new Map<string, Offer[]>()
  for (const o of offers) {
    const m = mpnKey(o.manufacturerPartNumber)
    const key = m ? `${m}|${makerKey(o.manufacturer)}` : `${o.supplier}|${o.supplierPartNumber}`
    const list = groups.get(key)
    if (list) list.push(o)
    else groups.set(key, [o])
  }
  return [...groups].map(([key, list]) => {
    const first = list[0]
    const withImage = list.find(o => o.imageUrl)
    // Le statut le plus défavorable l'emporte : mieux vaut signaler une fin de vie à tort que la cacher.
    const lifecycle = list.map(o => lifecycleOf(o.status)).reduce((a, b) => (LIFE_RANK[b] > LIFE_RANK[a] ? b : a), 'unknown' as Lifecycle)
    return {
      key,
      mpn: first.manufacturerPartNumber || first.supplierPartNumber,
      manufacturer: first.manufacturer,
      description: list.find(o => o.description)?.description ?? '',
      package: list.find(o => o.package)?.package ?? '',
      imageUrl: withImage?.imageUrl ?? '',
      largeImageUrl: withImage?.largeImageUrl || withImage?.imageUrl || '',
      lifecycle,
      stock: list.reduce((n, o) => n + (o.stock ?? 0), 0),
      offers: list,
      best: bestOffer(list, qty, currency, rates),
    }
  })
}

export interface SortKey { field: string; desc: boolean }

/** Tri à plusieurs niveaux ; les valeurs absentes vont toujours à la fin. */
export function sortBy<T>(rows: T[], keys: SortKey[], value: (row: T, field: string) => string | number | null | undefined): T[] {
  if (keys.length === 0) return rows
  return [...rows].sort((a, b) => {
    for (const k of keys) {
      const x = value(a, k.field)
      const y = value(b, k.field)
      const xNull = x == null || x === ''
      const yNull = y == null || y === ''
      if (xNull || yNull) {
        if (xNull && yNull) continue
        return xNull ? 1 : -1
      }
      const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), locale(), { numeric: true, sensitivity: 'base' })
      if (c !== 0) return k.desc ? -c : c
    }
    return 0
  })
}

export function money(v: number): string {
  const digits = Math.abs(v) < 10 ? 3 : 2
  return v.toLocaleString(locale(), { minimumFractionDigits: digits, maximumFractionDigits: Math.abs(v) < 1 ? 4 : digits })
}

/** Montant total : toujours deux décimales, contrairement à un prix unitaire. */
export const amount = (v: number) => v.toLocaleString(locale(), { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** N'ouvre que des liens http(s), même si le serveur en laissait passer un autre. */
export function safeHref(url: string | undefined | null): string | undefined {
  return url && /^https?:\/\//i.test(url) ? url : undefined
}

const CSV_RISK = /^[=@\t\r]|^[+-](?!\d+([.,]\d+)?$)/
const csvCell = (v: string | number | null | undefined) => {
  const s = v == null ? '' : String(v)
  return `"${(CSV_RISK.test(s) ? "'" + s : s).replace(/"/g, '""')}"`
}

export function offersToCsv(offers: Offer[], qty: number): string {
  const head = [t('csv.supplier'), t('csv.spn'), 'MPN', t('csv.maker'), t('csv.type'), t('csv.package'), t('csv.description'), 'Stock', t('csv.price', { n: qty }), t('csv.currency'), t('csv.breaks'), 'MOQ', t('csv.availability'), t('csv.status'), t('csv.link'), 'Datasheet']
  const rows = offers.map(o => [o.supplier, o.supplierPartNumber, o.manufacturerPartNumber, o.manufacturer, o.category, o.package, o.description, o.stock, priceAt(o, qty), o.currency,
    [...o.priceBreaks].sort((a, b) => a.quantity - b.quantity).map(b => `${b.quantity}:${b.price}`).join(';'), o.minOrderQty, o.stockInfo, o.status, o.productUrl, o.datasheetUrl])
  return '﻿' + [head, ...rows].map(r => r.map(csvCell).join(',')).join('\r\n') + '\r\n'
}

// ---- BOM ----

export const samePart = (a: BomLine, b: BomLine) =>
  a.supplierPartNumber.length > 0 && a.supplier.toLowerCase() === b.supplier.toLowerCase() && a.supplierPartNumber.toLowerCase() === b.supplierPartNumber.toLowerCase()

export function offerToLine(o: Part, qty: number): BomLine {
  return {
    include: true, refs: '', footprint: o.package, qty: Math.max(qty, o.minOrderQty ?? 1), supplier: o.supplier,
    supplierPartNumber: o.supplierPartNumber, manufacturerPartNumber: o.manufacturerPartNumber, manufacturer: o.manufacturer,
    description: o.description, unitPrice: o.unitPrice, currency: o.currency, priceBreaks: o.priceBreaks, productUrl: o.productUrl,
    stock: o.stock, dateAdded: new Date().toISOString().slice(0, 10),
  }
}

/** Même pièce déjà présente : la quantité augmente. Sinon la ligne rejoint celles de son fournisseur. */
export function addLine(bom: Bom, line: BomLine): Bom {
  const lines = [...bom.lines]
  const i = lines.findIndex(l => samePart(l, line))
  if (i >= 0) {
    lines[i] = { ...lines[i], qty: lines[i].qty + line.qty }
    return { ...bom, lines }
  }
  let last = -1
  lines.forEach((l, n) => { if (l.supplier.toLowerCase() === line.supplier.toLowerCase()) last = n })
  lines.splice(last >= 0 ? last + 1 : lines.length, 0, line)
  return { ...bom, lines }
}

export interface BomTotals {
  bySupplier: { supplier: string; amounts: Record<string, number> }[]
  grand: Record<string, number>
  noPrice: number
  converted: boolean
}

export function bomTotals(bom: Bom, currency: string, rates: Rates | null): BomTotals {
  const bySupplier: BomTotals['bySupplier'] = []
  const grand: Record<string, number> = {}
  let noPrice = 0
  let converted = false
  for (const l of bom.lines.filter(x => x.include)) {
    const p = priceAt(l, l.qty)
    if (p == null) { noPrice++; continue }
    const r = currency ? rateOf(l.currency, currency, rates) : null
    const cur = (r != null ? currency : l.currency).toUpperCase()
    if (r != null && r !== 1) converted = true
    const amount = p * (r ?? 1) * l.qty
    const name = l.supplier || t('bom.noSupplier')
    let e = bySupplier.find(s => s.supplier.toLowerCase() === name.toLowerCase())
    if (!e) { e = { supplier: name, amounts: {} }; bySupplier.push(e) }
    e.amounts[cur] = (e.amounts[cur] ?? 0) + amount
    grand[cur] = (grand[cur] ?? 0) + amount
  }
  return { bySupplier, grand, noPrice, converted }
}

/** Un BOM sans nom : le serveur stocke un libellé par défaut, à afficher dans la langue de l'interface. */
export const bomDisplayName = (name: string) => (name === 'BOM sans titre' || name === 'Untitled BOM' ? t('bom.untitled') : name)

export function bomToText(bom: Bom, currency: string, rates: Rates | null): string {
  const out: string[] = [t('bom.txtHead') + (bom.name || t('bom.untitled'))]
  const suppliers = [...new Set(bom.lines.map(l => l.supplier || t('bom.noSupplier')))]
  for (const s of suppliers) {
    out.push('', s)
    for (const l of bom.lines.filter(x => (x.supplier || t('bom.noSupplier')) === s)) {
      const p = priceAt(l, l.qty)
      const r = currency ? rateOf(l.currency, currency, rates) : null
      const cur = r != null ? currency : l.currency
      const price = p == null ? t('bom.noPrice') : `${money(p * (r ?? 1))} ${cur} × ${l.qty} = ${amount(p * (r ?? 1) * l.qty)} ${cur}`
      out.push(`  ${l.supplierPartNumber || '-'}  ${l.manufacturerPartNumber || '-'}  ${price}${l.include ? '' : '  ' + t('bom.txtNotCounted')}${l.productUrl ? '  ' + l.productUrl : ''}`)
    }
  }
  const totals = bomTotals(bom, currency, rates)
  out.push('', t('bom.txtTotal') + (Object.entries(totals.grand).map(([c, v]) => `${amount(v)} ${c}`).join(' + ') || '0') + (totals.noPrice ? t('bom.txtNoPrice', { n: totals.noPrice }) : ''))
  if (bom.notes.trim()) out.push('', t('bom.txtNotes') + bom.notes.trim())
  return out.join('\n') + '\n'
}
