import { useCallback, useEffect, useState } from 'react'
import { availabilityCallsApi } from '../api/endpoints.js'
import { formatDate, formatDateTime } from '../utils/format.js'

const SEEN_KEY = 'common_seen_answers'
const POLL_MS = 60_000

// תצוגה מקדימה עם נתוני דוגמה: ‎?demo=waiting
function isDemo() {
  return typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('demo') === 'waiting'
}

const DEMO_ITEMS = [
  {
    requestId: 'demo1',
    itemId: 'demo',
    itemName: 'Canon RF 50mm f/1.8',
    startDate: '2026-10-14',
    returnDate: '2026-10-15',
    createdAtUtc: new Date(Date.now() - 25 * 60 * 1000).toISOString(),
    ownerEmailed: true,
    retryAtLocal: null,
    notify: 'email',
    answer: null,
  },
  {
    requestId: 'demo2',
    itemId: 'demo',
    itemName: 'מבזק Godox V1',
    startDate: '2026-10-20',
    returnDate: '2026-10-20',
    createdAtUtc: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
    ownerEmailed: true,
    retryAtLocal: '2026-10-13T07:00',
    notify: 'phone',
    answer: 'Available',
  },
]

function readSeen() {
  try {
    return JSON.parse(localStorage.getItem(SEEN_KEY) || '[]')
  } catch {
    return []
  }
}

function retryText(local) {
  if (!local) return ''
  const [date, time] = local.split('T')
  const today = new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Jerusalem' })
  return date === today ? `היום ב-${time}` : `ב-${formatDate(date)} ב-${time}`
}

// בקשות אימות זמינות שממתינות לתשובת המשכירה - סמל צף ובולט בכל דפי האתר, עם פירוט מה נשלח ומתי
export function WaitingRequests() {
  const [items, setItems] = useState([])
  const [open, setOpen] = useState(false)
  const [seen, setSeen] = useState(readSeen)

  const load = useCallback(() => {
    if (isDemo()) {
      setItems(DEMO_ITEMS)
      return
    }
    availabilityCallsApi
      .mine()
      .then((list) => setItems(list ?? []))
      .catch(() => {})
  }, [])

  useEffect(() => {
    load()
    const t = setInterval(load, POLL_MS)
    return () => clearInterval(t)
  }, [load])

  const visible = items.filter((r) => !r.answer || !seen.includes(r.requestId))
  if (visible.length === 0) return null

  const answered = visible.filter((r) => r.answer).length

  function markSeen(id) {
    const next = [...seen, id]
    setSeen(next)
    try {
      localStorage.setItem(SEEN_KEY, JSON.stringify(next.slice(-50)))
    } catch {
      // בלי אחסון - יוסתר עד רענון
    }
  }

  async function cancel(id) {
    try {
      await availabilityCallsApi.cancelRequest(id)
    } finally {
      load()
    }
  }

  const dates = (r) => (r.startDate === r.returnDate ? formatDate(r.startDate) : `${formatDate(r.startDate)} עד ${formatDate(r.returnDate)}`)

  return (
    <div className={`waiting-fab${open ? ' is-open' : ''}${answered ? ' has-answer' : ''}`}>
      {open && (
        <section className="waiting-panel" aria-label="בקשות זמינות שממתינות לתשובה">
          <header>
            <strong>אימות זמינות</strong>
            <button type="button" className="waiting-close" aria-label="סגירה" onClick={() => setOpen(false)}>
              ×
            </button>
          </header>
          <ul>
            {visible.map((r) => (
              <li key={r.requestId}>
                <p className="waiting-item">
                  <strong>{r.itemName}{r.quantity > 1 ? ` (${r.quantity} יחידות)` : ''}</strong> · {dates(r)}
                </p>
                {r.answer === 'Available' ? (
                  <>
                    <p className="waiting-good">✓ הצלמת אישרה שהפריט פנוי!</p>
                    <a
                      className="btn btn-primary btn-small"
                      href={`/?item=${encodeURIComponent(r.itemId)}&from=${r.startDate}&to=${r.returnDate}${r.startTime && r.returnTime ? `&st=${r.startTime}&rt=${r.returnTime}` : ''}${r.quantity > 1 ? `&q=${r.quantity}` : ''}&confirmed=${r.requestId}`}
                      onClick={() => markSeen(r.requestId)}
                    >
                      להמשך ההזמנה
                    </a>
                  </>
                ) : r.answer === 'Unavailable' ? (
                  <>
                    <p className="waiting-bad">הצלמת עדכנה שהפריט לא פנוי בתאריכים האלה.</p>
                    <button type="button" className="link-button" onClick={() => markSeen(r.requestId)}>
                      הבנתי
                    </button>
                  </>
                ) : (
                  <>
                    <p>נשלחה בקשה ב-{formatDateTime(r.createdAtUtc)}.</p>
                    {r.ownerEmailed && <p>שלחנו לצלמת מייל עם כפתור לתשובה.</p>}
                    {r.retryAtLocal && <p>ננסה להתקשר אליה שוב {retryText(r.retryAtLocal)}.</p>}
                    <p>נעדכן אותך {r.notify === 'phone' ? 'בטלפון' : 'במייל'} ברגע שתענה.</p>
                    <button type="button" className="link-button" onClick={() => cancel(r.requestId)}>
                      ביטול הבקשה
                    </button>
                  </>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
      <button
        type="button"
        className="waiting-button"
        aria-expanded={open}
        aria-label={answered ? 'התקבלה תשובה לבקשת זמינות' : 'בקשת זמינות ממתינה לתשובה'}
        onClick={() => setOpen((v) => !v)}
      >
        <span aria-hidden="true">{answered ? '🔔' : '⏳'}</span>
        <span className="waiting-count">{visible.length}</span>
        <span className="waiting-label">{answered ? 'יש תשובה!' : 'ממתינה לתשובה'}</span>
      </button>
    </div>
  )
}
