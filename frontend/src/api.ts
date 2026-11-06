import type { Advanced, AuthUser, Bom, BomSummary, Meta, Mode, Rates, SearchEvent, Settings, SupplierInfo } from './types'
import { serverMessage, t } from './i18n'

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

let onUnauthorized = () => {}
export const setUnauthorizedHandler = (fn: () => void) => { onUnauthorized = fn }

async function request(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<Response> {
  const res = await fetch('/api' + path, {
    method,
    signal,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (res.ok) return res
  if (res.status === 401 && !path.startsWith('/auth/')) onUnauthorized()
  let message = res.status === 429 ? t('err.tooMany') : t('err.status', { n: res.status })
  try {
    const b = await res.clone().json()
    message = serverMessage(b.messageKey, b.message, b) || message
  } catch { /* corps non JSON */ }
  throw new ApiError(res.status, message)
}

async function json<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await request(method, path, body)
  const text = await res.text() // certaines réponses réussies n'ont pas de corps
  return (text ? JSON.parse(text) : undefined) as T
}

export interface ManagedUser { id: number; name: string; isAdmin: boolean; lang: string; createdUtc: string }

export const api = {
  authStatus: () => json<{ setupRequired: boolean; setupTokenRequired: boolean; authenticated: boolean; demo: boolean; user: AuthUser | null }>('GET', '/auth/status'),
  setup: (name: string, password: string, token: string) => json<void>('POST', '/auth/setup', { name, password, token }),
  login: (name: string, password: string) => json<void>('POST', '/auth/login', { name, password }),
  logout: () => json<void>('POST', '/auth/logout'),
  setLanguage: (lang: string) => json<void>('PUT', '/me/language', { lang }),
  changePassword: (current: string, password: string) => json<void>('PUT', '/me/password', { current, password }),
  users: () => json<ManagedUser[]>('GET', '/admin/users'),
  createUser: (name: string, password: string, isAdmin: boolean, lang: string) => json<{ id: number }>('POST', '/admin/users', { name, password, isAdmin, lang }),
  resetPassword: (id: number, password: string) => json<void>('PUT', `/admin/users/${id}/password`, { password }),
  deleteUser: (id: number) => json<void>('DELETE', `/admin/users/${id}`),
  meta: () => json<Meta>('GET', '/meta'),
  rates: () => json<Rates>('GET', '/rates'),
  history: () => json<string[]>('GET', '/history'),
  clearHistory: () => json<void>('DELETE', '/history'),
  suppliers: () => json<SupplierInfo[]>('GET', '/suppliers'),
  testSupplier: (name: string) => json<{ ok: boolean; message: string; messageKey?: string; count?: string }>('POST', `/suppliers/${encodeURIComponent(name)}/test`)
    .then(r => ({ ok: r.ok, message: serverMessage(r.messageKey, r.message, { count: r.count ?? '' }) })),
  settings: () => json<{ settings: Settings; secretsSet: Record<string, boolean>; unreadableSecrets: string[]; searchUrls: Record<string, string> }>('GET', '/settings'),
  saveSettings: (s: Settings) => json<void>('PUT', '/settings', s),
  saveSecrets: (v: Record<string, string>) => json<void>('PUT', '/settings/secrets', v),
  more: (b: { supplier: string; query: string; mode: Mode; offset: number; pageSize: number; inStockOnly: boolean; rohsOnly: boolean }) => json<SearchEvent>('POST', '/search/more', b),
  boms: () => json<BomSummary[]>('GET', '/boms'),
  bom: (id: number) => json<Bom>('GET', `/boms/${id}`),
  createBom: (b: Bom) => json<{ id: number }>('POST', '/boms', b),
  saveBom: (id: number, b: Bom) => json<void>('PUT', `/boms/${id}`, b),
  deleteBom: (id: number) => json<void>('DELETE', `/boms/${id}`),
  importBom: (csv: string, name: string) => json<{ id: number; lines: number }>('POST', '/boms/import', { csv, name }),
}

export interface SearchParams { query: string; mode: Mode; advanced: Advanced; suppliers?: string[]; refresh?: boolean }

/** Lit le flux de recherche ligne par ligne. Annuler le signal arrête aussi les appels côté serveur. */
export async function streamSearch(p: SearchParams, onEvent: (e: SearchEvent) => void, signal: AbortSignal): Promise<void> {
  const res = await request('POST', '/search', {
    query: p.query, mode: p.mode, maxResults: p.advanced.maxResults, inStockOnly: p.advanced.inStockOnly,
    rohsOnly: p.advanced.rohsOnly, suppliers: p.suppliers, refresh: p.refresh ?? false,
  }, signal)
  const reader = res.body!.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += value
    let i: number
    while ((i = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, i).trim()
      buffer = buffer.slice(i + 1)
      if (line) onEvent(JSON.parse(line))
    }
  }
}

export function download(name: string, content: string, type: string) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(new Blob([content], { type }))
  a.download = name
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 5000)
}

/** navigator.clipboard n'existe qu'en HTTPS ; sur le LAN en HTTP on passe par l'ancienne méthode. */
export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard) { await navigator.clipboard.writeText(text); return }
  const t = document.createElement('textarea')
  t.value = text
  t.style.position = 'fixed'
  t.style.opacity = '0'
  document.body.appendChild(t)
  t.select()
  document.execCommand('copy')
  t.remove()
}
