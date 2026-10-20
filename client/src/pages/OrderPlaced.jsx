import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { loansApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { ProofButton } from '../components/ProofButton.jsx'
import { CallPartyButton } from '../components/CallPartyButton.jsx'
import { ShipmentPanel } from '../components/ShipmentTracker.jsx'
import { LoanStatusBadge, paymentMethodLabel, paymentStatusLabel } from '../components/StatusBadge.jsx'
import { useAuth } from '../context/AuthContext.jsx'
import { ConfirmDialog } from '../components/ConfirmDialog.jsx'
import { formatDate, formatDateTime, formatMoney, israelNowWall, wallMs } from '../utils/format.js'

// מסך "ההזמנה בוצעה": נפתח מהמייל ומחלון האישור. מציג את פרטי ההזמנה, ולמשלוח - זמן הגעה ומעקב חי,
// גם בדרך הלוך (המשלוח אל השוכרת) וגם בדרך חזור (איסוף ההחזרה). פתוח לשוכרת, למשכירה ולמנהלות.
const DEMO_STAGES = {
  Pending: 'הוזמן',
  Assigned: 'שליח שובץ',
  OnTheWay: 'בדרך לאיסוף',
  PickedUp: 'בדרך ליעד',
  Delivered: 'נמסר',
  Canceled: 'בוטל',
}

const DEMO_LOANS = [
  {
    id: 'demo',
    itemName: 'חצובה איכותית (לדוגמה)',
    borrowerId: 'demo',
    startDate: new Date(Date.now() + 86400000).toISOString().slice(0, 10) + 'T00:00:00',
    startTime: '10:00',
    expectedReturnDate: new Date(Date.now() + 3 * 86400000).toISOString().slice(0, 10) + 'T12:00:00',
    deliveryMethod: 'Delivery',
    returnMethod: 'CourierPickup',
    paymentMethod: 'Cash',
    paymentStatus: 'Pending',
    status: 'Active',
    deliveryFee: 35,
    outboundTrackable: true,
    returnTrackable: true,
  },
  {
    id: 'demo2',
    itemName: 'עדשה 50mm (לדוגמה)',
    borrowerId: 'demo',
    deliveryMethod: 'Delivery',
    returnMethod: 'CourierPickup',
    status: 'Active',
    deliveryFee: 0,
  },
]

// הדגמת תשלום במזומן (איסוף עצמי): /order/demo?pay=cash&role=renter  או  &role=owner
const DEMO_CASH_LOANS = [
  {
    id: 'demo-cash',
    itemName: 'חצובה איכותית (לדוגמה)',
    borrowerId: 'demo',
    startDate: new Date(Date.now() + 86400000).toISOString().slice(0, 10) + 'T00:00:00',
    startTime: '10:00',
    expectedReturnDate: new Date(Date.now() + 3 * 86400000).toISOString().slice(0, 10) + 'T12:00:00',
    deliveryMethod: 'SelfPickup',
    returnMethod: 'SelfDropoff',
    paymentMethod: 'Cash',
    paymentStatus: 'Pending',
    status: 'Active',
    itemAddress: 'רחוב לדוגמה 5',
    itemCity: 'בני ברק',
    deliveryFee: 0,
    rentalPrice: 500,
  },
]

// הדגמת העברה בנקאית: /order/demo?pay=bank&role=renter  או  &role=owner
const DEMO_BANK_LOANS = [
  { ...DEMO_CASH_LOANS[0], id: 'demo-bank', paymentMethod: 'BankTransfer', hasPaymentProof: true },
]

export function OrderPlaced() {
  const { loanId } = useParams()
  const [searchParams] = useSearchParams()
  const { user } = useAuth()
  // /order/demo - דף הדגמה עם נתונים לדוגמה (בלי הזמנה אמיתית), לבדיקת העיצוב. ?stage=Pending|Assigned|OnTheWay|PickedUp|Delivered|Canceled
  const isDemo = !loanId || loanId === 'demo'
  const demoStage = searchParams.get('stage') || 'OnTheWay'
  const demoPay = isDemo ? searchParams.get('pay') : null
  const demoRole = searchParams.get('role') === 'owner' ? 'owner' : 'renter'
  const [loans, setLoans] = useState(isDemo ? (demoPay === 'cash' ? DEMO_CASH_LOANS : demoPay === 'bank' ? DEMO_BANK_LOANS : DEMO_LOANS) : null)
  const [payBusy, setPayBusy] = useState(false)
  const [payError, setPayError] = useState('')
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [cancelBusy, setCancelBusy] = useState(false)
  const [cancelNotice, setCancelNotice] = useState('')
  const [cancelError, setCancelError] = useState('')
  const [pickBusy, setPickBusy] = useState(false)
  const [pickError, setPickError] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (isDemo) return undefined
    let cancelled = false
    loansApi
      .getOrder(loanId)
      .then((result) => {
        if (!cancelled) setLoans(result)
      })
      .catch((err) => {
        if (!cancelled) setError(err.message || 'לא הצלחנו לטעון את ההזמנה')
      })
    return () => {
      cancelled = true
    }
  }, [loanId, isDemo])

  if (error) {
    return (
      <div className="page loan-action">
        <BrandMark className="loan-action-mark" />
        <h1>ההזמנה לא נמצאה</h1>
        <section className="loan-action-card">
          <p className="state-message" role="alert">
            {error}
          </p>
        </section>
        <Link to="/profile" className="btn btn-primary">
          לאזור האישי
        </Link>
      </div>
    )
  }

  if (!loans) {
    return (
      <div className="page loan-action">
        <Loader label="טוענות את ההזמנה" />
      </div>
    )
  }

  const first = loans[0]
  const isRenter = isDemo ? demoRole === 'renter' : first.borrowerId === user?.id
  const isDelivery = first.deliveryMethod === 'Delivery'
  const isCourierReturn = first.returnMethod === 'CourierPickup'
  const deliveryFee = loans.reduce((sum, l) => sum + (l.deliveryFee ?? 0), 0)
  const returnFee = loans.reduce((sum, l) => sum + (l.returnFee ?? 0), 0)
  const hasLiveTracking = first.outboundTrackable || first.returnTrackable

  // תשלום במזומן (אפשרי רק באיסוף עצמי): הסכום, והאם כבר סומן שהתקבל
  const isCash = first.paymentMethod === 'Cash'
  const isBank = first.paymentMethod === 'BankTransfer'
  const cashTotal = loans.reduce((sum, l) => sum + (l.rentalPrice ?? 0), 0)
  // בהעברה בנקאית הסכום כולל גם דמי משלוח (כמו הסכום שהוצג בהזמנה)
  const bankTotal = cashTotal + loans.reduce((sum, l) => sum + (l.deliveryFee ?? 0) + (l.returnFee ?? 0), 0)
  const bankSum = Number(bankTotal).toLocaleString('he-IL', { maximumFractionDigits: 2 })
  const cashPaid = loans.every((l) => l.paymentStatus === 'Paid')
  const cashSum = Number(cashTotal).toLocaleString('he-IL', { maximumFractionDigits: 2 })
  const itemNames = loans.map((l) => l.itemName).join(', ')
  const cancelDeadline = first.cancelDeadline
  const activeLoans = loans.filter((l) => l.status === 'Active')
  const awaitingPickup = activeLoans.filter((l) => !l.pickedUpAt)
  const pickedAt = activeLoans.find((l) => l.pickedUpAt)?.pickedUpAt
  const canCancelNow = Boolean(cancelDeadline) && wallMs(cancelDeadline) >= wallMs(israelNowWall())

  async function handlePickedUp() {
    setPickBusy(true)
    setPickError('')
    try {
      const pending = loans.filter((l) => l.status === 'Active' && !l.pickedUpAt)
      if (!isDemo) {
        for (const l of pending) await loansApi.markPickedUp(l.id)
      }
      const now = new Date().toISOString()
      setLoans(loans.map((l) => (pending.some((p) => p.id === l.id) ? { ...l, pickedUpAt: now, cancelDeadline: null } : l)))
    } catch (err) {
      setPickError(err.message || 'לא הצלחנו לסמן')
    } finally {
      setPickBusy(false)
    }
  }

  async function handleCancel() {
    setCancelBusy(true)
    setCancelError('')
    try {
      if (isDemo) {
        setCancelNotice('ההשכרה בוטלה (הדגמה).')
      } else {
        const result = await loansApi.cancel(first.id)
        setCancelNotice(`ההשכרה בוטלה. ${result.refundNote}`)
        setLoans(loans.map((l) => ({ ...l, status: 'Cancelled', cancelDeadline: null })))
      }
      setConfirmCancel(false)
    } catch (err) {
      setCancelError(err.message)
      setConfirmCancel(false)
    } finally {
      setCancelBusy(false)
    }
  }

  async function handleMarkPaid() {
    setPayBusy(true)
    setPayError('')
    try {
      if (!isDemo) {
        for (const l of loans) {
          if (l.paymentStatus !== 'Paid') await loansApi.confirmPayment(l.id)
        }
      }
      setLoans(loans.map((l) => ({ ...l, paymentStatus: 'Paid' })))
    } catch (err) {
      setPayError(err.message || 'לא הצלחנו לסמן את התשלום')
    } finally {
      setPayBusy(false)
    }
  }

  return (
    <div className="page loan-action order-placed">
      <BrandMark className="loan-action-mark" />
      <h1>ההזמנה בוצעה!</h1>
      {isRenter && first.status === 'Active' && first.isLocation && (
        <aside className="memo-note memo-green" aria-label="מה עכשיו?">
          <p className="memo-note-title">מה עכשיו?</p>
          <ul>
            <li>✓ שלחנו לך מייל עם כל פרטי ההזמנה</li>
            <li>⏰ נזכיר לך לפני הצילום</li>
            <li>📍 הכתובת המלאה מופיעה למטה</li>
            <li>🧹 בסיום - לפנות את המקום בזמן ומסודר</li>
          </ul>
        </aside>
      )}
      {isRenter && first.status === 'Active' && !first.pickedUpAt && !first.isLocation && (
        <aside className="memo-note memo-green" aria-label="מה עכשיו?">
          <p className="memo-note-title">מה עכשיו?</p>
          <ul>
            <li>✓ שלחנו לך מייל עם כל פרטי ההזמנה</li>
            <li>⏰ נזכיר לך לפני מועד {isDelivery ? 'המשלוח' : 'האיסוף'}</li>
            <li>📦 כשהפריט אצלך - סמני כאן ש{isDelivery ? 'הגיע' : 'אספת'}</li>
            <li>↩ לפני סיום ההשכרה נשלח תזכורת להחזרה</li>
          </ul>
        </aside>
      )}
      {isDemo && (
        <p className="state-message" role="note">
          זהו דף הדגמה עם נתונים לדוגמה - לא הזמנה אמיתית. אפשר לראות שלבים שונים:{' '}
          {Object.entries(DEMO_STAGES).map(([stage, label]) => (
            <Link key={stage} to={`/order/demo?stage=${stage}`} className="demo-stage-link">
              {label}
            </Link>
          ))}
          <br />
          תשלום במזומן:{' '}
          <Link to="/order/demo?pay=cash&role=renter" className="demo-stage-link">
            מבט שוכרת
          </Link>
          <Link to="/order/demo?pay=cash&role=owner" className="demo-stage-link">
            מבט משכירה
          </Link>
          <br />
          העברה בנקאית:{' '}
          <Link to="/order/demo?pay=bank&role=renter" className="demo-stage-link">
            מבט שוכרת
          </Link>
          <Link to="/order/demo?pay=bank&role=owner" className="demo-stage-link">
            מבט משכירה
          </Link>
        </p>
      )}

      {isCash && !isDelivery && cashTotal > 0 && (
        <section className="cash-callout" aria-label="תשלום במזומן" role={cashPaid ? 'status' : 'note'}>
          {cashPaid ? (
            <p className="cash-callout-text">✓ התשלום במזומן התקבל ({cashSum} ₪)</p>
          ) : isRenter ? (
            <p className="cash-callout-text">
              עלייך לשלם <strong>{cashSum} ₪</strong> במזומן למשכירה בעת קבלת הפריט
            </p>
          ) : (
            <>
              <p className="cash-callout-text">
                בעת הגעת השוכרת לאסוף את {itemNames}, ודאי שהיא משלמת <strong>{cashSum} ₪</strong> במזומן
              </p>
              <button type="button" className="cash-callout-link" onClick={handleMarkPaid} disabled={payBusy}>
                {payBusy ? 'מסמנת...' : 'לחצי כאן לסימון שהתשלום התקבל'}
              </button>
              {payError && (
                <p className="form-error" role="alert">
                  {payError}
                </p>
              )}
            </>
          )}
        </section>
      )}

      {isBank && bankTotal > 0 && (
        <section className="cash-callout" aria-label="העברה בנקאית" role={cashPaid ? 'status' : 'note'}>
          {cashPaid ? (
            <p className="cash-callout-text">✓ ההעברה הבנקאית אושרה ({bankSum} ₪)</p>
          ) : isRenter ? (
            <p className="cash-callout-text">✓ שולם בהעברה בנקאית ({bankSum} ₪)</p>
          ) : (
            <>
              <p className="cash-callout-text">
                השוכרת העבירה <strong>{bankSum} ₪</strong> לחשבונך עבור {itemNames}. לאחר שוידאת שהסכום נכנס לחשבון:
              </p>
              <div className="cash-callout-actions">
                {!isDemo && first.hasPaymentProof && <ProofButton loanId={first.id} />}
                <button type="button" className="cash-callout-link" onClick={handleMarkPaid} disabled={payBusy}>
                  {payBusy ? 'מסמנת...' : 'לחצי כאן לסימון שהתשלום התקבל'}
                </button>
              </div>
              {payError && (
                <p className="form-error" role="alert">
                  {payError}
                </p>
              )}
            </>
          )}
        </section>
      )}

      {activeLoans.length > 0 && !first.isLocation && (
        <section className="loan-action-card" aria-label="איסוף הפריט">
          {awaitingPickup.length > 0 ? (
            <>
              <p>
                {isRenter
                  ? first.deliveryMethod === 'Delivery'
                    ? 'כשהפריט מגיע אלייך - סמני כאן.'
                    : 'כשאספת את הפריט - סמני כאן.'
                  : 'כשהפריט נאסף ממך (על ידי השוכרת או השליח) - סמני כאן.'}
              </p>
              <button type="button" className="btn btn-primary" onClick={handlePickedUp} disabled={pickBusy}>
                {pickBusy
                  ? 'מסמנת...'
                  : isRenter
                    ? first.deliveryMethod === 'Delivery'
                      ? 'הפריט הגיע אליי'
                      : 'אספתי את הפריט'
                    : 'הפריט נאסף'}
              </button>
              {pickError && (
                <p className="form-error" role="alert">
                  {pickError}
                </p>
              )}
            </>
          ) : (
            <p className="loan-action-done">✓ סומן שהפריט נאסף{pickedAt ? ` (${formatDateTime(pickedAt)})` : ''}</p>
          )}
        </section>
      )}

      <section className="loan-action-card" aria-label="פרטי ההזמנה">
        <ul className="order-items">
          {loans.map((l) => (
            <li key={l.id}>
              {l.itemName} <LoanStatusBadge status={l.status} />
            </li>
          ))}
        </ul>
        <dl className="item-meta">
          <div>
            <dt>תחילת ההשכרה</dt>
            <dd>
              {formatDate(first.startDate)}
              {first.startTime ? ` · ${first.startTime}` : ''}
            </dd>
          </div>
          <div>
            <dt>{first.isLocation ? 'סיום' : 'החזרה'}</dt>
            <dd>{formatDateTime(first.expectedReturnDate)}</dd>
          </div>
          <div hidden={first.isLocation}>
            <dt>קבלת הפריט</dt>
            <dd>{isDelivery ? (first.expressDelivery && String(first.startDate).slice(0, 10) <= String(first.loanDate ?? '').slice(0, 10) ? 'משלוח אקספרס' : 'משלוח') : 'איסוף עצמי'}</dd>
          </div>
          <div hidden={first.isLocation}>
            <dt>החזרת הפריט</dt>
            <dd>{isCourierReturn ? 'איסוף שליח' : 'מסירה עצמית'}</dd>
          </div>
          {deliveryFee > 0 && (
            <div>
              <dt>דמי משלוח</dt>
              <dd>{formatMoney(deliveryFee)}</dd>
            </div>
          )}
          {returnFee > 0 && (
            <div>
              <dt>איסוף החזרה בשליח</dt>
              <dd>{formatMoney(returnFee)}</dd>
            </div>
          )}
          <div>
            <dt>תשלום</dt>
            <dd>
              {paymentMethodLabel(first.paymentMethod)} ·{' '}
              {first.paymentMethod === 'BankTransfer' && (isRenter || first.paymentStatus === 'Paid')
                ? 'שולם'
                : first.paymentMethod === 'BankTransfer'
                  ? 'שולם, ממתין לאישורך'
                  : paymentStatusLabel(first.paymentStatus)}
            </dd>
          </div>
        </dl>
      </section>

      {!isDelivery && first.itemAddress && (
        <section className="loan-action-card" aria-label="פרטי האיסוף">
          <h2 className="track-card-title">
            {first.isLocation ? 'כתובת המקום' : isRenter ? 'כתובת האיסוף' : 'כתובת האיסוף (אצלך)'}
          </h2>
          <p>
            {first.itemAddress}
            {first.itemCity ? `, ${first.itemCity}` : ''}
          </p>
          {isRenter ? (
            (first.itemAddressNotes || first.startDate) && (
              <aside className="memo-note memo-pink" aria-label="הערה מהמשכירה">
                {first.itemAddressNotes && (
                  <>
                    <p className="memo-note-title">📌 הערה מהמשכירה</p>
                    <p>{first.itemAddressNotes}</p>
                  </>
                )}
                {first.startDate && (
                  <p className="memo-note-meta">
                    איסוף: {formatDate(first.startDate)}
                    {first.startTime ? ` בשעה ${first.startTime}` : ''}
                  </p>
                )}
              </aside>
            )
          ) : (
            first.itemAddressNotes && <p className="payment-status-note">{first.itemAddressNotes}</p>
          )}
        </section>
      )}

      {(isDelivery || isCourierReturn) && (
        <section className="loan-action-card" aria-label="מעקב משלוח">
          <h2 className="track-card-title">מעקב משלוח וזמן הגעה</h2>
          {hasLiveTracking ? (
            <ShipmentPanel loan={first} demoStage={isDemo ? demoStage : undefined} />
          ) : (
            <p className="state-message">
              {isDelivery
                ? 'פרטי המשלוח - זמן הגעה ושליח - יופיעו כאן ברגע שיהיו זמינים.'
                : 'פרטי איסוף ההחזרה יופיעו כאן ברגע שיהיו זמינים.'}
            </p>
          )}
          {isCourierReturn && (
            <p className="payment-status-note">
              {isRenter
                ? 'בדרך חזור שליח יאסוף את הפריט מהכתובת שבחרת לאיסוף ההחזרה ביום ההחזרה ויחזיר אותו למשכירה.'
                : 'בדרך חזור שליח יאסוף את הפריט מהשוכרת ביום ההחזרה ויחזיר אותו אלייך.'}
            </p>
          )}
        </section>
      )}

      {!isDemo && isRenter && first.status === 'Active' && (
        <p>
          <Link to={`/order/${first.id}/return`}>
            {first.returnMethod === 'CourierPickup' ? 'שינוי לאיסוף עצמי / החלפת כתובת האיסוף' : 'שינוי לאיסוף במשלוח'}
          </Link>
        </p>
      )}

      {cancelNotice && (
        <p className="form-success" role="status">
          {cancelNotice}
        </p>
      )}
      {cancelError && (
        <p className="form-error" role="alert">
          {cancelError}
        </p>
      )}
      {canCancelNow && (
        <p>
          אפשר לבטל את ההשכרה עד {formatDateTime(cancelDeadline)} (24 שעות לפני תחילתה).{' '}
          <button type="button" className="btn btn-ghost btn-small" onClick={() => setConfirmCancel(true)}>
            ביטול ההשכרה
          </button>
        </p>
      )}
      {confirmCancel && (
        <ConfirmDialog
          title="ביטול השכרה"
          message={
            isRenter
              ? 'לבטל את ההשכרה? בתשלום באשראי ההחזר מבוצע על ידי ההנהלה בימים הקרובים.'
              : 'לבטל את ההשכרה? השוכרת תקבל מייל והחזר מלא, והפריט יחזור להיות זמין.'
          }
          confirmLabel="ביטול ההשכרה"
          cancelLabel="לא לבטל"
          busy={cancelBusy}
          onConfirm={handleCancel}
          onCancel={() => setConfirmCancel(false)}
        />
      )}

      <CallPartyButton loan={first} isRenter={isRenter} demoNumber={isDemo ? '03-5555555' : undefined} />

      <div className="track-actions">
        <Link to="/profile" className="btn btn-primary">
          לאזור האישי
        </Link>
        <Link to="/" className="btn btn-ghost">
          לקטלוג הציוד
        </Link>
      </div>
    </div>
  )
}
