import { useEffect, useState } from 'react'
import { copyText } from '../api'
import { lifecycleLabel, Lifecycle, money, safeHref } from '../lib/parts'
import { t } from '../i18n'

const CHIP = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
    <rect x="7" y="5" width="10" height="14" rx="1" /><path d="M7 8H4M7 12H4M7 16H4M17 8h3M17 12h3M17 16h3" />
  </svg>
)

export function Thumb({ url, large, alt, onOpen }: { url: string; large: string; alt: string; onOpen: (u: string, a: string) => void }) {
  const [failed, setFailed] = useState(false)
  useEffect(() => setFailed(false), [url])
  if (!url || failed) return <div className="thumb">{CHIP}</div>
  return (
    <button className="thumb" title={t('enlarge')} onClick={e => { e.stopPropagation(); onOpen(large || url, alt) }}>
      <img src={url} alt={alt} loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />
    </button>
  )
}

export const LifeBadge = ({ life, title }: { life: Lifecycle; title?: string }) => <span className={'badge ' + life} title={title}>{lifecycleLabel(life)}</span>

export const ExtLink = ({ href, children, className, title }: { href?: string | null; children: React.ReactNode; className?: string; title?: string }) => {
  const safe = safeHref(href)
  if (!safe) return <span className={className}>{children}</span>
  return <a className={className} href={safe} title={title} target="_blank" rel="noopener noreferrer" onClick={e => e.stopPropagation()}>{children}</a>
}

export function CopyButton({ text, label }: { text: string; label: string }) {
  const [done, setDone] = useState(false)
  if (!text) return null
  return (
    <button className="mini" title={label} onClick={async e => {
      e.stopPropagation()
      await copyText(text)
      setDone(true)
      setTimeout(() => setDone(false), 1200)
    }}>{done ? '✓' : '⧉'}</button>
  )
}

export const Money = ({ v, cur }: { v: number; cur?: string }) => <>{money(v)}{cur ? ' ' + cur : ''}</>

export function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: React.ReactNode; wide?: boolean }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onClose() }}>
      <div className={'modal' + (wide ? ' wide' : '')} role="dialog" aria-modal="true" aria-label={title}>
        <header className="modal-head"><h2>{title}</h2><button className="btn" aria-label={t('close')} onClick={onClose}>✕</button></header>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  )
}
