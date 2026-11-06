import { useCallback, useRef, useState } from 'react'
import { api, ApiError, streamSearch } from './api'
import { Offer } from './lib/parts'
import type { Advanced, Mode, SearchEvent, SupplierPage, SupplierState } from './types'
import { serverMessage, t } from './i18n'

export interface ActiveSearch { query: string; mode: Mode; advanced: Advanced }

/** État d'une recherche : les fournisseurs, et les offres au fur et à mesure qu'elles arrivent. */
export function useSearch() {
  const [offers, setOffers] = useState<Offer[]>([])
  const [suppliers, setSuppliers] = useState<SupplierState[]>([])
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState('')
  const [elapsedMs, setElapsedMs] = useState<number | null>(null)
  const [active, setActive] = useState<ActiveSearch | null>(null)
  const abort = useRef<AbortController | null>(null)
  const seen = useRef(new Set<string>())
  const seq = useRef(0)

  /** Ajoute une page sans doublons (les pages se recoupent parfois) ; renvoie le nombre de nouvelles lignes. */
  const addPage = useCallback((page: SupplierPage) => {
    const fresh: Offer[] = []
    for (const p of page.parts) {
      // Le fabricant est dans la clé : Mouser liste des LM358 de plusieurs fabricants sans numéro Mouser.
      const key = `${p.supplier}|${p.supplierPartNumber}|${p.manufacturerPartNumber}|${p.manufacturer}`
      if (seen.current.has(key)) continue
      seen.current.add(key)
      fresh.push({ ...p, id: key, seq: ++seq.current })
    }
    if (fresh.length) setOffers(all => [...all, ...fresh])
    return fresh.length
  }, [])

  const patch = useCallback((name: string, change: (s: SupplierState) => SupplierState) => {
    setSuppliers(list => list.map(s => (s.name === name ? change(s) : s)))
  }, [])

  const onEvent = useCallback((e: SearchEvent) => {
    switch (e.type) {
      case 'started':
        setSuppliers((e.suppliers ?? []).map(name => ({ name, status: 'waiting', hits: 0 })))
        break
      case 'page': {
        const page = e.page!
        const added = addPage(page)
        patch(page.supplier, s => {
          const hits = s.hits + added
          return { ...s, status: hits ? 'ok' : 'empty', hits, total: page.total, elapsedMs: page.elapsedMs, cached: page.cached, fetchedUtc: page.fetchedUtc, hasMore: page.hasMore, nextOffset: page.nextOffset, query: page.query, mode: page.mode, message: undefined }
        })
        break
      }
      case 'link':
        patch(e.supplier!, s => ({ ...s, status: 'link', url: e.url }))
        break
      case 'error':
        patch(e.supplier!, s => ({ ...s, status: 'error', message: serverMessage(e.messageKey, e.message), url: e.url ?? s.url }))
        break
      case 'codeResolved':
        setNote(e.mpn ? t('search.codeFound', { code: e.code ?? '', mpn: e.mpn, supplier: e.supplier ?? '' }) : t('search.codeNotFound', { code: e.code ?? '' }))
        break
      case 'done':
        setElapsedMs(e.elapsedMs ?? null)
        break
    }
  }, [addPage, patch])

  const run = useCallback(async (search: ActiveSearch, opts: { suppliers?: string[]; refresh?: boolean } = {}) => {
    abort.current?.abort()
    const controller = new AbortController()
    abort.current = controller
    seen.current = new Set()
    seq.current = 0
    setOffers([])
    setSuppliers([])
    setNote('')
    setElapsedMs(null)
    setActive(search)
    setBusy(true)
    try {
      await streamSearch({ ...search, ...opts }, onEvent, controller.signal)
    } catch (e) {
      if (controller.signal.aborted) {
        setSuppliers(list => list.map(s => (s.status === 'waiting' ? { ...s, status: 'cancelled' } : s)))
      } else if (!(e instanceof ApiError && e.status === 401)) {
        setNote(t('search.failed', { m: (e as Error).message }))
      }
    } finally {
      if (abort.current === controller) setBusy(false)
    }
  }, [onEvent])

  const cancel = useCallback(() => abort.current?.abort(), [])

  const loadMore = useCallback(async () => {
    if (!active) return
    const targets = suppliers.filter(s => s.hasMore)
    setBusy(true)
    await Promise.all(targets.map(async s => {
      try {
        const e = await api.more({
          supplier: s.name, query: s.query ?? active.query, mode: s.mode ?? active.mode, offset: s.nextOffset ?? 0,
          pageSize: active.advanced.maxResults, inStockOnly: active.advanced.inStockOnly, rohsOnly: active.advanced.rohsOnly,
        })
        if (e.page) {
          const added = addPage(e.page)
          // Une page sans rien de nouveau : on s'arrête là, même si le fournisseur annonce davantage.
          patch(s.name, x => ({ ...x, hits: x.hits + added, total: e.page!.total, hasMore: e.page!.hasMore && added > 0, nextOffset: e.page!.nextOffset, status: x.hits + added ? 'ok' : 'empty' }))
        } else {
          patch(s.name, x => ({ ...x, status: 'error', message: serverMessage(e.messageKey, e.message), hasMore: false }))
        }
      } catch (err) {
        patch(s.name, x => ({ ...x, status: 'error', message: (err as Error).message }))
      }
    }))
    setBusy(false)
  }, [active, suppliers, addPage, patch])

  return { offers, suppliers, busy, note, elapsedMs, active, run, cancel, loadMore }
}
