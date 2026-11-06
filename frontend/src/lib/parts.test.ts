import { describe, expect, it } from 'vitest'
import { setLang } from '../i18n'
import type { Advanced, Bom, Part, Rates } from '../types'
import { addLine, amount, bestOffer, bomTotals, emptyFilters, groupOffers, lifecycleOf, makerKey, matchesAdvanced, Offer, offersToCsv, offerToLine, offerVisible, priceAt, rateOf, safeHref, sortBy, withBorrowed } from './parts'

setLang('fr') // les attentes de ce fichier sont écrites avec le format français

const rates: Rates = { ratesDate: '2026-11-03', stale: false, perEuro: { EUR: 1, CAD: 1.5, USD: 1.0 } }
let n = 0
const offer = (o: Partial<Part>): Offer => ({
  id: String(++n), seq: n, supplier: 'S', supplierPartNumber: 'P' + n, manufacturerPartNumber: 'LM358', manufacturer: 'TI', description: '', category: '', package: '',
  stock: 100, stockInfo: '', unitPrice: 1, currency: 'CAD', priceBreaks: [], minOrderQty: 1, status: '', productUrl: '', datasheetUrl: '', imageUrl: '', largeImageUrl: '', ...o,
})
const adv: Advanced = { inStockOnly: false, rohsOnly: false, maxResults: 25, manufacturerContains: '', partNoContains: '', mustContain: '', maxUnitPrice: null, containsIsQuery: false }

describe('priceAt', () => {
  const p = { unitPrice: 9, priceBreaks: [{ quantity: 100, price: 0.2 }, { quantity: 1, price: 1 }, { quantity: 10, price: 0.5 }] }
  it('prend le palier le plus haut atteint', () => {
    expect(priceAt(p, 1)).toBe(1)
    expect(priceAt(p, 9)).toBe(1)
    expect(priceAt(p, 10)).toBe(0.5)
    expect(priceAt(p, 5000)).toBe(0.2)
  })
  it('sous le premier palier, prend le premier', () => expect(priceAt({ unitPrice: null, priceBreaks: [{ quantity: 10, price: 2 }] }, 1)).toBe(2))
  it('sans palier, prend le prix unitaire', () => expect(priceAt({ unitPrice: 3, priceBreaks: [] }, 10)).toBe(3))
})

describe('rateOf', () => {
  it('passe par l\'euro', () => expect(rateOf('USD', 'CAD', rates)).toBeCloseTo(1.5))
  it('même devise sans taux', () => expect(rateOf('xyz', 'XYZ', null)).toBe(1))
  it('taux absent', () => expect(rateOf('USD', 'JPY', rates)).toBeNull())
})

describe('bestOffer', () => {
  const cheapUsd = offer({ currency: 'USD', priceBreaks: [{ quantity: 1, price: 0.5 }] }) // 0,75 CAD
  const cad = offer({ currency: 'CAD', priceBreaks: [{ quantity: 1, price: 0.8 }] })
  it('compare après conversion', () => {
    const b = bestOffer([cad, cheapUsd], 10, 'CAD', rates)!
    expect(b.offer).toBe(cheapUsd)
    expect(b.unit).toBeCloseTo(0.75)
    expect(b.total).toBeCloseTo(7.5)
    expect(b.currency).toBe('CAD')
  })
  it('écarte un MOQ trop haut', () => {
    const moq = offer({ minOrderQty: 100, priceBreaks: [{ quantity: 100, price: 0.01 }] })
    expect(bestOffer([moq, cad], 10, 'CAD', rates)!.offer).toBe(cad)
    expect(bestOffer([moq], 10, 'CAD', rates)).toBeNull()
  })
  it('écarte un stock insuffisant ou inconnu', () => {
    expect(bestOffer([offer({ stock: 5 })], 10, 'CAD', rates)).toBeNull()
    expect(bestOffer([offer({ stock: null })], 10, 'CAD', rates)).toBeNull()
  })
  it('ne compare pas des devises différentes sans taux', () => {
    const jpy = offer({ currency: 'JPY', priceBreaks: [{ quantity: 1, price: 0.001 }] })
    expect(bestOffer([jpy, cad], 1, 'CAD', rates)!.offer).toBe(cad)
  })
  it('un palier plus bas au-delà de la quantité ne compte pas', () => {
    const a = offer({ priceBreaks: [{ quantity: 1, price: 1 }, { quantity: 100, price: 0.1 }] })
    const b = offer({ priceBreaks: [{ quantity: 1, price: 0.5 }] })
    expect(bestOffer([a, b], 10, 'CAD', rates)!.offer).toBe(b)
    expect(bestOffer([a, b], 100, 'CAD', rates)!.offer).toBe(a)
  })
})

describe('lifecycleOf', () => {
  it.each([
    ['Active', 'active'], ['STOCKED', 'active'], ['', 'unknown'], ['Obsolete', 'obsolete'], ['NO_LONGER_STOCKED', 'obsolete'], ['NO_LONGER_MANUFACTURED', 'obsolete'],
    ['End of Life', 'obsolete'], ['Not For New Designs', 'nrnd'], ['Last Time Buy', 'nrnd'], ['NRND', 'nrnd'], ['BLOCKED_FOR_ZBL_BACKORDERS', 'unknown'],
  ])('%s → %s', (s, e) => expect(lifecycleOf(s)).toBe(e))
})

describe('groupOffers', () => {
  it('regroupe un même MPN malgré casse, espaces et écriture du fabricant', () => {
    const g = groupOffers([offer({ manufacturerPartNumber: 'LM358 DR', manufacturer: 'TEXAS INSTRUMENTS' }), offer({ manufacturerPartNumber: 'lm358dr', manufacturer: 'Texas Instruments Inc.' }), offer({ manufacturerPartNumber: 'LM358DR', manufacturer: 'TI' })], 1, 'CAD', rates)
    expect(g).toHaveLength(1)
    expect(g[0].offers).toHaveLength(3)
    expect(g[0].stock).toBe(300)
  })
  it('sépare deux fabricants du même MPN', () => {
    expect(groupOffers([offer({ manufacturer: 'onsemi' }), offer({ manufacturer: 'Diodes Inc' })], 1, 'CAD', rates)).toHaveLength(2)
    expect(makerKey('ON Semiconductor')).toBe(makerKey('onsemi'))
  })
  it('ne fusionne pas les suffixes d\'emballage', () => {
    expect(groupOffers([offer({ manufacturerPartNumber: 'LM358AN' }), offer({ manufacturerPartNumber: 'LM358AN/NOPB' })], 1, 'CAD', rates)).toHaveLength(2)
  })
  it('ne fusionne pas deux pièces sans MPN', () => {
    expect(groupOffers([offer({ manufacturerPartNumber: '' }), offer({ manufacturerPartNumber: '' })], 1, 'CAD', rates)).toHaveLength(2)
  })
  it('garde le statut le plus défavorable', () => {
    expect(groupOffers([offer({ status: 'Active' }), offer({ status: 'Obsolete' }), offer({ status: '' })], 1, 'CAD', rates)[0].lifecycle).toBe('obsolete')
  })
})

describe('withBorrowed', () => {
  it('emprunte photo et boîtier à une offre de même MPN', () => {
    const [a] = withBorrowed([offer({ supplier: 'Mouser', manufacturerPartNumber: 'X1' }), offer({ supplier: 'DigiKey', manufacturerPartNumber: 'x1', imageUrl: 'https://i/a.png', package: 'SOIC' })])
    expect(a.imageUrl).toBe('https://i/a.png')
    expect(a.package).toBe('SOIC')
    expect(a.packageFrom).toBe('DigiKey')
  })
})

describe('filtres', () => {
  it('stock, statut, type, texte, fournisseur, lignes retirées', () => {
    const o = offer({ stock: 0, status: 'Obsolete', category: 'Op Amps', supplier: 'Mouser', description: 'Double ampli' })
    expect(offerVisible(o, emptyFilters())).toBe(true)
    expect(offerVisible(o, { ...emptyFilters(), inStock: true })).toBe(false)
    expect(offerVisible(o, { ...emptyFilters(), lifecycles: new Set(['active']) })).toBe(false)
    expect(offerVisible(o, { ...emptyFilters(), lifecycles: new Set(['obsolete']) })).toBe(true)
    expect(offerVisible(o, { ...emptyFilters(), types: new Set(['Autre']) })).toBe(false)
    expect(offerVisible(o, { ...emptyFilters(), text: 'AMPLI' })).toBe(true)
    expect(offerVisible(o, { ...emptyFilters(), text: 'absent' })).toBe(false)
    expect(offerVisible(o, { ...emptyFilters(), hiddenSuppliers: new Set(['Mouser']) })).toBe(false)
    expect(offerVisible(o, { ...emptyFilters(), removed: new Set([o.id]) })).toBe(false)
  })
  it('recherche avancée', () => {
    const o = offer({ manufacturerPartNumber: 'KSC931G', manufacturer: 'Omron', description: 'Switch tactile', priceBreaks: [{ quantity: 1, price: 2 }] })
    expect(matchesAdvanced(o, { ...adv, partNoContains: '931 g' }, 'CAD', rates, 1)).toBe(true)
    expect(matchesAdvanced(o, { ...adv, manufacturerContains: 'alps' }, 'CAD', rates, 1)).toBe(false)
    expect(matchesAdvanced(o, { ...adv, mustContain: 'switch, absent' }, 'CAD', rates, 1)).toBe(false)
    expect(matchesAdvanced(o, { ...adv, mustContain: 'absent', containsIsQuery: true }, 'CAD', rates, 1)).toBe(true)
    expect(matchesAdvanced(o, { ...adv, maxUnitPrice: 1 }, 'CAD', rates, 1)).toBe(false)
    expect(matchesAdvanced(o, { ...adv, maxUnitPrice: 2 }, 'CAD', rates, 1)).toBe(true)
    expect(matchesAdvanced(offer({ unitPrice: null }), { ...adv, maxUnitPrice: 5 }, 'CAD', rates, 1)).toBe(false)
  })
})

describe('sortBy', () => {
  const rows = [{ a: 2, b: 'x' }, { a: null, b: 'y' }, { a: 1, b: 'y' }, { a: 2, b: 'a' }]
  const get = (r: typeof rows[0], f: string) => (f === 'a' ? r.a : r.b)
  it('met les valeurs absentes à la fin, même en décroissant', () => {
    expect(sortBy(rows, [{ field: 'a', desc: true }], get).map(r => r.a)).toEqual([2, 2, 1, null])
  })
  it('trie sur plusieurs niveaux', () => {
    expect(sortBy(rows, [{ field: 'a', desc: false }, { field: 'b', desc: false }], get).map(r => r.b)).toEqual(['y', 'a', 'x', 'y'])
  })
})

describe('formats', () => {
  it('un total a toujours deux décimales', () => {
    expect(amount(8.6)).toBe('8,60')
    expect(amount(1234.5).replace(/\s/g, ' ')).toBe('1 234,50')
  })
})

describe('liens et CSV', () => {
  it('refuse les liens non http', () => {
    expect(safeHref('javascript:alert(1)')).toBeUndefined()
    expect(safeHref('https://a.test')).toBe('https://a.test')
  })
  it('neutralise les formules et échappe les guillemets', () => {
    const csv = offersToCsv([offer({ description: '=1+1', manufacturer: 'A "B"' })], 1)
    expect(csv).toContain(`"'=1+1"`)
    expect(csv).toContain('"A ""B"""')
    expect(csv.startsWith('﻿')).toBe(true)
  })
})

describe('BOM', () => {
  const base: Bom = { name: '', notes: '', lines: [] }
  it('respecte le MOQ à l\'ajout et cumule une pièce déjà présente', () => {
    const o = offer({ supplierPartNumber: 'A', minOrderQty: 5 })
    let b = addLine(base, offerToLine(o, 1))
    expect(b.lines[0].qty).toBe(5)
    b = addLine(b, offerToLine(o, 10))
    expect(b.lines).toHaveLength(1)
    expect(b.lines[0].qty).toBe(15)
  })
  it('garde les lignes d\'un fournisseur ensemble', () => {
    let b = base
    for (const [s, p] of [['X', '1'], ['Y', '2'], ['X', '3']]) b = addLine(b, offerToLine(offer({ supplier: s, supplierPartNumber: p }), 1))
    expect(b.lines.map(l => l.supplierPartNumber)).toEqual(['1', '3', '2'])
  })
  it('totalise, ignore le DNP, compte les lignes sans prix et applique le palier', () => {
    const lines = [
      offerToLine(offer({ supplier: 'X', supplierPartNumber: '1', currency: 'USD', priceBreaks: [{ quantity: 1, price: 1 }, { quantity: 10, price: 0.5 }] }), 10),
      { ...offerToLine(offer({ supplier: 'X', supplierPartNumber: '2', priceBreaks: [{ quantity: 1, price: 100 }] }), 1), include: false },
      offerToLine(offer({ supplier: 'Y', supplierPartNumber: '3', unitPrice: null }), 1),
    ]
    const t = bomTotals({ ...base, lines }, 'CAD', rates)
    expect(t.grand.CAD).toBeCloseTo(7.5)
    expect(t.noPrice).toBe(1)
    expect(t.converted).toBe(true)
  })
  it('sépare les devises sans taux', () => {
    const lines = [offerToLine(offer({ supplierPartNumber: '1', currency: 'JPY', unitPrice: 100 }), 1), offerToLine(offer({ supplierPartNumber: '2', currency: 'CAD', unitPrice: 1 }), 1)]
    const t = bomTotals({ ...base, lines }, 'CAD', rates)
    expect(t.grand).toEqual({ JPY: 100, CAD: 1 })
  })
})
