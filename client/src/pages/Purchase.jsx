import { useCallback, useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { salesApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { formatDateTime } from '../utils/format.js'

// דף רכישת יד 2: פרטי האיסוף/משלוח, אישור קבלה ודיווח על בעיה (לקונה), פרטי המכירה (למוכרת)
export function Purchase() {
  const { id } = useParams()
  const [params] = useSearchParams()
  const token = params.get('t') || ''
  const askHanded = params.get('action') === 'handed'
  const [sale, setSale] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [problemOpen, setProblemOpen] = useState(false)
  const [problemText, setProblemText] = useState('')

  const load = useCallback(async () => {
    try {
      setSale(await salesApi.get(id, token))
    } catch (err) {
      setError(err.message)
    }
  }, [id, token])

  useEffect(() => {
    load()
  }, [load])

  async function act(fn) {
    setBusy(true)
    setError('')
    try {
      setSale(await fn())
      setProblemOpen(false)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  if (!sale) return error ? <p className="state-message state-error">{error}</p> : <Loader label="טוען את הרכישה" />

  const buyer = sale.role === 'Buyer'
  const many = sale.items?.length > 1
  const canReport = buyer && sale.status === 'Paid' && new Date(sale.confirmUntilUtc) > new Date()

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>{buyer ? 'הרכישה שלך' : 'המכירה שלך'}</h1>
      <section className="loan-action-card">
        {many ? (
          <>
            <p className="admin-call-note">{formatDateTime(sale.createdAtUtc)}</p>
            {sale.items.map((line) => (
              <div className="question-item" key={line.itemId}>
                {line.image && <img src={line.image} alt="" />}
                <span>
                  <strong>{line.itemName}</strong>
                  <span className="admin-call-note"> · ₪{line.price}</span>
                </span>
              </div>
            ))}
          </>
        ) : (
          <div className="question-item">
            {sale.image && <img src={sale.image} alt="" />}
            <span>
              <strong>{sale.itemName}</strong>
              <span className="admin-call-note"> · {formatDateTime(sale.createdAtUtc)}</span>
            </span>
          </div>
        )}

        {buyer ? (
          sale.paymentMethod === 'Cash' ? (
            <p>
              לתשלום במזומן למוכרת באיסוף: <strong>₪{sale.price}</strong>
            </p>
          ) : sale.paymentMethod === 'BankTransfer' ? (
            <p>
              שולם בהעברה בנקאית למוכרת: <strong>₪{sale.price}</strong>
            </p>
          ) : (
            <p>
              שולם: <strong>₪{sale.total}</strong>
              {sale.deliveryFee > 0 && <span className="admin-call-note"> (כולל משלוח ₪{sale.deliveryFee})</span>}
            </p>
          )
        ) : sale.paymentMethod === 'CreditCard' ? (
          <p>
            נמכר ב-<strong>₪{sale.price}</strong> בכרטיס אשראי דרך האתר
          </p>
        ) : (
          <p>
            {sale.paymentMethod === 'Cash' ? 'הקונה תשלם לך במזומן באיסוף' : 'הקונה העבירה לך בהעברה בנקאית'}:{' '}
            <strong>₪{sale.price}</strong>
          </p>
        )}

        {sale.deliveryMethod === 'Delivery' ? (
          <p>
            משלוח עם שליח{sale.deliveryAddress ? ` אל ${sale.deliveryAddress}` : ''}.
            {sale.trackingId && <span className="admin-call-note"> מספר מעקב: {sale.trackingId}</span>}
          </p>
        ) : buyer ? (
          <p>
            <strong>איסוף עצמי:</strong> {sale.pickupAddress}
            <br />
            המוכרת: {sale.otherName}, <a href={`tel:${sale.otherPhone}`} dir="ltr">{sale.otherPhone}</a>
          </p>
        ) : (
          <p>
            הקונה תגיע לאסוף: {sale.otherName}, <a href={`tel:${sale.otherPhone}`} dir="ltr">{sale.otherPhone}</a>
          </p>
        )}

        {sale.status === 'Received' && (
          <p className="form-success">
            ✓ {buyer
              ? many ? 'אישרת שקיבלת את הפריטים. תודה!' : 'אישרת שקיבלת את הפריט. תודה!'
              : sale.paymentMethod === 'CreditCard'
                ? many ? 'הקונה קיבלה את הפריטים - הסכום יועבר אלייך בימים הקרובים.' : 'הקונה קיבלה את הפריט - הסכום יועבר אלייך בימים הקרובים.'
                : many ? 'הקונה קיבלה את הפריטים.' : 'הקונה קיבלה את הפריט.'}
          </p>
        )}
        {!buyer && sale.status === 'Paid' && (
          <div className={askHanded ? 'sale-handed is-asked' : 'sale-handed'}>
            {sale.handedOverAtUtc ? (
              <p className="form-success">✓ {many ? 'סימנת שמסרת את הפריטים.' : 'סימנת שמסרת את הפריט.'}</p>
            ) : sale.notHandedAtUtc ? (
              <p className="field-hint">סימנת שהפריט עוד לא נמסר. נעדכן כשהקונה תאשר קבלה.</p>
            ) : (
              <p className="field-hint">{many ? 'מסרת את הפריטים לקונה?' : 'מסרת את הפריט לקונה?'}</p>
            )}
            {!sale.handedOverAtUtc && (
              <button type="button" className="btn btn-primary" disabled={busy} onClick={() => act(() => salesApi.handed(id, token, true))}>
                {many ? 'מסרתי את הפריטים ✓' : 'מסרתי את הפריט ✓'}
              </button>
            )}
            {!sale.handedOverAtUtc && !sale.notHandedAtUtc && (
              <button type="button" className="link-button" disabled={busy} onClick={() => act(() => salesApi.handed(id, token, false))}>
                עוד לא נמסר
              </button>
            )}
          </div>
        )}
        {sale.status === 'Problem' && <p className="form-error">דווחה בעיה - הנהלת האתר בודקת ותחזור אליכן.</p>}

        {buyer && sale.status === 'Paid' && (
          <>
            <button type="button" className="btn btn-primary" disabled={busy} onClick={() => act(() => salesApi.received(id, token))}>
              {many ? 'קיבלתי את הפריטים ✓' : 'קיבלתי את הפריט ✓'}
            </button>
            {canReport && !problemOpen && (
              <button type="button" className="link-button" onClick={() => setProblemOpen(true)}>
                הפריט לא תואם לתיאור? דיווח על בעיה
              </button>
            )}
            {problemOpen && (
              <div className="ask-owner-box">
                <label htmlFor="problemText">מה הבעיה?</label>
                <textarea id="problemText" rows={3} value={problemText} onChange={(e) => setProblemText(e.target.value)} />
                <div className="ask-owner-actions">
                  <button type="button" className="btn btn-primary btn-small" disabled={busy || !problemText.trim()}
                    onClick={() => act(() => salesApi.problem(id, token, problemText.trim()))}>
                    שליחה
                  </button>
                  <button type="button" className="btn btn-ghost btn-small" onClick={() => setProblemOpen(false)}>
                    ביטול
                  </button>
                </div>
              </div>
            )}
            <p className="field-hint">אם לא יתקבל דיווח, הרכישה תאושר אוטומטית 3 ימים אחרי התשלום.</p>
          </>
        )}
        {error && <p className="form-error">{error}</p>}
      </section>
      <Link to="/" className="btn btn-ghost">
        לקטלוג
      </Link>
    </div>
  )
}
