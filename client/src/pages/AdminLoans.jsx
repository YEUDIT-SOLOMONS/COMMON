import { useCallback, useEffect, useMemo, useState } from 'react'
import { adminApi } from '../api/endpoints.js'
import { ConfirmDialog } from '../components/ConfirmDialog.jsx'
import { Loader } from '../components/Loader.jsx'
import { LoanStatusBadge, paymentMethodLabel, paymentStatusLabel } from '../components/StatusBadge.jsx'
import { formatDate, formatDateTime, formatMoney } from '../utils/format.js'

// כל ההשכרות באתר (החדשות ראשונות), עם חיפוש ואפשרות מחיקה
export function AdminLoans() {
  const [rows, setRows] = useState(null)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [toDelete, setToDelete] = useState(null)
  const [deleting, setDeleting] = useState(false)

  const load = useCallback(async () => {
    try {
      setRows(await adminApi.loans())
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const shown = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!rows) return []
    return term
      ? rows.filter((r) =>
          [r.itemName, r.renterName, r.renterEmail, r.ownerName].some((f) => (f ?? '').toLowerCase().includes(term)),
        )
      : rows
  }, [rows, search])

  async function confirmDelete() {
    setDeleting(true)
    try {
      await adminApi.deleteLoan(toDelete.id)
      setToDelete(null)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setDeleting(false)
    }
  }

  if (!rows) return error ? <p className="state-message state-error">{error}</p> : <Loader label="טוען השכרות" />

  return (
    <section>
      <div className="admin-users-controls">
        <input
          type="search"
          placeholder="חיפוש לפי פריט, שוכרת או משכירה"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <span className="payment-status-note">{shown.length} השכרות</span>
      </div>
      {error && <p className="form-error">{error}</p>}

      {shown.length === 0 ? (
        <p className="state-message">אין השכרות להצגה.</p>
      ) : (
        <div className="table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>נוצרה</th>
                <th>פריט</th>
                <th>שוכרת</th>
                <th>משכירה</th>
                <th>תאריכים</th>
                <th>סטטוס</th>
                <th>תשלום</th>
                <th>סכום</th>
                <th>
                  <span className="visually-hidden">פעולות</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {shown.map((r) => (
                <tr key={r.id}>
                  <td>{formatDateTime(r.createdAt)}</td>
                  <td>{r.itemName}</td>
                  <td>
                    {r.renterName}
                    {r.renterEmail && (
                      <span className="admin-call-note" dir="ltr">
                        {' '}
                        · {r.renterEmail}
                      </span>
                    )}
                  </td>
                  <td>{r.ownerName || '—'}</td>
                  <td>
                    {formatDate(r.startDate)}
                    {r.startTime ? ` ${r.startTime}` : ''} - {formatDateTime(r.expectedReturnDate)}
                  </td>
                  <td>
                    <LoanStatusBadge status={r.status} />
                    {r.returnedBy && <span className="admin-call-note"> · {r.returnedBy}</span>}
                  </td>
                  <td>
                    {paymentMethodLabel(r.paymentMethod)}
                    <span className="admin-call-note"> · {paymentStatusLabel(r.paymentStatus)}</span>
                  </td>
                  <td>{formatMoney(r.total)}</td>
                  <td>
                    <button type="button" className="btn btn-ghost btn-small" onClick={() => setToDelete(r)}>
                      מחיקה
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {toDelete && (
        <ConfirmDialog
          title="מחיקת השכרה"
          message={`למחוק לצמיתות את ההשכרה של "${toDelete.itemName}" (${toDelete.renterName})? אי אפשר לשחזר.${
            toDelete.status === 'Active' ? ' שימי לב: ההשכרה עדיין פעילה.' : ''
          }`}
          confirmLabel="מחיקה"
          busy={deleting}
          onConfirm={confirmDelete}
          onCancel={() => setToDelete(null)}
        />
      )}
    </section>
  )
}
