import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api, download, setUnauthorizedHandler } from './api'
import { AdvancedDialog, defaultAdvanced } from './components/Advanced'
import { BomDrawer } from './components/BomDrawer'
import { ExtLink, Modal } from './components/common'
import { Login } from './components/Login'
import { GroupedTable, OFFER_COLUMNS, OffersTable, PART_COLUMNS } from './components/Results'
import { loadOrder, saveOrder, sameOrder } from './lib/columns'
import { SettingsDialog } from './components/SettingsDialog'
import { addLine, emptyFilters, Filters, groupOffers, lifecycleLabel, Lifecycle, lifecycleOf, matchesAdvanced, Offer, offersToCsv, offerToLine, offerVisible, SortKey, withBorrowed } from './lib/parts'
import type { Advanced, AuthUser, Bom, BomSummary, Meta, Mode, Rates, SupplierInfo, SupplierState } from './types'
import { ActiveSearch, useSearch } from './useSearch'
import { changeLang, getLang, otherLang, t } from './i18n'

const MODES: { mode: Mode; label: string }[] = [
  { mode: 'Keyword', label: t('mode.keyword') }, { mode: 'PartNumber', label: t('mode.partNumber') },
  { mode: 'ExactPartNumber', label: t('mode.exact') }, { mode: 'SupplierCode', label: t('mode.code') },
]

export default function App() {
  const [auth, setAuth] = useState<Awaited<ReturnType<typeof api.authStatus>> | null>(null)
  const [error, setError] = useState('')
  const refresh = useCallback(() => { api.authStatus().then(setAuth).catch(e => setError(e.message)) }, [])
  useEffect(() => { refresh(); setUnauthorizedHandler(refresh) }, [refresh])
  // La langue appartient au compte, sur tous ses appareils. Un compte sans choix garde celle affichée : le navigateur ne décide plus.
  const signedIn = auth?.user != null
  const accountLang = auth?.user?.lang
  useEffect(() => {
    if (!signedIn) return
    if (accountLang === 'fr' || accountLang === 'en') { if (accountLang !== getLang()) changeLang(accountLang) }
    else api.setLanguage(getLang()).catch(() => {})
  }, [signedIn, accountLang])
  if (!auth) return <main className="login"><p className={error ? 'error' : 'sub'}>{error || t('loading')}</p></main>
  if (!auth.authenticated || !auth.user) return <Login setup={auth.setupRequired} tokenRequired={auth.setupTokenRequired} onDone={refresh} />
  return <Main user={auth.user} onLogout={() => api.logout().finally(refresh)} />
}

function urlState() {
  const p = new URLSearchParams(location.search)
  const mode = p.get('mode') as Mode | null
  return { q: p.get('q') ?? '', mode: mode && MODES.some(m => m.mode === mode) ? mode : 'Keyword' as Mode, qty: Math.max(1, Number(p.get('qty')) || 1) }
}

function SupplierChip({ s, hidden, onToggle }: { s: SupplierState; hidden: boolean; onToggle: () => void }) {
  if (s.status === 'link') return <ExtLink className="chip link" href={s.url} title={t('chip.openSite')}><i />{s.name} <small>{t('chip.link')}</small></ExtLink>
  const detail = {
    waiting: '…', cancelled: t('chip.cancelled'), error: t('chip.error'), empty: '0',
    ok: `${s.hits}${s.total != null && s.hasMore ? ' / ' + s.total : ''}`,
  }[s.status]
  const time = s.status === 'ok' || s.status === 'empty' ? (s.cached ? ` · ${t('chip.cached')}` : s.elapsedMs != null ? ` · ${(s.elapsedMs / 1000).toFixed(1)} s` : '') : ''
  const title = s.status === 'error' ? `${s.name} : ${s.message}` : hidden ? t('chip.show') : t('chip.hide')
  return (
    <span className="chip-wrap">
      <button className={`chip ${s.status === 'waiting' ? 'wait' : s.status === 'error' ? 'err' : s.status === 'cancelled' ? '' : 'ok'}${hidden ? ' off' : ''}`} title={title} aria-pressed={!hidden} onClick={onToggle}>
        <i />{s.name} <small>{detail}{time}</small>
      </button>
      {s.status === 'error' && s.url && <ExtLink className="chip-link" href={s.url} title={t('chip.searchSite')}>↗</ExtLink>}
    </span>
  )
}

function Facet({ title, values, selected, label, onToggle }: { title: string; values: Map<string, number>; selected: Set<string>; label?: (v: string) => string; onToggle: (v: string, on: boolean) => void }) {
  if (values.size === 0) return null
  return (
    <>
      <h3>{title}</h3>
      <div className="facet-list">
        {[...values].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map(([v, n]) => (
          <label key={v} title={v}><input type="checkbox" checked={selected.has(v)} onChange={e => onToggle(v, e.target.checked)} /><span>{label ? label(v) : v}</span><em>{n}</em></label>
        ))}
      </div>
    </>
  )
}

function Main({ user, onLogout }: { user: AuthUser; onLogout: () => void }) {
  const initial = useRef(urlState()).current
  const [query, setQuery] = useState(initial.q)
  const [mode, setMode] = useState<Mode>(initial.mode)
  const [qty, setQty] = useState(initial.qty)
  const [qtyText, setQtyText] = useState(String(initial.qty))
  const [meta, setMeta] = useState<Meta | null>(null)
  const [rates, setRates] = useState<Rates | null>(null)
  const [currency, setCurrency] = useState(localStorage.getItem('ps.currency') ?? '')
  const [supplierInfo, setSupplierInfo] = useState<SupplierInfo[]>([])
  const [history, setHistory] = useState<string[]>([])
  const [filters, setFilters] = useState<Filters>(emptyFilters)
  const [view, setView] = useState<'parts' | 'offers'>(localStorage.getItem('ps.view') === 'offers' ? 'offers' : 'parts')
  const [sort, setSort] = useState<SortKey[]>([])
  const [partCols, setPartCols] = useState(() => loadOrder('ps.cols.parts', PART_COLUMNS))
  const [offerCols, setOfferCols] = useState(() => loadOrder('ps.cols.offers', OFFER_COLUMNS))
  const [theme, setTheme] = useState(localStorage.getItem('ps.theme') ?? (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'))
  const [image, setImage] = useState<{ url: string; alt: string } | null>(null)
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const [showFacets, setShowFacets] = useState(false)
  const [advanced, setAdvanced] = useState<Advanced>(defaultAdvanced)
  const [chosenSuppliers, setChosenSuppliers] = useState<string[]>([])
  const [bomOpen, setBomOpen] = useState(false)
  const [bomList, setBomList] = useState<BomSummary[]>([])
  const [bomId, setBomId] = useState<number | null>(null)
  const [bom, setBom] = useState<Bom | null>(null)
  const [bomError, setBomError] = useState('')
  const saveTimer = useRef<number>(0)
  const input = useRef<HTMLInputElement>(null)
  const search = useSearch()

  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem('ps.theme', theme) }, [theme])
  useEffect(() => { localStorage.setItem('ps.view', view) }, [view])
  useEffect(() => { saveOrder('ps.cols.parts', partCols, PART_COLUMNS) }, [partCols])
  useEffect(() => { saveOrder('ps.cols.offers', offerCols, OFFER_COLUMNS) }, [offerCols])

  const loadSuppliers = useCallback(() => api.suppliers().then(setSupplierInfo).catch(() => {}), [])
  const loadHistory = useCallback(() => api.history().then(setHistory).catch(() => {}), [])

  const loadBom = useCallback(async (id: number) => {
    setBom(await api.bom(id))
    setBomId(id)
    localStorage.setItem('ps.bom', String(id))
  }, [])
  const refreshBomList = useCallback(() => api.boms().then(l => { setBomList(l); return l }), [])

  useEffect(() => {
    api.meta().then(setMeta).catch(() => {})
    api.rates().then(setRates).catch(() => {})
    api.settings().then(s => setCurrency(c => c || s.settings.convertCurrency)).catch(() => {})
    loadSuppliers()
    loadHistory()
    refreshBomList().then(l => {
      const last = Number(localStorage.getItem('ps.bom'))
      const pick = l.find(b => b.id === last) ?? l[0]
      if (pick) loadBom(pick.id).catch(() => {})
    }).catch(() => {})
  }, [loadSuppliers, loadHistory, loadBom, refreshBomList])

  const start = useCallback((s: ActiveSearch, opts: { suppliers?: string[]; refresh?: boolean; push?: boolean } = {}) => {
    setFilters(f => ({ ...f, removed: new Set(), types: new Set(), packages: new Set(), makers: new Set(), hiddenSuppliers: new Set() }))
    const url = new URLSearchParams({ q: s.query })
    if (s.mode !== 'Keyword') url.set('mode', s.mode)
    if (qty > 1) url.set('qty', String(qty))
    if (opts.push !== false) window.history.pushState(null, '', '?' + url.toString())
    search.run(s, { suppliers: opts.suppliers, refresh: opts.refresh }).finally(() => { loadSuppliers(); loadHistory() })
  }, [qty, search.run, loadSuppliers, loadHistory]) // eslint-disable-line

  const submit = () => {
    const q = query.trim()
    if (!q) { input.current?.focus(); return }
    // Une recherche simple repart sans les critères avancés, sauf les fournisseurs choisis.
    const a = defaultAdvanced()
    a.maxResults = advanced.maxResults
    setAdvanced(a)
    start({ query: q, mode, advanced: a }, { suppliers: chosenSuppliers.length ? chosenSuppliers : undefined })
  }

  // Recherche présente dans l'URL à l'ouverture, et boutons précédent et suivant du navigateur.
  useEffect(() => {
    if (initial.q) start({ query: initial.q, mode: initial.mode, advanced: defaultAdvanced() }, { push: false })
    const onPop = () => {
      const u = urlState()
      setQuery(u.q)
      setMode(u.mode)
      setQty(u.qty)
      setQtyText(String(u.qty))
      if (u.q) search.run({ query: u.q, mode: u.mode, advanced: defaultAdvanced() })
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, []) // eslint-disable-line

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); input.current?.select() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const changeQty = (text: string) => {
    setQtyText(text)
    const n = Math.floor(Number(text))
    if (n >= 1) {
      setQty(n)
      const url = new URLSearchParams(location.search)
      if (n > 1) url.set('qty', String(n))
      else url.delete('qty')
      window.history.replaceState(null, '', '?' + url.toString())
    }
  }

  // Les offres retenues : emprunts de photo et de boîtier, puis critères de la recherche avancée.
  const prepared = useMemo(() => {
    const borrowed = withBorrowed(search.offers)
    const a = search.active?.advanced
    return a ? borrowed.filter(o => matchesAdvanced(o, a, currency, rates, qty)) : borrowed
  }, [search.offers, search.active, currency, rates, qty])
  const leftOut = search.offers.length - prepared.length

  const facets = useMemo(() => {
    const life = new Map<string, number>(), types = new Map<string, number>(), pkgs = new Map<string, number>(), makers = new Map<string, number>()
    const inc = (m: Map<string, number>, k: string) => { const v = k.trim(); if (v) m.set(v, (m.get(v) ?? 0) + 1) }
    for (const o of prepared) {
      if (filters.removed.has(o.id) || filters.hiddenSuppliers.has(o.supplier)) continue
      inc(life, lifecycleOf(o.status)); inc(types, o.category); inc(pkgs, o.package); inc(makers, o.manufacturer)
    }
    return { life, types, pkgs, makers }
  }, [prepared, filters.removed, filters.hiddenSuppliers])

  const visible = useMemo(() => prepared.filter(o => offerVisible(o, filters)), [prepared, filters])
  const groups = useMemo(() => groupOffers(visible, qty, currency, rates), [visible, qty, currency, rates])

  const toggleIn = <T,>(s: Set<T>, v: T, on: boolean) => { const n = new Set(s); if (on) n.add(v); else n.delete(v); return n }
  const onSort = useCallback((field: string, add: boolean) => {
    setSort(cur => {
      const i = cur.findIndex(k => k.field === field)
      if (add) {
        if (i < 0) return [...cur, { field, desc: false }]
        return cur.map((k, n) => (n === i ? { ...k, desc: !k.desc } : k))
      }
      if (i === 0 && cur.length === 1) return cur[0].desc ? [] : [{ field, desc: true }]
      return [{ field, desc: false }]
    })
  }, [])

  // ---- BOM : enregistrement automatique, une demi-seconde après la dernière modification ----
  const changeBom = useCallback((b: Bom, id: number | null = bomId) => {
    setBom(b)
    if (id == null) return
    window.clearTimeout(saveTimer.current)
    saveTimer.current = window.setTimeout(() => {
      api.saveBom(id, b).then(() => { setBomError(''); refreshBomList() }).catch(e => setBomError(t('bom.notSaved', { m: e.message })))
    }, 500)
  }, [bomId, refreshBomList])

  const createBom = async (initialBom: Bom = { name: '', notes: '', lines: [] }) => {
    window.clearTimeout(saveTimer.current)
    if (bomId != null && bom) await api.saveBom(bomId, bom).catch(() => {})
    const { id } = await api.createBom(initialBom)
    setBomId(id)
    setBom(initialBom)
    localStorage.setItem('ps.bom', String(id))
    await refreshBomList()
  }
  const addToBom = async (o: Offer) => {
    const line = offerToLine(o, qty)
    try {
      if (bom && bomId != null) changeBom(addLine(bom, line))
      else await createBom(addLine({ name: '', notes: '', lines: [] }, line))
      setBomError('')
      setBomOpen(true)
    } catch (e) {
      setBomError(t('bom.notSaved', { m: (e as Error).message }))
    }
  }
  const selectBom = async (id: number) => {
    window.clearTimeout(saveTimer.current)
    if (bomId != null && bom) await api.saveBom(bomId, bom).catch(() => {})
    await loadBom(id)
  }
  const deleteBom = async () => {
    if (bomId == null) return
    window.clearTimeout(saveTimer.current)
    await api.deleteBom(bomId)
    const l = await refreshBomList()
    if (l[0]) await loadBom(l[0].id)
    else { setBom(null); setBomId(null); localStorage.removeItem('ps.bom') }
  }

  const common = { qty, currency, rates, sort, onSort, onAdd: addToBom, onRemove: (o: Offer) => setFilters(f => ({ ...f, removed: toggleIn(f.removed, o.id, true) })), onImage: (url: string, alt: string) => setImage({ url, alt }) }
  const found = search.offers.length
  const filtered = filters.inStock || filters.lifecycles.size + filters.types.size + filters.packages.size + filters.makers.size > 0 || filters.text.trim().length > 0
  const moreCount = search.suppliers.filter(s => s.hasMore).length
  const allCached = search.suppliers.some(s => s.cached)
  const oldest = search.suppliers.filter(s => s.cached && s.fetchedUtc).map(s => new Date(s.fetchedUtc!).getTime()).sort()[0]
  const noKeys = supplierInfo.length > 0 && !supplierInfo.some(s => s.configured)

  return (
    <div className="app">
      {meta?.demo && <div className="notice"><strong>{t('top.demo')}</strong> {t('top.demoDetail')}</div>}
      <header className="top">
        <div className="logo">Part<b>Scout</b>{meta && <small className="version" title={t('top.version')}>v{meta.version}</small>}</div>
        <form className="search" role="search" onSubmit={e => { e.preventDefault(); submit() }}>
          <span aria-hidden="true">⌕</span>
          <input ref={input} value={query} onChange={e => setQuery(e.target.value)} list="history" placeholder={t('top.placeholder')} aria-label={t('top.searchLabel')} autoFocus={!initial.q} />
          <datalist id="history">{history.map(h => <option key={h} value={h} />)}</datalist>
          <kbd>Ctrl K</kbd>
        </form>
        <div className="seg" role="group" aria-label={t('top.modeLabel')}>
          {MODES.map(m => <button key={m.mode} aria-pressed={mode === m.mode} onClick={() => setMode(m.mode)}>{m.label}</button>)}
        </div>
        <label className="field">{t('top.qty')} <input type="number" min={1} value={qtyText} onChange={e => changeQty(e.target.value)} onBlur={() => setQtyText(String(qty))} /></label>
        <label className="field">{t('top.currency')}
          <select value={currency} onChange={e => { setCurrency(e.target.value); localStorage.setItem('ps.currency', e.target.value) }}>
            <option value="">{t('top.currencyOriginal')}</option>
            {(meta?.convertCurrencies ?? ['CAD']).map(c => <option key={c}>{c}</option>)}
          </select>
        </label>
        {search.busy
          ? <button className="btn" onClick={search.cancel}>{t('cancel')}</button>
          : <button className="btn primary" onClick={submit}>{t('search')}</button>}
        <button className="btn" onClick={() => setShowAdvanced(true)}>{t('top.advanced')}</button>
        <button className="btn" onClick={() => setBomOpen(o => !o)} aria-expanded={bomOpen}>BOM<span className="count">{bom?.lines.length ?? 0}</span></button>
        <button className="btn" onClick={() => setShowSettings(true)} aria-label={t('top.settings')} title={t('top.settings')}>⚙</button>
        <button className="btn lang-switch" title={t('lang.switch')} aria-label={t('lang.switch')} onClick={() => { const l = otherLang(); api.setLanguage(l).catch(() => {}).finally(() => changeLang(l)) }}>{otherLang().toUpperCase()}</button>
        <button className="btn" onClick={() => setTheme(t => (t === 'dark' ? 'light' : 'dark'))} aria-label={t('top.theme')} title={t('top.theme')}>◐</button>
      </header>

      {search.suppliers.length > 0 && (
        <div className="strip" aria-live="polite">
          {search.suppliers.map(s => <SupplierChip key={s.name} s={s} hidden={filters.hiddenSuppliers.has(s.name)} onToggle={() => setFilters(f => ({ ...f, hiddenSuppliers: toggleIn(f.hiddenSuppliers, s.name, !f.hiddenSuppliers.has(s.name)) }))} />)}
        </div>
      )}

      <main className={'content' + (bomOpen ? ' with-bom' : '')}>
        <aside className={'facets' + (showFacets ? ' show' : '')} aria-label={t('filter.label')}>
          <button className="btn only-s close-facets" onClick={() => setShowFacets(false)}>{t('close')}</button>
          <h3>{t('filter.title')}</h3>
          <input className="filter" placeholder={t('filter.placeholder')} aria-label={t('filter.aria')} value={filters.text} onChange={e => setFilters(f => ({ ...f, text: e.target.value }))} />
          <label className="toggle"><input type="checkbox" checked={filters.inStock} onChange={e => setFilters(f => ({ ...f, inStock: e.target.checked }))} /><span>{t('filter.inStock')}</span></label>
          <Facet title={t('filter.lifecycle')} values={facets.life} selected={filters.lifecycles as Set<string>} label={v => lifecycleLabel(v as Lifecycle)} onToggle={(v, on) => setFilters(f => ({ ...f, lifecycles: toggleIn(f.lifecycles, v as Lifecycle, on) }))} />
          <Facet title={t('filter.type')} values={facets.types} selected={filters.types} onToggle={(v, on) => setFilters(f => ({ ...f, types: toggleIn(f.types, v, on) }))} />
          <Facet title={t('filter.package')} values={facets.pkgs} selected={filters.packages} onToggle={(v, on) => setFilters(f => ({ ...f, packages: toggleIn(f.packages, v, on) }))} />
          <Facet title={t('filter.maker')} values={facets.makers} selected={filters.makers} onToggle={(v, on) => setFilters(f => ({ ...f, makers: toggleIn(f.makers, v, on) }))} />
        </aside>

        <section className="results">
          <div className="toolbar">
            <button className="btn only-s" onClick={() => setShowFacets(s => !s)} aria-expanded={showFacets}>{t('filter.label')}</button>
            <div className="seg small" role="group" aria-label={t('view.label')}>
              <button aria-pressed={view === 'parts'} onClick={() => { setView('parts'); setSort([]) }}>{t('view.parts')}</button>
              <button aria-pressed={view === 'offers'} onClick={() => { setView('offers'); setSort([]) }}>{t('view.offers')}</button>
            </div>
            <span className="sub">{view === 'parts' ? t('count.parts', { n: groups.length }) : ''}{t('count.offers', { n: visible.length })}{visible.length !== found ? t('count.of', { n: found }) : ''}</span>
            <span className="grow" />
            {(view === 'parts' ? !sameOrder(partCols, PART_COLUMNS) : !sameOrder(offerCols, OFFER_COLUMNS)) && <button className="btn" title={t('toolbar.resetColumnsTitle')} onClick={() => view === 'parts' ? setPartCols([...PART_COLUMNS]) : setOfferCols([...OFFER_COLUMNS])}>{t('toolbar.resetColumns')}</button>}
            {filters.removed.size > 0 && <button className="btn" onClick={() => setFilters(f => ({ ...f, removed: new Set() }))}>{t('toolbar.restore', { n: filters.removed.size })}</button>}
            {allCached && !search.busy && <button className="btn" title={oldest ? t('toolbar.cachedFor', { n: Math.max(0, Math.round((Date.now() - oldest) / 60000)) }) : undefined} onClick={() => search.active && start(search.active, { refresh: true, suppliers: chosenSuppliers.length ? chosenSuppliers : undefined })}>{t('toolbar.refresh')}</button>}
            {moreCount > 0 && <button className="btn" disabled={search.busy} onClick={search.loadMore}>{t('toolbar.more', { n: moreCount })}</button>}
            <button className="btn" disabled={visible.length === 0} onClick={() => download(`${search.active?.query.replace(/[^\w-]+/g, '_') ?? t('toolbar.csvName')}.csv`, offersToCsv(visible, qty), 'text/csv;charset=utf-8')}>{t('toolbar.csv')}</button>
          </div>
          {search.note && <p className="banner" role="status">{search.note}</p>}
          {bomError && <p className="banner bad" role="alert">{bomError}</p>}
          {rates?.stale && <p className="banner" role="status">{rates.ratesDate ? t('banner.ratesStale', { date: rates.ratesDate }) : t('banner.ratesNone')}</p>}
          {leftOut > 0 && <p className="banner" role="status">{t('banner.leftOut', { n: leftOut })}</p>}

          {!search.active && (
            <div className="welcome">
              <h1>{t('welcome.title')}</h1>
              <p className="sub">{t('welcome.hint')}</p>
              {noKeys && <p className="banner">{t('welcome.noKeys')} <button className="linkbtn" onClick={() => setShowSettings(true)}>{t('welcome.addKeys')}</button></p>}
              {history.length > 0 && <div className="recent">{history.slice(0, 8).map(h => <button key={h} className="chip" onClick={() => { setQuery(h); start({ query: h, mode, advanced: defaultAdvanced() }) }}>{h}</button>)}</div>}
            </div>
          )}
          {search.active && !search.busy && found === 0 && !search.suppliers.some(s => s.status === 'error') && (
            <div className="empty">{search.suppliers.some(s => s.status === 'link') ? t('empty.tryLinks') : t('empty.none')}</div>
          )}
          {search.active && !search.busy && found > 0 && visible.length === 0 && (
            <div className="empty">{t(filtered ? 'empty.hiddenFilters' : 'empty.hidden', { n: found })} <button className="linkbtn" onClick={() => setFilters(emptyFilters())}>{t('empty.showAll')}</button></div>
          )}
          {visible.length > 0 && (view === 'parts' ? <GroupedTable groups={groups} {...common} columns={partCols} onColumns={setPartCols} /> : <OffersTable offers={visible} {...common} columns={offerCols} onColumns={setOfferCols} />)}
          {search.elapsedMs != null && <p className="foot sub">{t('foot.done', { s: (search.elapsedMs / 1000).toFixed(1) })}{rates?.ratesDate ? t('foot.rates', { date: rates.ratesDate }) : ''}{t('foot.check')}</p>}
        </section>

        <BomDrawer open={bomOpen} onClose={() => setBomOpen(false)} bom={bom} bomId={bomId} list={bomList} currency={currency} rates={rates}
          onChange={b => changeBom(b)} onSelect={selectBom} onCreate={() => createBom()} onDelete={deleteBom}
          onImported={async id => { await refreshBomList(); await selectBom(id) }} />
      </main>

      {showAdvanced && (
        <AdvancedDialog initial={advanced} query={query} mode={mode} suppliers={supplierInfo} chosen={chosenSuppliers} onClose={() => setShowAdvanced(false)}
          onSearch={(q, m, a, s) => {
            setShowAdvanced(false)
            setQuery(q)
            setMode(m)
            setAdvanced(a)
            setChosenSuppliers(s)
            start({ query: q, mode: m, advanced: a }, { suppliers: s })
          }} />
      )}
      {showSettings && meta && <SettingsDialog meta={meta} user={user} onClose={() => setShowSettings(false)} onLogout={onLogout} onSaved={() => { setChosenSuppliers([]); loadSuppliers(); loadHistory() }} />}
      {image && <Modal title={image.alt} onClose={() => setImage(null)}><img className="large" src={image.url} alt={image.alt} referrerPolicy="no-referrer" /></Modal>}
    </div>
  )
}

