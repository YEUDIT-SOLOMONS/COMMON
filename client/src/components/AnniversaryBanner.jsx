import { useEffect, useState } from 'react'
import { usersApi } from '../api/endpoints.js'

const SHOW_DAYS = 7

function yearsText(years) {
  if (years === 1) return 'שנה שלמה איתנו!'
  if (years === 2) return 'שנתיים איתנו!'
  return `${years} שנים איתנו!`
}

// בשבוע שאחרי יום השנה להצטרפות: "שנה שלמה איתנו! תודה שאת חלק מהקהילה"
export function AnniversaryBanner({ userId }) {
  const [years, setYears] = useState(0)

  useEffect(() => {
    usersApi
      .getById(userId)
      .then((u) => {
        if (!u?.createdAtUtc) return
        const joined = new Date(u.createdAtUtc)
        const now = new Date()
        let full = now.getFullYear() - joined.getFullYear()
        const anniversary = new Date(joined)
        anniversary.setFullYear(joined.getFullYear() + full)
        if (anniversary > now) {
          full -= 1
          anniversary.setFullYear(anniversary.getFullYear() - 1)
        }
        const daysSince = (now - anniversary) / 86400000
        if (full >= 1 && daysSince <= SHOW_DAYS) setYears(full)
      })
      .catch(() => {})
  }, [userId])

  if (!years) return null

  return (
    <p className="anniversary-banner" role="status">
      <strong>{yearsText(years)}</strong> תודה שאת חלק מהקהילה
    </p>
  )
}
