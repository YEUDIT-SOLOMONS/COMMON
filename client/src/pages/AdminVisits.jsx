import { useEffect, useState } from 'react'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'

function israelDay(offset = 0) {
  const d = new Date(Date.now() - offset * 86400000)
  return d.toLocaleDateString('en-CA', { timeZone: 'Asia/Jerusalem' })
}

// ספירת גולשים: כניסות לאתר (פעם אחת לכל ביקור) וגולשות חדשות, לפי יום
export function AdminVisits() {
  const [days, setDays] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    adminApi
      .visits()
      .then((d) => setDays(d ?? []))
      .catch((err) => setError(err.message))
  }, [])

  if (error) return <p className="state-message state-error">{error}</p>
  if (!days) return <Loader label="טוען נתוני גולשים" />

  const byDay = Object.fromEntries(days.map((d) => [d.day, d]))
  const sumSince = (n) => {
    let visits = 0
    let fresh = 0
    for (let i = 0; i < n; i++) {
      const d = byDay[israelDay(i)]
      visits += d?.visits ?? 0
      fresh += d?.newVisitors ?? 0
    }
    return { visits, fresh }
  }
  const today = sumSince(1)
  const yesterday = byDay[israelDay(1)] ?? { visits: 0, newVisitors: 0 }
  const week = sumSince(7)
  const month = sumSince(30)
  const total = days.reduce((s, d) => ({ visits: s.visits + d.visits, fresh: s.fresh + d.newVisitors }), { visits: 0, fresh: 0 })

  const last14 = Array.from({ length: 14 }, (_, i) => israelDay(13 - i)).map((day) => ({ day, ...(byDay[day] ?? { visits: 0, newVisitors: 0 }) }))
  const max = Math.max(1, ...last14.map((d) => d.visits))

  const cards = [
    { label: 'היום', v: today.visits, f: today.fresh },
    { label: 'אתמול', v: yesterday.visits, f: yesterday.newVisitors },
    { label: '7 ימים', v: week.visits, f: week.fresh },
    { label: '30 יום', v: month.visits, f: month.fresh },
    { label: 'מאז ההתחלה', v: total.visits, f: total.fresh },
  ]

  return (
    <section className="admin-visits">
      <p className="field-hint">
        "כניסות" = כל ביקור באתר (פעם אחת לכל פתיחה). "גולשות חדשות" = מי שנכנסה מהדפדפן הזה בפעם הראשונה.
      </p>
      <div className="visits-cards">
        {cards.map((c) => (
          <div key={c.label} className="visits-card">
            <span className="visits-card-label">{c.label}</span>
            <strong className="visits-card-num">{c.v.toLocaleString('he-IL')}</strong>
            <span className="visits-card-sub">{c.f.toLocaleString('he-IL')} חדשות</span>
          </div>
        ))}
      </div>

      <h3>14 הימים האחרונים</h3>
      <ul className="visits-bars">
        {last14.map((d) => (
          <li key={d.day}>
            <span className="visits-bar-day">{d.day.slice(8, 10)}/{d.day.slice(5, 7)}</span>
            <span className="visits-bar-track">
              <span className="visits-bar" style={{ width: `${(d.visits / max) * 100}%` }} />
            </span>
            <span className="visits-bar-num">{d.visits}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}
