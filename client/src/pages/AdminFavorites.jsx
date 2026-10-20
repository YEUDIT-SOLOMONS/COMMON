import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { formatDate } from '../utils/format.js'

// הפריטים האהובים באתר: כמה שמרו כל פריט במועדפים, וכמה ממתינות שיתפנה
export function AdminFavorites() {
  const [rows, setRows] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    adminApi
      .favorites()
      .then((r) => setRows(r ?? []))
      .catch((err) => setError(err.message))
  }, [])

  if (error) return <p className="state-message state-error">{error}</p>
  if (!rows) return <Loader label="טוען פריטים אהובים" />
  if (rows.length === 0) return <p className="state-message">עוד אף פריט לא נשמר במועדפים.</p>

  return (
    <section>
      <p className="field-hint">מסודר לפי מספר השמירות. "ממתינות" = ביקשו התראה כשהפריט מתפנה.</p>
      <div className="table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>פריט</th>
              <th>משכירה</th>
              <th>♥ שמרו</th>
              <th>🔔 ממתינות</th>
              <th>מי שמרה</th>
              <th>נשמר לאחרונה</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.itemId}>
                <td>
                  <Link to={`/?item=${encodeURIComponent(r.itemId)}`}>{r.itemName}</Link>
                  {r.status === 'Rented' && <span className="admin-badge">מושכר עכשיו</span>}
                </td>
                <td>{r.ownerName || '—'}</td>
                <td>
                  <strong>{r.favorites}</strong>
                </td>
                <td>{r.waiting || '—'}</td>
                <td>
                  {(r.savers ?? []).map((u, i) => (
                    <span key={i} className="favorite-saver">
                      {u.name}
                      {u.waiting && ' 🔔'}
                      {u.email && (
                        <a className="admin-call-note" dir="ltr" href={`mailto:${u.email}`}>
                          {' '}
                          {u.email}
                        </a>
                      )}
                    </span>
                  ))}
                </td>
                <td>{r.lastSavedUtc ? formatDate(r.lastSavedUtc) : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
