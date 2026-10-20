import { useCallback, useEffect, useState } from 'react'
import { commissionsApi } from '../api/endpoints.js'
import { Modal } from '../components/Modal.jsx'
import { CommissionRentalsTable } from '../components/CommissionRentalsTable.jsx'
import { formatDateTime, formatMoney, formatMonth } from '../utils/format.js'
import { Loader } from '../components/Loader.jsx'
import { openDataUrl } from '../utils/proof.js'

// לשונית "עמלות" במסך הניהול: לכל חודש - כמה כל משכירה חייבת (על השכרות שהוחזרו באותו חודש), פירוט
// מלא, שליחת הדוח במייל (בנוסף לשליחה האוטומטית ב-1 לחודש) וסימון "שולם"
export function AdminCommissions() {
  const [month, setMonth] = useState('')
  const [overview, setOverview] = useState(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busyId, setBusyId] = useState(null)
  const [openId, setOpenId] = useState(null)
  const [preview, setPreview] = useState(null) // { ToEmail, Subject, Html } של המייל שמוצג בתצוגה מקדימה

  const load = useCallback(async (m) => {
    try {
      const result = await commissionsApi.adminOverview(m)
      setOverview(result)
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }, [])

  useEffect(() => {
    load(month)
  }, [month, load])

  async function openProof(row) {
    try {
      const proof = await commissionsApi.adminProof(row.ownerId, overview.month)
      openDataUrl(proof.dataUrl, proof.fileName)
    } catch (err) {
      setError(err.message)
    }
  }

  async function togglePaid(row) {
    setBusyId(row.ownerId)
    setNotice('')
    try {
      await commissionsApi.adminSetPaid({ ownerId: row.ownerId, month: overview.month, paid: !row.paid })
      await load(overview.month)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  async function showPreview(row) {
    setBusyId(row.ownerId)
    setNotice('')
    setError('')
    try {
      setPreview(await commissionsApi.adminPreview(row.ownerId, overview.month))
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  async function sendReport(row) {
    setBusyId(row.ownerId)
    setNotice('')
    try {
      await commissionsApi.adminSend({ ownerId: row.ownerId, month: overview.month })
      setNotice(`הדוח נשלח אל ${row.ownerName} (${row.ownerEmail})`)
      await load(overview.month)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  if (error && !overview) return <p className="form-error">{error}</p>
  if (!overview) return <Loader label="טוען עמלות" compact />

  return (
    <div className="admin-commissions">
      <div className="admin-users-controls">
        <select
          value={overview.month}
          onChange={(e) => {
            setMonth(e.target.value)
            setOpenId(null)
          }}
          aria-label="חודש"
        >
          {overview.months.map((m) => (
            <option key={m} value={m}>
              {formatMonth(m)}
            </option>
          ))}
        </select>
        <p className="admin-commissions-hint">
          עמלה {overview.percent}% מסכום הציוד של כל השכרה שהוחזרה בחודש. דוחות נשלחים אוטומטית ב-1 לחודש הבא.
        </p>
      </div>

      <ul className="admin-stats admin-stats-3">
        <li className="admin-stat">
          <strong>{formatMoney(overview.totalCommission)}</strong>
          <span>סה"כ עמלות לחודש</span>
        </li>
        <li className="admin-stat">
          <strong>{formatMoney(overview.paidCommission)}</strong>
          <span>שולם</span>
        </li>
        <li className="admin-stat">
          <strong>{formatMoney(overview.unpaidCommission)}</strong>
          <span>ממתין לתשלום</span>
        </li>
      </ul>

      {error && <p className="form-error">{error}</p>}
      {notice && <p className="form-success">{notice}</p>}

      {overview.owners.length === 0 ? (
        <p className="state-message">אין השכרות שהוחזרו בחודש הזה.</p>
      ) : (
        <ul className="admin-commissions-list">
          {overview.owners.map((row) => (
            <li key={row.ownerId} className="admin-commissions-item">
              <div className="admin-commissions-row">
                <div className="admin-commissions-owner">
                  <strong>{row.ownerName}</strong>
                  <span dir="ltr" className="admin-ltr">
                    {row.ownerEmail} {row.ownerPhone && `· ${row.ownerPhone}`}
                  </span>
                </div>
                <div className="admin-commissions-numbers">
                  <span>{row.totalRentals} השכרות</span>
                  <strong>{formatMoney(row.totalCommission)}</strong>
                </div>
                <span className={`commission-status ${row.paid ? 'is-paid' : ''}`}>
                  {row.paid ? (row.offsetAmount > 0 ? 'קוזז מתשלומי אשראי' : 'שולם') : 'ממתין'}
                  {!row.paid && row.offsetAmount > 0 && ` · קוזזו ${formatMoney(row.offsetAmount)}`}
                </span>
                {row.hasProof && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    title={`הועלתה ${formatDateTime(row.proofUploadedAtUtc)}`}
                    onClick={() => openProof(row)}
                  >
                    אסמכתא
                  </button>
                )}
                <div className="admin-commissions-actions">
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    onClick={() => setOpenId(openId === row.ownerId ? null : row.ownerId)}
                  >
                    {openId === row.ownerId ? 'הסתרת פירוט' : 'פירוט'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    disabled={busyId === row.ownerId}
                    onClick={() => showPreview(row)}
                  >
                    תצוגה מקדימה
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    disabled={busyId === row.ownerId}
                    onClick={() => sendReport(row)}
                    title={row.sentAtUtc ? `נשלח ${formatDateTime(row.sentAtUtc)}` : 'עוד לא נשלח'}
                  >
                    {row.sentAtUtc ? 'שליחה חוזרת במייל' : 'שליחה במייל'}
                  </button>
                  <button
                    type="button"
                    className={`btn btn-small ${row.paid ? 'btn-ghost' : 'profile-card-action'}`}
                    disabled={busyId === row.ownerId}
                    onClick={() => togglePaid(row)}
                  >
                    {row.paid ? 'ביטול סימון שולם' : 'סימון כשולם'}
                  </button>
                </div>
              </div>
              {openId === row.ownerId && (
                <CommissionRentalsTable
                  rentals={row.rentals}
                  totalRentalAmount={row.totalRentalAmount}
                  totalCommission={row.totalCommission}
                />
              )}
            </li>
          ))}
        </ul>
      )}

      {preview && (
        <Modal title="תצוגה מקדימה של הדוח" wide onClose={() => setPreview(null)}>
          <div className="email-preview">
            <dl>
              <div>
                <dt>אל</dt>
                <dd dir="ltr" className="admin-ltr">
                  {preview.toName} &lt;{preview.toEmail}&gt;
                </dd>
              </div>
              <div>
                <dt>נושא</dt>
                <dd>{preview.subject}</dd>
              </div>
            </dl>
            <iframe title="תצוגה מקדימה של מייל הדוח" sandbox="" srcDoc={preview.html} />
          </div>
        </Modal>
      )}
    </div>
  )
}
