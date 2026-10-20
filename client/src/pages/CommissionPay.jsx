import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { commissionPayApi, commissionProofApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { formatMoney } from '../utils/format.js'

// "לתשלום באשראי" במייל דוח העמלה (בלי התחברות; הקישור מוגן בטוקן הסודי של הדוח).
// לפני המעבר לדף התשלום המאובטח של סאמיט חובה לאשר שמירת הכרטיס וחיוב חודשי אוטומטי - בלי סימון אי אפשר להמשיך.
// דף התשלום נוצר בכל כניסה מחדש, ולכן הקישור שבמייל לא פג.
export function CommissionPay() {
  const [searchParams] = useSearchParams()
  const ownerId = searchParams.get('o') ?? ''
  const month = searchParams.get('m') ?? ''
  const token = searchParams.get('t') ?? ''

  const [view, setView] = useState(null)
  const [loadError, setInvalid] = useState('')
  const linkMissing = !ownerId || !month || !token
  const invalid = linkMissing ? 'הקישור חסר או שגוי' : loadError
  const [consent, setConsent] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [emailState, setEmailState] = useState('idle')

  useEffect(() => {
    if (linkMissing) return undefined
    let cancelled = false
    commissionProofApi
      .get(ownerId, month, token)
      .then((result) => {
        if (!cancelled) setView(result)
      })
      .catch((err) => {
        if (!cancelled) setInvalid(err.message || 'הקישור אינו תקף')
      })
    return () => {
      cancelled = true
    }
  }, [ownerId, month, token, linkMissing])

  async function handleContinue() {
    setBusy(true)
    setError('')
    try {
      const result = await commissionPayApi.start(ownerId, month, token, consent)
      window.location.assign(result.redirectUrl)
    } catch (err) {
      setError(err.message || 'לא הצלחנו לפתוח את דף התשלום')
      setBusy(false)
    }
  }

  async function handleEmail() {
    setEmailState('sending')
    setError('')
    try {
      await commissionPayApi.email(ownerId, month, token)
      setEmailState('sent')
    } catch (err) {
      setError(err.message || 'לא הצלחנו לשלוח את המייל')
      setEmailState('idle')
    }
  }

  if (invalid) {
    return (
      <div className="page loan-action">
        <h1>הקישור אינו תקף</h1>
        <p className="state-message">{invalid}. אפשר לשלם בהעברה בנקאית (הפרטים במייל הדוח), או להשיב למייל ונעזור.</p>
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

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>תשלום עמלה באשראי</h1>
      <p className="loan-action-sub">
        דוח עמלה לחודש {view.monthLabel} · {formatMoney(view.amount)}
      </p>

      {error && <p className="form-error">{error}</p>}

      <section className="loan-action-card">
        {view.paid ? (
          <p className="loan-action-done">✓ הדוח הזה כבר שולם. תודה!</p>
        ) : (
          <>
            <label className="commission-consent">
              <input
                type="checkbox"
                checked={consent}
                onChange={(e) => setConsent(e.target.checked)}
                aria-describedby="commission-consent-text"
              />
              <span id="commission-consent-text">
                אני מאשרת חיוב הכרטיס אוטומטית, מדי חודש, בסכום עמלת השירות החודשית של common (בהתאם ל
                <Link to="/terms">תנאי השימוש</Link>), בעת שליחת הדוח החודשי. חשבונית/קבלה תישלח אליי במייל. פרטי הכרטיס אינם נשמרים
                באתר COMMON אלא מאובטחים אצל חברת הסליקה סאמיט.
              </span>
            </label>
            <button type="button" className="btn btn-primary" disabled={!consent || busy} onClick={handleContinue}>
              {busy ? 'מעבירות לדף התשלום...' : 'המשך לתשלום מאובטח'}
            </button>
            {emailState === 'sent' ? (
              <p className="form-success" role="status">
                ✓ הדוח החודשי ובו פרטי חשבון להעברה בנקאית נשלח אלייך בהצלחה
              </p>
            ) : (
              <p className="field-hint">
                אפשר גם לשלם בהעברה בנקאית{' '}
                <button type="button" className="link-button" onClick={handleEmail} disabled={emailState === 'sending'}>
                  {emailState === 'sending' ? 'שולחות...' : 'שלחו לי מייל'}
                </button>
              </p>
            )}
          </>
        )}
      </section>
    </div>
  )
}
