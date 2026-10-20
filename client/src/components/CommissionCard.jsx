import { useEffect, useState } from 'react'
import { commissionsApi } from '../api/endpoints.js'
import { formatDateTime, formatMoney, formatMonth } from '../utils/format.js'
import { CommissionRentalsTable } from './CommissionRentalsTable.jsx'
import { SectionTitle } from './SectionTitle.jsx'

// התראה צהובה/אדומה כשדוח עמלה לא שולם בזמן - מוצגת בתוך כרטיס העמלה
function DebtNotice({ debt }) {
  if (!debt || debt.state === 'ok') return null
  return (
    <div className={`debt-notice debt-notice-${debt.state}`} role="alert">
      {debt.state === 'blocked' ? (
        <p>
          <strong>דוח העמלה של {debt.monthLabel} לא שולם מעל 14 ימים.</strong> עד לסילוק לא ניתן להוסיף פריטים והפריטים
          שלך מוסתרים זמנית מהקטלוג. מיד אחרי התשלום הכול חוזר לפעול.
        </p>
      ) : (
        <p>
          <strong>דוח העמלה של {debt.monthLabel} טרם שולם.</strong> נשארו {debt.daysLeft} ימים לתשלום.
        </p>
      )}
      {debt.payUrl && (
        <a className="btn btn-primary btn-small" href={debt.payUrl}>
          לתשלום באשראי
        </a>
      )}
    </div>
  )
}

// כרטיס "עמלת שירות" באזור האישי של משכירה: סכום העמלה לחודש הנוכחי (על השכרות שהוחזרו בו),
// בחירת חודש קודם, ופירוט מלא של כל ההשכרות שעליהן נגבית העמלה
export function CommissionCard({ debt }) {
  const [month, setMonth] = useState('')
  const [report, setReport] = useState(null)
  const [error, setError] = useState('')
  const [open, setOpen] = useState(false)

  useEffect(() => {
    let cancelled = false
    commissionsApi
      .my(month)
      .then((result) => {
        if (cancelled) return
        setReport(result)
        setError('')
        if (!month) setMonth(result.month)
      })
      .catch((err) => {
        if (!cancelled) setError(err.message)
      })
    return () => {
      cancelled = true
    }
  }, [month])

  if (error || !report) return <DebtNotice debt={debt} /> // בלי נתוני הדוח - לפחות ההתראה על החוב

  const hasRentals = report.totalRentals > 0

  return (
    <section className="profile-card profile-details-card commission-card">
      <div className="commission-card-head">
        <SectionTitle storageKey="commissionCard" defaultCollapsed className="profile-card-title">עמלת שירות</SectionTitle>
        <select
          value={report.month}
          onChange={(e) => {
            setMonth(e.target.value)
            setOpen(false)
          }}
          aria-label="בחירת חודש"
        >
          {report.recentMonths.map((m) => (
            <option key={m.month} value={m.month}>
              {formatMonth(m.month)}
            </option>
          ))}
        </select>
      </div>

      <DebtNotice debt={debt} />

      <div className="commission-card-summary">
        <div>
          <span className="commission-card-label">העמלה לחודש {formatMonth(report.month)}</span>
          <strong className="commission-card-amount">{formatMoney(report.totalCommission)}</strong>
        </div>
        <div>
          <span className="commission-card-label">השכרות שהוחזרו</span>
          <strong>{report.totalRentals}</strong>
        </div>
        <div>
          <span className="commission-card-label">סטטוס</span>
          <span className={`commission-status ${report.paid ? 'is-paid' : ''}`}>
            {report.paid ? `שולם ${formatDateTime(report.paidAtUtc)}` : hasRentals ? 'ממתין לתשלום' : '—'}
          </span>
        </div>
      </div>

      <p className="commission-card-note">
        העמלה היא {report.percent}% מסכום הציוד בכל השכרה שהוחזרה (ללא משלוח). דוח מפורט נשלח אליך במייל
        ב-1 לחודש הבא.
      </p>

      {hasRentals && (
        <>
          <button type="button" className="btn profile-card-action" onClick={() => setOpen((prev) => !prev)}>
            {open ? 'הסתרת הפירוט' : 'פירוט ההשכרות'}
          </button>
          {open && (
            <CommissionRentalsTable
              rentals={report.rentals}
              totalRentalAmount={report.totalRentalAmount}
              totalCommission={report.totalCommission}
            />
          )}
        </>
      )}
    </section>
  )
}
