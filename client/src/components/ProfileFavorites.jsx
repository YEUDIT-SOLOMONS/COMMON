import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { loadFavorites, updateFavorite } from '../utils/favorites.js'
import { SectionTitle } from './SectionTitle.jsx'

// "14.10 ב-18:00" - קצר, כדי שיישאר בשורה אחת
function freesAt(busyUntil) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(busyUntil ?? '')
  return match ? `${Number(match[3])}.${Number(match[2])} ב-${match[4]}:${match[5]}` : null
}

function FavoriteCard({ favorite, action }) {
  const { item } = favorite
  const available = item.status !== 'Rented'
  return (
    <li className="fav-card">
      <Link to={`/?item=${encodeURIComponent(favorite.itemId)}`} className="fav-card-link">
        <span className="fav-card-media">
          {item.imageDataUrl ? <img src={item.imageDataUrl} alt="" loading="lazy" decoding="async" /> : <span className="fav-card-placeholder">📷</span>}
        </span>
        <span className="fav-card-body">
          <strong className="fav-card-name">{item.name}</strong>
          <span className="fav-card-meta">
            ₪{item.dailyPrice} ליום{item.addVat ? ' כולל מע״מ' : ''}{item.city ? ` · ${item.city}` : ''}
          </span>
          <span className={`fav-card-status ${available ? 'is-free' : 'is-busy'}`}>
            {available
              ? 'פנוי עכשיו'
              : freesAt(item.busyUntil)
                ? `מתפנה ${freesAt(item.busyUntil)}`
                : 'מושכר כרגע'}
          </span>
        </span>
      </Link>
      {action}
    </li>
  )
}

// באזור האישי: "המועדפים שלי" (ימין) ו"ממתינה לפריט" (שמאל, רק כשיש)
export function ProfileFavorites({ userId }) {
  const [list, setList] = useState(null)
  const [error, setError] = useState('')

  const reload = useCallback(() => {
    loadFavorites(userId)
      .then((r) => setList(r.filter((f) => f.item)))
      .catch((err) => setError(err.message))
  }, [userId])

  useEffect(() => {
    reload()
  }, [reload])

  async function remove(itemId, field) {
    setError('')
    try {
      await updateFavorite(itemId, { [field]: false })
      reload()
    } catch (err) {
      setError(err.message)
    }
  }

  if (!list) return null
  const favorites = list.filter((f) => f.isFavorite)
  const waiting = list.filter((f) => f.notifyWhenAvailable)
  if (favorites.length === 0 && waiting.length === 0) return null

  return (
    <div className="favorites-columns">
      {favorites.length > 0 && (
      <section className="profile-section">
        <div className="profile-section-head">
          <SectionTitle storageKey="favorites">המועדפים שלי</SectionTitle>
        </div>
        {error && <p className="form-error">{error}</p>}
        {favorites.length === 0 ? (
          <p className="state-message">
            עוד לא שמרת פריטים. ראית משהו שאהבת? לחצי על הלב בכרטיס הפריט, והוא יחכה לך כאן.
          </p>
        ) : (
          <ul className="fav-grid">
            {favorites.map((f) => (
              <FavoriteCard
                key={f.itemId}
                favorite={f}
                action={
                  <button
                    type="button"
                    className="favorite-heart is-on fav-card-heart"
                    onClick={() => remove(f.itemId, 'isFavorite')}
                    aria-label={`הסרת ${f.item.name} מהמועדפים`}
                  >
                    ♥
                  </button>
                }
              />
            ))}
          </ul>
        )}
      </section>
      )}

      {waiting.length > 0 && (
        <section className="profile-section">
          <div className="profile-section-head">
            <SectionTitle storageKey="waiting">ממתינה לפריט</SectionTitle>
          </div>
          <p className="field-hint">🔔 ברגע שהפריט יתפנה נשלח לך מייל עם קישור ישיר אליו.</p>
          <ul className="fav-grid">
            {waiting.map((f) => (
              <FavoriteCard
                key={f.itemId}
                favorite={f}
                action={
                  <button
                    type="button"
                    className="link-button fav-card-cancel"
                    onClick={() => remove(f.itemId, 'notifyWhenAvailable')}
                  >
                    ביטול ההתראה
                  </button>
                }
              />
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
