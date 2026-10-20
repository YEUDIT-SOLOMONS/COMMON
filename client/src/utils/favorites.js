import { useEffect, useSyncExternalStore } from 'react'
import { favoritesApi } from '../api/endpoints.js'

// מצב המועדפים של המשתמשת המחוברת, משותף לכל המסכים: { [itemId]: { isFavorite, notifyWhenAvailable } }
let state = { loadedFor: null, map: {} }
const listeners = new Set()

function emit(next) {
  state = next
  listeners.forEach((listener) => listener())
}

function subscribe(listener) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export async function loadFavorites(userId) {
  if (!userId) {
    emit({ loadedFor: null, map: {} })
    return []
  }
  const list = await favoritesApi.mine()
  emit({
    loadedFor: userId,
    map: Object.fromEntries(list.map((f) => [f.itemId, { isFavorite: f.isFavorite, notifyWhenAvailable: f.notifyWhenAvailable }])),
  })
  return list
}

export async function updateFavorite(itemId, dto) {
  const updated = await favoritesApi.update(itemId, dto)
  const map = { ...state.map }
  if (updated.isFavorite || updated.notifyWhenAvailable) {
    map[itemId] = { isFavorite: updated.isFavorite, notifyWhenAvailable: updated.notifyWhenAvailable }
  } else {
    delete map[itemId]
  }
  emit({ ...state, map })
  return updated
}

export function useFavorites(userId) {
  const snapshot = useSyncExternalStore(subscribe, () => state)
  useEffect(() => {
    if (userId && snapshot.loadedFor !== userId) loadFavorites(userId).catch(() => {})
  }, [userId, snapshot.loadedFor])
  return snapshot.map
}
