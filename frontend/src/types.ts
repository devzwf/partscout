export type Mode = 'Keyword' | 'PartNumber' | 'ExactPartNumber' | 'SupplierCode'

export interface PriceBreak { quantity: number; price: number }

export interface Part {
  supplier: string
  supplierPartNumber: string
  manufacturerPartNumber: string
  manufacturer: string
  description: string
  category: string
  package: string
  stock?: number | null
  stockInfo: string
  unitPrice?: number | null
  currency: string
  priceBreaks: PriceBreak[]
  minOrderQty?: number | null
  status: string
  productUrl: string
  datasheetUrl: string
  imageUrl: string
  largeImageUrl: string
}

export interface SupplierPage {
  supplier: string
  query: string
  mode: Mode
  parts: Part[]
  total?: number | null
  nextOffset: number
  hasMore: boolean
  elapsedMs: number
  cached: boolean
  fetchedUtc: string
}

export interface AuthUser { id: number; name: string; isAdmin: boolean; lang: string }

export interface SearchEvent {
  type: 'started' | 'page' | 'link' | 'error' | 'codeResolved' | 'done'
  suppliers?: string[]
  supplier?: string
  page?: SupplierPage
  url?: string
  message?: string
  messageKey?: string
  code?: string
  mpn?: string
  elapsedMs?: number
}

export interface SupplierState {
  name: string
  status: 'waiting' | 'ok' | 'empty' | 'error' | 'link' | 'cancelled'
  hits: number
  total?: number | null
  elapsedMs?: number
  cached?: boolean
  fetchedUtc?: string
  message?: string
  url?: string
  hasMore?: boolean
  nextOffset?: number
  query?: string
  mode?: Mode
}

export interface Rates {
  ratesDate: string
  perEuro: Record<string, number>
  stale: boolean
  error?: string
}

export interface BomLine {
  include: boolean
  refs: string
  footprint: string
  qty: number
  supplier: string
  supplierPartNumber: string
  manufacturerPartNumber: string
  manufacturer: string
  description: string
  unitPrice?: number | null
  currency: string
  priceBreaks: PriceBreak[]
  productUrl: string
  stock?: number | null
  dateAdded?: string | null
}

export interface Bom { name: string; notes: string; lines: BomLine[] }
export interface BomSummary { id: number; name: string; updatedUtc: string }

export interface Advanced {
  inStockOnly: boolean
  rohsOnly: boolean
  maxResults: number
  manufacturerContains: string
  partNoContains: string
  mustContain: string
  maxUnitPrice: number | null
  /** La recherche elle-même est faite de ces mots : ne pas les revérifier lettre à lettre. */
  containsIsQuery: boolean
}

export interface CustomSupplier { name: string; searchUrl: string }

export interface Settings {
  digiKeySite: string
  digiKeyLanguage: string
  digiKeyCurrency: string
  farnellStore: string
  tmeCountry: string
  tmeCurrency: string
  convertCurrency: string
  customSuppliers: CustomSupplier[]
  enabledSuppliers: Record<string, boolean>
  searchUrls: Record<string, string>
}

export interface SupplierInfo {
  name: string
  kind: 'api' | 'link'
  configured: boolean
  enabled: boolean
  callsToday: number
}

export interface Meta {
  demo: boolean
  version: string
  digiKeySites: string[]
  digiKeyCurrencies: string[]
  farnellStores: { id: string; country: string; currency: string }[]
  tmeCountries: string[]
  tmeCurrencies: string[]
  convertCurrencies: string[]
}
