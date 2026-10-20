import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext.jsx'
import { updateFavorite, useFavorites } from '../utils/favorites.js'

// לב לשמירה במועדפים - על כרטיס הפריט בקטלוג
export function FavoriteHeart({ item }) {
  const { user, isAuthenticated } = useAuth()
  const navigate = useNavigate()
  const favorites = useFavorites(user?.id)
  const saved = Boolean(favorites[item.id]?.isFavorite)
  const [busy, setBusy] = useState(false)

  if (user && item.ownerId === user.id) return null

  async function toggle(e) {
    e.stopPropagation()
    if (!isAuthenticated) {
      navigate('/login')
      return
    }
    setBusy(true)
    try {
      await updateFavorite(item.id, { isFavorite: !saved })
    } catch {
      // בלי הודעה על הכרטיס - אפשר לנסות שוב
    } finally {
      setBusy(false)
    }
  }

  return (
    <button
      type="button"
      className={`favorite-heart ${saved ? 'is-on' : ''}`}
      onClick={toggle}
      disabled={busy}
      aria-pressed={saved}
      aria-label={saved ? `הסרת ${item.name} מהמועדפים` : `שמירת ${item.name} במועדפים`}
    >
      {saved ? '♥' : '♡'}
    </button>
  )
}
