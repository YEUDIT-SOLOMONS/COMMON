import { useCallback, useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { loanActionsApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { openDataUrl } from '../utils/proof.js'
import { formatDate, formatDateTime, formatMoney } from '../utils/format.js'

// "חלון הסימון" של המשכירה - נפתח מהקישור במייל (בלי התחברות, מוגן בטוקן סודי בכתובת): מסמנים שהפריט
// נאסף, ושהתשלום התקבל (במזומן), ורואים אם התשלום בוצע בהעברה בנקאית עם קישור לאסמכתא.
export function LoanAction() {
  const { loanId } = useParams()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('t') ?? ''
  const action = searchParams.get('a') ?? ''

  const [view, setView] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')
  const [invalid, setInvalid] = useState(false)

  const load = useCallback(async () => {
    try {
      setView(await loanActionsApi.get(loanId, token))
      setError('')
    } catch (err) {
      setInvalid(true)
      setError(err.message)
    }
  }, [loanId, token])

  useEffect(() => {
    load()
  }, [load])

  const [extensionMessage, setExtensionMessage] = useState('')

  async function runExtension(a) {
    setBusy('ext')
    setError('')
    try {
      const r = await loanActionsApi.extension(loanId, token, a)
      setExtensionMessage(r?.message ?? '')
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy('')
    }
  }

  async function run(name, action) {
    setBusy(name)
    setError('')
    try {
      await action(loanId, token)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy('')
    }
  }

  if (invalid) {
    return (
      <div className="page loan-action">
        <h1>הקישור אינו תקף</h1>
        <p className="state-message">{error || 'הקישור פג תוקף או שגוי. אפשר להיכנס לאזור האישי כדי לנהל את ההשכרה.'}</p>
      </div>
    )
  }

  if (!view) {
    return (
      <div className="page loan-action">
        <Loader label="טוען" />
      </div>
    )
  }

  // "האם X הוחזר?" - הקישורים שבמייל חצי שעה אחרי מועד ההחזרה (a=returned / a=not-returned)
  if (action === 'returned' || action === 'not-returned') {
    return <ReturnWindow view={view} loanId={loanId} token={token} action={action} />
  }

  const isCash = view.paymentMethod === 'Cash'
  const isBank = view.paymentMethod === 'BankTransfer' && view.hasPaymentProof
  const isPaid = view.paymentStatus === 'Paid'
  const startText = `${formatDate(view.startDate)}${view.startTime ? ` בשעה ${view.startTime}` : ''}`

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>חלון סימון</h1>
      <p className="loan-action-sub">
        {view.itemName} · תחילת השכרה {startText} · החזרה צפויה {formatDateTime(view.expectedReturnDate)}
      </p>

      {error && <p className="form-error">{error}</p>}

      <section className="loan-action-card">
        <h2>הפריט נאסף?</h2>
        {view.pickedUpAt ? (
          <p className="loan-action-done">
            {view.pickedUpBy === 'Renter'
              ? '✓ השוכרת כבר סימנה שהפריט נאסף. בהצלחה!'
              : `✓ סומן שהפריט נאסף (${formatDateTime(view.pickedUpAt)})`}
          </p>
        ) : (
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy === 'picked'}
            onClick={() => run('picked', loanActionsApi.pickedUp)}
          >
            {busy === 'picked' ? 'מסמן...' : 'הפריט נאסף'}
          </button>
        )}
      </section>

      {view.extensionStatus === 'Pending' && (
        <section className="loan-action-card">
          <h2>בקשת הארכה</h2>
          <p>
            השוכרת מבקשת להאריך את ההשכרה ב-{view.extensionDays === 1 ? 'יום אחד' : `${view.extensionDays} ימים`}, עד{' '}
            {formatDateTime(view.extensionNewReturn)}.
          </p>
          <div className="loan-action-buttons">
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy === 'ext'}
              onClick={() => runExtension(1)}
            >
              אישור ההארכה
            </button>
            <button
              type="button"
              className="btn btn-ghost"
              disabled={busy === 'ext'}
              onClick={() => runExtension(2)}
            >
              דחיית ההארכה
            </button>
          </div>
        </section>
      )}
      {extensionMessage && (
        <section className="loan-action-card">
          <p className="loan-action-done">✓ {extensionMessage}</p>
        </section>
      )}

      {isBank && (
        <section className="loan-action-card">
          <h2>אסמכתת העברה בנקאית</h2>
          <button
            type="button"
            className="btn btn-ghost"
            disabled={busy === 'proof'}
            onClick={async () => {
              setBusy('proof')
              setError('')
              try {
                const proof = await loanActionsApi.proof(loanId, token)
                openDataUrl(proof.dataUrl, proof.fileName)
              } catch (err) {
                setError(err.message)
              } finally {
                setBusy('')
              }
            }}
          >
            {busy === 'proof' ? 'טוען...' : 'צפייה באסמכתא'}
          </button>
          {!isPaid ? (
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy === 'paid'}
              onClick={() => run('paid', loanActionsApi.paymentReceived)}
            >
              {busy === 'paid' ? 'מאשרת...' : 'אישור אסמכתא'}
            </button>
          ) : (
            <p className="loan-action-done">✓ האסמכתא אושרה</p>
          )}
        </section>
      )}

      {isCash && (
        <section className="loan-action-card">
          <h2>תשלום</h2>
          {!isPaid ? (
            <>
              <div className="loan-action-cash">
                ודאי שהתקבל תשלום במזומן: <strong>{formatMoney(view.amount)}</strong>
              </div>
              <button
                type="button"
                className="btn btn-primary"
                disabled={busy === 'paid'}
                onClick={() => run('paid', loanActionsApi.paymentReceived)}
              >
                {busy === 'paid' ? 'מסמן...' : `התשלום התקבל (${formatMoney(view.amount)})`}
              </button>
            </>
          ) : (
            <p className="loan-action-done">✓ התשלום במזומן התקבל ({formatMoney(view.amount)})</p>
          )}
        </section>
      )}
    </div>
  )
}

// חלון "האם X הוחזר?": הלחיצה על "לא הוחזר" שולחת לשוכרת מייל ושיחת תזכורת (וקנס של יום נוסף על כל יום איחור),
// ומשם אפשר בכל עת לסמן שהפריט הוחזר. כל פעולה דורשת לחיצה על כפתור, ולא מתבצעת בעצם פתיחת הקישור.
function ReturnWindow({ view, loanId, token, action }) {
  const alreadyReturned = view.status === 'Returned'
  const [returned, setReturned] = useState(alreadyReturned)
  const [result, setResult] = useState(null)
  const [reportedBefore] = useState(Boolean(view.notReturnedReportedAt))
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')

  async function markReturned() {
    setBusy('returned')
    setError('')
    try {
      await loanActionsApi.returned(loanId, token)
      setReturned(true)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy('')
    }
  }

  async function markNotReturned() {
    setBusy('not')
    setError('')
    try {
      setResult(await loanActionsApi.notReturned(loanId, token))
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy('')
    }
  }

  const reported = reportedBefore || result?.reported
  const lateDays = result?.lateDays
  const fee = result?.feeSoFar

  if (view.isLocation) {
    return (
      <div className="page loan-action">
        <BrandMark className="loan-action-mark" />
        <h1>{returned ? `הצילום ב${view.itemName} הסתיים` : `הצילום ב${view.itemName} הסתיים?`}</h1>
        <p className="loan-action-sub">
          {view.itemName} · עד {formatDateTime(view.expectedReturnDate)}
        </p>
        {error && <p className="form-error">{error}</p>}
        <section className="loan-action-card">
          {returned ? (
            <p className="loan-action-done">✓ סומן שהצילום הסתיים. תודה!</p>
          ) : (
            <>
              <h2>הכול עבר בסדר והמקום פונה?</h2>
              <button type="button" className="btn btn-primary" disabled={busy === 'returned'} onClick={markReturned}>
                {busy === 'returned' ? 'מסמן...' : 'כן, הצילום הסתיים'}
              </button>
              {action !== 'returned' && (
                <p className="field-hint">
                  אם הצלמת עדיין במקום או שיש בעיה אחרת - כתבי לנו ל-
                  <a href="mailto:common@onyxapp.co.il">common@onyxapp.co.il</a> ונטפל בזה.
                </p>
              )}
            </>
          )}
        </section>
      </div>
    )
  }

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>{returned ? `${view.itemName} הוחזר` : action === 'returned' ? `האם ${view.itemName} הוחזר?` : `${view.itemName} עדיין לא הוחזר?`}</h1>
      <p className="loan-action-sub">
        {view.itemName} · החזרה צפויה {formatDateTime(view.expectedReturnDate)}
      </p>

      {error && <p className="form-error">{error}</p>}

      <section className="loan-action-card">
        {returned ? (
          <p className="loan-action-done">✓ סומן ש-{view.itemName} הוחזר. תודה!</p>
        ) : action === 'returned' ? (
          <>
            <h2>{view.itemName} הוחזר אלייך?</h2>
            <button type="button" className="btn btn-primary" disabled={busy === 'returned'} onClick={markReturned}>
              {busy === 'returned' ? 'מסמן...' : `כן, ${view.itemName} הוחזר`}
            </button>
          </>
        ) : result?.courierHasItem ? (
          <p className="loan-action-done">
            השליח כבר אסף את {view.itemName} מהשוכרת והוא בדרך אלייך, ולכן לא נשלחה תזכורת. כשהפריט יגיע, אפשר לסמן שהוחזר.
          </p>
        ) : reported ? (
          <>
            {result?.callPlaced ? (
              <p className="loan-action-done">שיחת תזכורת יצאה כעת לשוכרת.</p>
            ) : result?.callScheduledFor ? (
              <p className="loan-action-done">
                שיחת התזכורת לשוכרת תצא ב{formatDateTime(result.callScheduledFor)} (בשבת ובחגים לא מתקשרים).
              </p>
            ) : result?.emailSent ? (
              <p className="loan-action-done">מייל תזכורת נשלח כעת לשוכרת.</p>
            ) : (
              <p className="loan-action-done">דיווחת ש-{view.itemName} לא הוחזר, ויצאה תזכורת לשוכרת.</p>
            )}
            <p className="loan-action-cash">
              את תפוצי בסכום על זמן נוסף.
              {lateDays > 0 && (
                <>
                  {' '}
                  עד כה נספרו {lateDays} ימי איחור: <strong>{formatMoney(fee)}</strong> ({formatMoney(result.dailyPrice)} ליום).
                </>
              )}
            </p>
            <p>
              בכל עת ש-{view.itemName} מוחזר,{' '}
              <button type="button" className="btn btn-ghost btn-small" disabled={busy === 'returned'} onClick={markReturned}>
                {busy === 'returned' ? 'מסמן...' : 'לחצי כאן'}
              </button>{' '}
              לסימון.
            </p>
          </>
        ) : (
          <>
            <h2>לסמן ש-{view.itemName} עדיין לא הוחזר?</h2>
            <p className="field-hint">נתקשר ונשלח מייל לשוכרת, ונטפל בכך. איחור מחויב ביום השכרה נוסף על כל יום.</p>
            <button type="button" className="btn btn-primary" disabled={busy === 'not'} onClick={markNotReturned}>
              {busy === 'not' ? 'מדווחת...' : `כן, ${view.itemName} עדיין לא הוחזר`}
            </button>
          </>
        )}
      </section>
    </div>
  )
}
