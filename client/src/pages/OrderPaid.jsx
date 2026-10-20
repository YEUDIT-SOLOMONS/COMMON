import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { loansApi, orderPayApi, salesApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'

const DRAFT_KEY = 'pr.orderDraft'

function readDraft() {
  try {
    return JSON.parse(sessionStorage.getItem(DRAFT_KEY) ?? 'null')
  } catch {
    return null
  }
}

export function OrderPaid() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const paymentId = searchParams.get('og-paymentid') ?? ''
  const externalIdentifier = searchParams.get('og-externalidentifier') ?? ''
  const [state, setState] = useState('checking')
  const [error, setError] = useState('')
  const [confirmedId, setConfirmedId] = useState(null)
  const started = useRef(false)
  const serverDraft = useRef(null)

  const createOrder = useCallback(
    async (transactionId) => {
      const stored = readDraft() ?? serverDraft.current
      if (!stored?.draft) {
        setState('nodraft')
        return
      }
      setState('creating')
      setError('')
      try {
        if (stored.draft.sale) {
          const sale = await salesApi.create({ ...stored.draft.sale, paymentTransactionId: transactionId })
          sessionStorage.removeItem(DRAFT_KEY)
          navigate(`/purchase/${sale.id}?t=${sale.token}`, { replace: true })
          return
        }
        const draft = { ...stored.draft, paymentMethod: 'CreditCard', paymentTransactionId: transactionId }
        const created = stored.isBatch ? await loansApi.createBatch(draft) : await loansApi.create(draft)
        sessionStorage.removeItem(DRAFT_KEY)
        const first = stored.isBatch ? created[0] : created
        navigate(`/order/${first.id}`, { replace: true })
      } catch (err) {
        setError(err.message)
        setState('failed')
      }
    },
    [navigate],
  )

  useEffect(() => {
    if (started.current) return
    started.current = true
    if (!paymentId) {
      setState('unverified')
      return
    }
    orderPayApi
      .confirm(paymentId, externalIdentifier)
      .then((r) => {
        if (!r.paid) {
          setState('unverified')
          return
        }
        if (r.existingLoanId) {
          sessionStorage.removeItem(DRAFT_KEY)
          navigate(`/order/${r.existingLoanId}`, { replace: true })
          return
        }
        try {
          serverDraft.current = r.draft ? { draft: JSON.parse(r.draft), isBatch: r.isBatch } : null
        } catch {
          serverDraft.current = null
        }
        setConfirmedId(r.paymentId)
        createOrder(r.paymentId)
      })
      .catch(() => setState('unverified'))
  }, [paymentId, externalIdentifier, createOrder, navigate])

  if (state === 'checking' || state === 'creating') {
    return (
      <div className="page loan-action">
        <Loader label={state === 'checking' ? 'מאמתות את התשלום' : 'פותחות את ההזמנה'} />
      </div>
    )
  }

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>{state === 'failed' ? 'התשלום התקבל, אבל ההזמנה לא נפתחה' : 'לא הצלחנו להשלים את ההזמנה'}</h1>
      <section className="loan-action-card">
        {state === 'failed' && (
          <>
            <p className="state-message" role="alert">
              {error}
            </p>
            <p className="payment-status-note">התשלום נשמר ואינו מתבזבז: אפשר לנסות שוב בלי לשלם פעם נוספת.</p>
            <button type="button" className="btn btn-primary" onClick={() => createOrder(confirmedId)}>
              ניסיון נוסף
            </button>
          </>
        )}
        {state === 'nodraft' && (
          <p className="state-message">
            התשלום התקבל, אבל פרטי ההזמנה לא נמצאו בדפדפן הזה. אפשר לפנות להנהלת האתר בדף{' '}
            <Link to="/contact">צור קשר</Link> ונסדר את זה.
          </p>
        )}
        {state === 'unverified' && (
          <p className="state-message">
            לא הצלחנו לאמת את התשלום. אם שולם, אין צורך לשלם שוב. אפשר לפנות להנהלת האתר בדף{' '}
            <Link to="/contact">צור קשר</Link>.
          </p>
        )}
      </section>
      <Link to="/" className="btn btn-ghost">
        לקטלוג הציוד
      </Link>
    </div>
  )
}
