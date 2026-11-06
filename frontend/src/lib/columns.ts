// Ordre des colonnes des tableaux de résultats, réglable par glisser-déposer
// (comme les grilles de Win-PartsFinder) et gardé dans le navigateur.

/** Ordre enregistré, nettoyé : colonnes inconnues retirées, colonnes nouvelles ajoutées à leur place par défaut. */
export function mergeOrder(saved: readonly string[] | null | undefined, defaults: readonly string[]): string[] {
  if (!saved?.length) return [...defaults]
  const known = new Set(defaults)
  const out = [...new Set(saved.filter(id => known.has(id)))]
  defaults.forEach((id, i) => {
    if (out.includes(id)) return
    // Insérée juste après sa voisine de gauche par défaut, sinon en tête.
    const left = defaults.slice(0, i).reverse().find(d => out.includes(d))
    out.splice(left ? out.indexOf(left) + 1 : 0, 0, id)
  })
  return out
}

/** Déplace `id` à la place de `target` (avant elle si on va vers la gauche, après sinon). */
export function moveColumn(order: readonly string[], id: string, target: string): string[] {
  const from = order.indexOf(id)
  const to = order.indexOf(target)
  if (from < 0 || to < 0 || from === to) return [...order]
  const out = [...order]
  out.splice(from, 1)
  out.splice(to, 0, id)
  return out
}

/** Déplace `id` d'une place (`-1` à gauche, `1` à droite), pour le clavier. */
export function stepColumn(order: readonly string[], id: string, step: -1 | 1): string[] {
  const i = order.indexOf(id)
  const j = i + step
  if (i < 0 || j < 0 || j >= order.length) return [...order]
  return moveColumn(order, id, order[j])
}

export const sameOrder = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((id, i) => id === b[i])

export function loadOrder(key: string, defaults: readonly string[]): string[] {
  try {
    const raw = JSON.parse(localStorage.getItem(key) ?? 'null')
    return mergeOrder(Array.isArray(raw) ? raw.filter((x): x is string => typeof x === 'string') : null, defaults)
  } catch {
    return [...defaults]
  }
}

export function saveOrder(key: string, order: readonly string[], defaults: readonly string[]) {
  if (sameOrder(order, defaults)) localStorage.removeItem(key)
  else localStorage.setItem(key, JSON.stringify(order))
}
