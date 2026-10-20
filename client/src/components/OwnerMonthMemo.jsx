import { useEffect, useState } from 'react'
import { loansApi } from '../api/endpoints.js'
import { formatMoney } from '../utils/format.js'

const MONTHS = ['ינואר', 'פברואר', 'מרץ', 'אפריל', 'מאי', 'יוני', 'יולי', 'אוגוסט', 'ספטמבר', 'אוקטובר', 'נובמבר', 'דצמבר']

// פתק סיכום החודש למשכירה - מוצג רק כשהציוד שלה הושכר החודש
export function OwnerMonthMemo({ ownerId }) {
  const [summary, setSummary] = useState(null)

  useEffect(() => {
    loansApi
      .ownerSummary(ownerId)
      .then(setSummary)
      .catch(() => {})
  }, [ownerId])

  if (!summary?.rentals) return null

  const month = MONTHS[new Date().getMonth()]
  const times = summary.rentals === 1 ? 'פעם אחת' : `${summary.rentals} פעמים`

  return (
    <aside className="memo-note memo-blue memo-wide" aria-label="סיכום החודש">
      <p className="memo-note-title">
        בחודש {month} הציוד שלך הושכר {times} והכניס לך {formatMoney(summary.earned)} 💛
      </p>
      <p className="memo-note-meta">אחרי עמלת השירות · השכרות שהוחזרו החודש</p>
    </aside>
  )
}
