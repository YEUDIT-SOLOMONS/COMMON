import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { salesApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { formatDateTime } from '../utils/format.js'

const STATUS = { Paid: 'ממתין לקבלה', Received: 'התקבל', Problem: 'דווחה בעיה' }
const METHOD = { CreditCard: 'אשראי (דרך האתר)', Cash: 'מזומן למוכרת', BankTransfer: 'העברה למוכרת' }

// מכירות יד 2: מה נמכר, מה התקבל, וכמה להעביר לכל מוכרת
export function AdminSales() {
  const [rows, setRows] = useState(null)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState(null)

  const load = useCallback(() => {
    salesApi
      .adminList()
      .then((r) => setRows(r ?? []))
      .catch((err) => setError(err.message))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function act(id, fn, question) {
    if (!window.confirm(question)) return
    setBusyId(id)
    try {
      await fn()
      load()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  if (error) return <p className="state-message state-error">{error}</p>
  if (!rows) return <Loader label="טוען מכירות" />
  if (rows.length === 0) return <p className="state-message">עדיין אין מכירות.</p>

  const owed = rows.filter((s) => s.paymentMethod === 'CreditCard' && s.status === 'Received' && !s.sellerPaidAtUtc).reduce((sum, s) => sum + s.sellerAmount, 0)
  const commission = rows.reduce((sum, s) => sum + s.commission, 0)

  return (
    <section>
      <p className="field-hint">
        להעביר למוכרות: <strong>₪{owed.toFixed(2)}</strong> · סך העמלות: ₪{commission.toFixed(2)}
      </p>
      <div className="table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>מתי</th>
              <th>פריט</th>
              <th>מוכרת</th>
              <th>קונה</th>
              <th>סכומים</th>
              <th>משלוח</th>
              <th>מצב</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((s) => (
              <tr key={s.id}>
                <td>{formatDateTime(s.createdAtUtc)}</td>
                <td>
                  <Link to={`/?item=${encodeURIComponent(s.itemId)}`}>{s.itemName}</Link>
                </td>
                <td>
                  {s.sellerName}
                  <span className="admin-call-note" dir="ltr"> · {s.sellerPhone}</span>
                </td>
                <td>
                  {s.buyerName}
                  <span className="admin-call-note" dir="ltr"> · {s.buyerPhone}</span>
                </td>
                <td>
                  <span className="admin-call-note">{METHOD[s.paymentMethod] ?? s.paymentMethod}</span>
                  <br />₪{s.price} · עמלה ₪{s.commission}
                  {s.paymentMethod === 'CreditCard' ? <> · <strong>למוכרת ₪{s.sellerAmount}</strong></> : <span className="admin-call-note"> (בדוח העמלות החודשי)</span>}
                  {s.deliveryFee > 0 && <span className="admin-call-note"> · משלוח ₪{s.deliveryFee}</span>}
                </td>
                <td>
                  {s.deliveryMethod === 'Delivery' ? 'שליח' : 'איסוף עצמי'}
                  {s.trackingId && <span className="admin-call-note"> · {s.trackingId}</span>}
                  {s.dispatchError && <span className="form-error"> הזמנת השליח נכשלה: {s.dispatchError}</span>}
                </td>
                <td>
                  {STATUS[s.status] ?? s.status}
                  {s.autoConfirmed && <span className="admin-call-note"> (אוטומטית)</span>}
                  {s.status === 'Paid' && s.handedOverAtUtc && <span className="admin-call-note"> · המוכרת מסרה</span>}
                  {s.status === 'Paid' && s.notHandedAtUtc && !s.handedOverAtUtc && <span className="form-error"> · לא נמסר (האישור האוטומטי נעצר)</span>}
                  {s.problemText && <span className="admin-call-note"> · {s.problemText}</span>}
                  {s.sellerPaidAtUtc && <span className="admin-call-note"> · הועבר למוכרת {formatDateTime(s.sellerPaidAtUtc)}</span>}
                </td>
                <td>
                  {s.status === 'Problem' && (
                    <button type="button" className="btn btn-ghost btn-small" disabled={busyId === s.id}
                      onClick={() => act(s.id, () => salesApi.adminRelease(s.id), 'לשחרר את התשלום למוכרת?')}>
                      שחרור למוכרת
                    </button>
                  )}
                  {s.paymentMethod === 'CreditCard' && s.status === 'Received' && !s.sellerPaidAtUtc && (
                    <button type="button" className="btn btn-ghost btn-small" disabled={busyId === s.id}
                      onClick={() => act(s.id, () => salesApi.adminSellerPaid(s.id), `לסמן שהועברו ₪${s.sellerAmount} ל${s.sellerName}?`)}>
                      סימון כהועבר
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
