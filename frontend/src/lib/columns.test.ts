import { describe, expect, it } from 'vitest'
import { mergeOrder, moveColumn, sameOrder, stepColumn } from './columns'

const D = ['a', 'b', 'c', 'd']

describe('ordre des colonnes', () => {
  it('sans ordre enregistré : ordre par défaut', () => {
    expect(mergeOrder(null, D)).toEqual(D)
    expect(mergeOrder([], D)).toEqual(D)
  })

  it('retire les colonnes inconnues et les doublons', () => {
    expect(mergeOrder(['d', 'x', 'c', 'd', 'b', 'a'], D)).toEqual(['d', 'c', 'b', 'a'])
  })

  it('ajoute une nouvelle colonne après sa voisine par défaut', () => {
    expect(mergeOrder(['c', 'a', 'd'], D)).toEqual(['c', 'a', 'b', 'd'])
    expect(mergeOrder(['d', 'c', 'b'], D)).toEqual(['a', 'd', 'c', 'b'])
  })

  it('glisser vers la droite ou vers la gauche', () => {
    expect(moveColumn(D, 'a', 'c')).toEqual(['b', 'c', 'a', 'd'])
    expect(moveColumn(D, 'd', 'b')).toEqual(['a', 'd', 'b', 'c'])
    expect(moveColumn(D, 'a', 'a')).toEqual(D)
    expect(moveColumn(D, 'x', 'a')).toEqual(D)
  })

  it('au clavier, une place à la fois, sans sortir du tableau', () => {
    expect(stepColumn(D, 'b', -1)).toEqual(['b', 'a', 'c', 'd'])
    expect(stepColumn(D, 'b', 1)).toEqual(['a', 'c', 'b', 'd'])
    expect(stepColumn(D, 'a', -1)).toEqual(D)
    expect(stepColumn(D, 'd', 1)).toEqual(D)
  })

  it('compare deux ordres', () => {
    expect(sameOrder(D, [...D])).toBe(true)
    expect(sameOrder(D, ['b', 'a', 'c', 'd'])).toBe(false)
  })
})
