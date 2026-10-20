import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { salesApi } from '../api/endpoints.js'
import { formatDateTime } from '../utils/format.js'
import { SectionTitle } from './SectionTitle.jsx'

const STATUS = { Paid: 'ממתין לקבלה', Received: 'התקבל', Problem: 'דווחה בעיה' }

// הקניות והמכירות ביד 2 - בתוך "לוח יד 2" באזור האישי, מוסתר כשאין
export function ProfileSales({ embedded = false }) {
  const [list, setList] = useState(null)

  useEffect(() => {
    salesApi
      .mine()
      .then((r) => setList(r ?? []))
      .catch(() => setList([]))
  }, [])

  if (!list || list.length === 0) return null

  const Wrap = embedded ? 'div' : 'section'
  return (
    <Wrap className={embedded ? 'profile-subsection' : 'profile-section'}>
      {embedded ? (
        <h3 className="profile-subtitle">הקניות והמכירות שלי</h3>
      ) : (
        <div className="profile-section-head">
          <SectionTitle storageKey="sales">קניות ומכירות יד 2</SectionTitle>
        </div>
      )}
      <ul className="question-list">
        {list.map((s) => (
          <li key={s.id}>
            <Link to={`/purchase/${s.id}?t=${s.token}`} className="question-list-row">
              {s.image ? <img src={s.image} alt="" loading="lazy" /> : <span className="question-list-ph">🛍️</span>}
              <span className="question-list-body">
                <strong>{s.itemName}</strong>
                <span className="admin-call-note">
                  {s.role === 'Buyer' ? 'קנית' : 'מכרת'} · ₪{s.price} · {formatDateTime(s.createdAtUtc)}
                </span>
              </span>
              <span className={s.status === 'Paid' && s.role === 'Buyer' ? 'question-list-new' : 'admin-call-note'}>
                {s.status === 'Paid' && s.role === 'Buyer' ? 'לאשר קבלה' : STATUS[s.status] ?? s.status}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </Wrap>
  )
}
