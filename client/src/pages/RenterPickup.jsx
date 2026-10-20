import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { renterPickupApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'

export function RenterPickup() {
  const { loanId } = useParams()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('t') ?? ''

  const [view, setView] = useState(null)
  const [error, setError] = useState('')
  const [invalid, setInvalid] = useState(false)
  const [busy, setBusy] = useState(false)
  const [justMarked, setJustMarked] = useState(false)

  useEffect(() => {
    renterPickupApi
      .get(loanId, token)
      .then(setView)
      .catch((err) => {
        setInvalid(true)
        setError(err.message)
      })
  }, [loanId, token])

  async function mark() {
    setBusy(true)
    setError('')
    try {
      const updated = await renterPickupApi.mark(loanId, token)
      setJustMarked(updated?.pickedUpBy === 'Renter')
      setView(updated)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  if (invalid) {
    return (
      <div className="page loan-action">
        <h1>הקישור אינו תקף</h1>
        <p className="state-message">{error || 'אפשר להיכנס לאזור האישי כדי לראות את ההשכרה.'}</p>
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

  const delivery = view.deliveryMethod === 'Delivery'

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>קיבלת את {view.itemName}?</h1>

      {error && <p className="form-error">{error}</p>}

      <section className="loan-action-card">
        {view.pickedUpAt ? (
          <p className="loan-action-done">
            {justMarked
              ? `✓ תודה! סומן ש${delivery ? 'הפריט הגיע אלייך' : 'אספת את הפריט'}. בהצלחה!`
              : view.pickedUpBy === 'Renter'
                ? '✓ כבר סימנת שהפריט נאסף. בהצלחה!'
                : '✓ המשכירה כבר סימנה שהפריט נאסף. בהצלחה!'}
          </p>
        ) : view.status !== 'Active' ? (
          <p className="state-message">ההשכרה הזו כבר אינה פעילה.</p>
        ) : (
          <button type="button" className="btn btn-primary" disabled={busy} onClick={mark}>
            {busy ? 'מסמנת...' : delivery ? 'הפריט הגיע אליי' : 'אספתי את הפריט'}
          </button>
        )}
      </section>

      <p className="field-hint">
        <Link to={`/order/${loanId}`}>פרטי ההזמנה</Link>
      </p>
    </div>
  )
}
