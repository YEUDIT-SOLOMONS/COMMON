import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { commissionPayApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { formatMoney } from '../utils/format.js'

// עמוד "התשלום התקבל": סאמיט מחזירה לכאן אחרי תשלום מוצלח (עם og-paymentid ו-og-externalidentifier בכתובת).
// השרת מאמת מול סאמיט שהתשלום תקף ובסכום המלא, ורק אז מסמן את הדוח כשולם.
export function CommissionPaid() {
  const [searchParams] = useSearchParams()
  const paymentId = searchParams.get('og-paymentid') ?? ''
  const externalIdentifier = searchParams.get('og-externalidentifier') ?? ''
  const [state, setState] = useState('checking') // checking | paid | unverified
  const [result, setResult] = useState(null)
  const started = useRef(false)

  useEffect(() => {
    if (started.current) return
    started.current = true
    if (!paymentId) {
      setState('unverified')
      return
    }
    commissionPayApi
      .confirm(paymentId, externalIdentifier)
      .then((r) => {
        setResult(r)
        setState(r.paid ? 'paid' : 'unverified')
      })
      .catch(() => setState('unverified'))
  }, [paymentId, externalIdentifier])

  if (state === 'checking') {
    return (
      <div className="page loan-action">
        <Loader label="מאמתות את התשלום" />
      </div>
    )
  }

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      {state === 'paid' ? (
        <>
          <h1>התשלום התקבל, תודה!</h1>
          <section className="loan-action-card">
            <p className="loan-action-done">
              ✓ עמלת {result?.monthLabel} ({formatMoney(result?.amount)}) שולמה. חשבונית/קבלה נשלחת אלייך במייל.
            </p>
          </section>
        </>
      ) : (
        <>
          <h1>קיבלנו את הפנייה</h1>
          <section className="loan-action-card">
            <p className="state-message">
              לא הצלחנו לאמת את התשלום אוטומטית. אם שילמת, אין צורך לשלם שוב - נעדכן את הסטטוס בהקדם. אפשר גם להשיב למייל
              הדוח ונטפל.
            </p>
          </section>
        </>
      )}
      <Link to="/" className="btn btn-primary">
        לקטלוג הציוד
      </Link>
    </div>
  )
}
