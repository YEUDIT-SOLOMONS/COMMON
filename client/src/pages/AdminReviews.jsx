import { useCallback, useEffect, useState } from 'react'
import { reviewsApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { Stars } from '../components/Stars.jsx'
import { formatDateTime } from '../utils/format.js'

// כל הדירוגים: של שוכרות על פריטים (גלויים באתר - אפשר להסתיר) ושל משכירות על שוכרות (גלויים רק כאן)
export function AdminReviews() {
  const [reviews, setReviews] = useState(null)
  const [filter, setFilter] = useState('Renter')
  const [error, setError] = useState('')

  const reload = useCallback(() => {
    reviewsApi
      .adminList()
      .then((r) => setReviews(r ?? []))
      .catch((err) => setError(err.message))
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  async function toggleHidden(review) {
    setError('')
    try {
      await reviewsApi.setHidden(review.id, !review.hidden)
      reload()
    } catch (err) {
      setError(err.message)
    }
  }

  if (!reviews) return error ? <p className="form-error">{error}</p> : <Loader label="טוען דירוגים" />

  const shown = reviews.filter((r) => r.byRole === filter)

  return (
    <section className="admin-section">
      <div className="admin-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={filter === 'Renter'}
          className={`admin-tab ${filter === 'Renter' ? 'admin-tab-active' : ''}`}
          onClick={() => setFilter('Renter')}
        >
          דירוגי פריטים (גלויים באתר)
        </button>
      </div>

      {error && <p className="form-error">{error}</p>}
      {shown.length === 0 ? (
        <p className="state-message">אין עדיין דירוגים כאן.</p>
      ) : (
        <div className="table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>מתי</th>
                <th>פריט</th>
                <th>מדרגת</th>
                <th>{filter === 'Renter' ? 'הצלמת המשכירה' : 'השוכרת'}</th>
                <th>דירוג</th>
                <th>טקסט</th>
                {filter === 'Renter' && <th>באתר</th>}
              </tr>
            </thead>
            <tbody>
              {shown.map((r) => (
                <tr key={r.id} className={r.hidden ? 'is-muted' : ''}>
                  <td>{formatDateTime(r.createdAtUtc)}</td>
                  <td>{r.itemName}</td>
                  <td>{r.authorName}</td>
                  <td>{r.subjectName}</td>
                  <td>
                    <Stars value={r.stars} />
                  </td>
                  <td>
                    {r.text || '—'}
                    {r.thankYouNote && <div className="admin-thanks-note">מילה טובה: {r.thankYouNote}</div>}
                  </td>
                  {filter === 'Renter' && (
                    <td>
                      <button type="button" className="btn btn-ghost btn-small" onClick={() => toggleHidden(r)}>
                        {r.hidden ? 'הצגה באתר' : 'הסתרה'}
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
