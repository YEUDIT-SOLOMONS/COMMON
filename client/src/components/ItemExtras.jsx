import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { reviewsApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { updateFavorite, useFavorites } from '../utils/favorites.js'
import { Stars } from './Stars.jsx'
import { shareItem } from '../utils/share.js'

// בחלון הפריט: כוכבי הדירוג (בלי פירוט), שמירה למועדפים ו"תודיעו לי כשמתפנה"
export function ItemExtras({ item, isOwnItem }) {
  const { user, isAuthenticated } = useAuth()
  const navigate = useNavigate()
  const favorites = useFavorites(user?.id)
  const current = favorites[item.id] ?? { isFavorite: false, notifyWhenAvailable: false }
  const [reviews, setReviews] = useState(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false
    reviewsApi
      .forItem(item.id)
      .then((r) => {
        if (!cancelled) setReviews(r)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [item.id])

  async function toggle(field) {
    if (!isAuthenticated) {
      navigate('/login')
      return
    }
    setBusy(true)
    setError('')
    try {
      await updateFavorite(item.id, { [field]: !current[field] })
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  const rented = item.status === 'Rented'

  return (
    <div className="item-extras">
      {reviews?.count > 0 && (
        <div className="item-rating">
          <Stars value={reviews.average} />
        </div>
      )}

      <div className="item-extras-actions">
        <button type="button" className="btn btn-small btn-ghost share-toggle" onClick={() => shareItem(item)}>
          ↗ שיתוף
        </button>
        {!isOwnItem && (
          <>
            <button
              type="button"
              className={`btn btn-small btn-ghost favorite-toggle ${current.isFavorite ? 'is-on' : ''}`}
              onClick={() => toggle('isFavorite')}
              disabled={busy}
              aria-pressed={current.isFavorite}
            >
              {current.isFavorite ? '♥ במועדפים' : '♡ שמירה'}
            </button>
            {rented && (
              <button
                type="button"
                className={`btn btn-small btn-ghost notify-toggle ${current.notifyWhenAvailable ? 'is-on' : ''}`}
                onClick={() => toggle('notifyWhenAvailable')}
                disabled={busy}
                aria-pressed={current.notifyWhenAvailable}
              >
                {current.notifyWhenAvailable ? '🔔 נודיע לך כשיתפנה' : '🔔 תודיעו לי כשמתפנה'}
              </button>
            )}
          </>
        )}
      </div>
      {error && <p className="form-error">{error}</p>}
    </div>
  )
}
