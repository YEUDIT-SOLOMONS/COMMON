import { useState } from 'react'
import { isoDay } from '../utils/schedule.js'

const WEEKDAYS = ['א', 'ב', 'ג', 'ד', 'ה', 'ו', 'ש']
const MONTHS = ['ינואר', 'פברואר', 'מרץ', 'אפריל', 'מאי', 'יוני', 'יולי', 'אוגוסט', 'ספטמבר', 'אוקטובר', 'נובמבר', 'דצמבר']

// לוח חודשי פשוט (ראשון-שבת). dayClass(day) מחזיר מחלקות לעיצוב היום (תפוס/נבחר/חסום...),
// isDisabled(day) - ימים שאי אפשר ללחוץ עליהם
export function MonthCalendar({ dayClass, isDisabled, onDayClick, dayTitle }) {
  const today = new Date()
  const [offset, setOffset] = useState(0)
  const first = new Date(today.getFullYear(), today.getMonth() + offset, 1)
  const daysInMonth = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate()
  const cells = [...Array(first.getDay()).fill(null)]
  for (let d = 1; d <= daysInMonth; d++) cells.push(isoDay(new Date(first.getFullYear(), first.getMonth(), d)))
  const todayIso = isoDay(today)

  return (
    <div className="month-calendar">
      <div className="month-calendar-head">
        <button type="button" className="month-calendar-nav" onClick={() => setOffset((o) => o - 1)} disabled={offset <= 0} aria-label="החודש הקודם">
          ›
        </button>
        <strong>
          {MONTHS[first.getMonth()]} {first.getFullYear()}
        </strong>
        <button type="button" className="month-calendar-nav" onClick={() => setOffset((o) => o + 1)} disabled={offset >= 11} aria-label="החודש הבא">
          ‹
        </button>
      </div>
      <div className="month-calendar-grid">
        {WEEKDAYS.map((w) => (
          <span key={w} className="month-calendar-weekday">
            {w}
          </span>
        ))}
        {cells.map((day, i) =>
          day ? (
            <button
              key={day}
              type="button"
              className={`month-calendar-day ${day === todayIso ? 'is-today' : ''} ${day < todayIso ? 'is-past' : ''} ${dayClass?.(day) ?? ''}`}
              disabled={day < todayIso || Boolean(isDisabled?.(day))}
              onClick={() => onDayClick?.(day)}
              title={dayTitle?.(day)}
            >
              {Number(day.slice(8))}
            </button>
          ) : (
            <span key={`e${i}`} />
          ),
        )}
      </div>
    </div>
  )
}
