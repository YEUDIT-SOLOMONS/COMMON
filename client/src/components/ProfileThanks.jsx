import { useEffect, useState } from 'react'
import { reviewsApi } from '../api/endpoints.js'
import { formatDate } from '../utils/format.js'
import { SectionTitle } from './SectionTitle.jsx'

// "מילים טובות שקיבלת" - מילות תודה שהשאירו שוכרות. כשאין - לא מוצג בכלל
export function ProfileThanks() {
  const [notes, setNotes] = useState([])

  useEffect(() => {
    reviewsApi
      .thanks()
      .then((r) => setNotes((r ?? []).filter((n) => n?.text?.trim())))
      .catch(() => {})
  }, [])

  if (notes.length === 0) return null

  return (
    <section className="profile-section">
      <div className="profile-section-head">
        <SectionTitle storageKey="thanks">מילים טובות שקיבלת</SectionTitle>
      </div>
      <ul className="thanks-list">
        {notes.map((n, i) => (
          <li key={i} className="thanks-note">
            <p className="thanks-note-text">{n.text}</p>
            <p className="thanks-note-meta">
              {n.fromFirstName} · {n.itemName} · {formatDate(n.createdAtUtc)}
            </p>
          </li>
        ))}
      </ul>
    </section>
  )
}
