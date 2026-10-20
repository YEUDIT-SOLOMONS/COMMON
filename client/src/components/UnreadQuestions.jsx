import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { questionsApi } from '../api/endpoints.js'

const POLL_MS = 60_000

// הודעה חדשה שלא נקראה בהתכתבות - סמל צף בצד שמאל למטה בכל דפי האתר
export function UnreadQuestions() {
  const [list, setList] = useState([])
  const [open, setOpen] = useState(false)
  const { pathname } = useLocation()

  const load = useCallback(() => {
    questionsApi
      .mine()
      .then((r) => setList((r ?? []).filter((q) => q.unread)))
      .catch(() => {})
  }, [])

  useEffect(() => {
    load()
    const t = setInterval(load, POLL_MS)
    return () => clearInterval(t)
  }, [load, pathname])

  const visible = list.filter((q) => pathname !== `/questions/${q.id}`)
  if (visible.length === 0) return null

  return (
    <div className="waiting-fab unread-fab">
      {open && (
        <section className="waiting-panel" aria-label="הודעות חדשות בהתכתבויות">
          <header>
            <strong>הודעות חדשות</strong>
            <button type="button" className="waiting-close" aria-label="סגירה" onClick={() => setOpen(false)}>
              ×
            </button>
          </header>
          <ul>
            {visible.map((q) => (
              <li key={q.id}>
                <p className="waiting-item">
                  <strong>{q.itemName}</strong> · {q.role === 'Owner' ? 'שאלה על פריט שלך' : 'תשובה מהמשכירה'}
                </p>
                {q.lastText && <p className="unread-text">"{q.lastText}"</p>}
                <Link to={`/questions/${q.id}`} className="btn btn-primary btn-small" onClick={() => setOpen(false)}>
                  {q.role === 'Owner' ? 'לתשובה' : 'לצפייה ולתשובה'}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
      <button
        type="button"
        className="waiting-button"
        aria-expanded={open}
        aria-label="הודעה חדשה בהתכתבות"
        onClick={() => setOpen((v) => !v)}
      >
        <span aria-hidden="true">💬</span>
        <span className="waiting-count">{visible.length}</span>
        <span className="waiting-label">הודעה חדשה</span>
      </button>
    </div>
  )
}
