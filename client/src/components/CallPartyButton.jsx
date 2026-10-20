import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { callsApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'

let infoPromise = null

function loadCallInfo() {
  if (!infoPromise) {
    infoPromise = callsApi
      .commonNumber()
      .then((r) => ({ number: r?.number ?? null, callback: Boolean(r?.callback) }))
      .catch(() => {
        infoPromise = null
        return { number: null, callback: false }
      })
  }
  return infoPromise
}

function PhoneIcon() {
  return (
    <svg className="call-party-icon" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M6.6 2.5 4.2 3.2c-.9.3-1.4 1.2-1.2 2.1 1.1 5.2 5.4 9.5 10.6 10.6.9.2 1.8-.3 2.1-1.2l.7-2.4c.1-.5-.1-1-.6-1.2l-2.7-1.1c-.4-.2-.9-.1-1.2.3l-.8 1a8.8 8.8 0 0 1-3.8-3.8l1-.8c.4-.3.5-.8.3-1.2L7.8 3.1c-.2-.4-.7-.7-1.2-.6Z"
      />
    </svg>
  )
}

export function callAllowed(loan) {
  return loan.paymentStatus === 'Paid' || loan.paymentMethod === 'BankTransfer' || loan.paymentMethod === 'Cash'
}

export function CallPartyButton({ loan, isRenter, demoNumber }) {
  const { user } = useAuth()
  const [info, setInfo] = useState(demoNumber ? { number: demoNumber, callback: true } : { number: null, callback: false })
  const [open, setOpen] = useState(false)
  const [calling, setCalling] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState('')
  const live = loan.status === 'Active' || loan.status === 'Overdue'
  const allowed = callAllowed(loan)

  useEffect(() => {
    if (demoNumber || !live) return undefined
    let cancelled = false
    loadCallInfo().then((i) => {
      if (!cancelled) setInfo(i)
    })
    return () => {
      cancelled = true
    }
  }, [live, demoNumber])

  const { number, callback } = info
  if (!live || !allowed || (!number && !callback)) return null

  const other = isRenter ? 'המשכירה' : 'השוכרת'
  const tel = number ? `tel:${String(number).replace(/[^\d+]/g, '')}` : null

  async function handleCall() {
    setCalling(true)
    setError('')
    try {
      if (!demoNumber) await callsApi.connect(loan.id)
      setSent(true)
    } catch (err) {
      setError(err.message || 'לא הצלחנו לחייג')
    } finally {
      setCalling(false)
    }
  }

  return (
    <div className="call-party">
      <button
        type="button"
        className="call-party-btn"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        <PhoneIcon />
        שיחה עם {other}
      </button>
      {open && (
        <div className="call-party-panel" role="region" aria-label={`שיחה עם ${other}`}>
          {callback ? (
            <>
              <p className="call-party-lead">
                נתקשר אלייך
                {user?.phone ? (
                  <>
                    {' '}
                    למספר <span dir="ltr">{user.phone}</span>
                  </>
                ) : null}{' '}
                ונחבר אותך אל {other}.
              </p>
              {sent ? (
                <p className="call-party-dial">מתקשרים אלייך עכשיו. יש לענות לשיחה.</p>
              ) : (
                <button type="button" className="btn btn-primary btn-small" onClick={handleCall} disabled={calling}>
                  {calling ? 'מחייגים...' : 'התקשרו אליי'}
                </button>
              )}
              {error && (
                <p className="form-error" role="alert">
                  {error}
                </p>
              )}
            </>
          ) : null}
          {number && (
            <>
              <p className="call-party-lead">
                {callback ? 'אפשר גם לחייג' : `החיבור אל ${other} מתבצע אוטומטית. יש לחייג`} מהמספר איתו נרשמת ב-common
                {user?.phone ? (
                  <>
                    {' - '}
                    <span dir="ltr">{user.phone}</span>
                  </>
                ) : null}
              </p>
              <p className="call-party-dial">
                לחיוג:{' '}
                <a className="call-party-number" href={tel} dir="ltr">
                  {number}
                </a>
              </p>
            </>
          )}
          <p className="payment-status-note">
            אם יש בעיה, אפשר לפנות להנהלת האתר בדף <Link to="/contact" className="call-party-link">צור קשר</Link>.
          </p>
        </div>
      )}
    </div>
  )
}
