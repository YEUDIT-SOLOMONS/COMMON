import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { questionsApi } from '../api/endpoints.js'
import { formatDateTime } from '../utils/format.js'
import { SectionTitle } from './SectionTitle.jsx'

// "התכתבויות" באזור האישי - מוסתר כשאין אף התכתבות
export function ProfileQuestions() {
  const [list, setList] = useState(null)

  useEffect(() => {
    questionsApi
      .mine()
      .then((r) => setList(r ?? []))
      .catch(() => setList([]))
  }, [])

  if (!list || list.length === 0) return null
  const awaiting = list.some((q) => q.awaitingMe)

  return (
    <section className="profile-section">
      <div className="profile-section-head">
        {/* פתוח כשיש התכתבות שמחכה לתגובה שלי; אחרת מכווץ */}
        <SectionTitle
          key={awaiting ? 'awaiting' : 'quiet'}
          storageKey={awaiting ? 'questions-awaiting' : 'questions'}
          defaultCollapsed={!awaiting}
        >
          התכתבויות
        </SectionTitle>
      </div>
      <ul className="question-list">
        {list.map((q) => (
          <li key={q.id}>
            <Link to={`/questions/${q.id}`} className="question-list-row">
              {q.image ? <img src={q.image} alt="" loading="lazy" /> : <span className="question-list-ph">💬</span>}
              <span className="question-list-body">
                <strong>{q.itemName}</strong>
                <span className="admin-call-note">
                  {q.role === 'Owner' ? 'שאלה על פריט שלך' : 'שאלת את המשכירה'} · {q.count} הודעות ·{' '}
                  {formatDateTime(q.lastMessageAtUtc)}
                </span>
              </span>
              {q.awaitingMe && <span className="question-list-new">{q.role === 'Owner' ? 'מחכה לתשובה' : 'תשובה חדשה'}</span>}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
