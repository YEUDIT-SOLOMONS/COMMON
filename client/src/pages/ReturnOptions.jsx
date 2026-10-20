import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { loansApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { CityStreetFields } from '../components/AddressFields.jsx'
import { Loader } from '../components/Loader.jsx'
import { useAuth } from '../context/AuthContext.jsx'
import { formatDateTime } from '../utils/format.js'

// שינוי אופן ההחזרה באמצע ההשכרה - מסירה עצמית <-> איסוף שליח (עם כתובת איסוף לבחירה).
// נפתח מהמיילים (אישור ההשכרה והתזכורת), מהאזור האישי ומדף ההזמנה. אפשרי עד שהשליח יוצא לאסוף.
export function ReturnOptions() {
  const { loanId } = useParams()
  const { user } = useAuth()
  const [loans, setLoans] = useState(null)
  const [loadError, setLoadError] = useState('')
  const [method, setMethod] = useState('SelfDropoff')
  const [city, setCity] = useState('')
  const [street, setStreet] = useState('')
  const [houseNumber, setHouseNumber] = useState('')
  const [floor, setFloor] = useState('')
  const [apartment, setApartment] = useState('')
  const [addressValid, setAddressValid] = useState(false)
  const [price, setPrice] = useState(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState('')

  useEffect(() => {
    let cancelled = false
    loansApi
      .getOrder(loanId)
      .then((result) => {
        if (cancelled) return
        setLoans(result)
        setMethod(result[0].returnMethod)
      })
      .catch((err) => {
        if (!cancelled) setLoadError(err.message || 'לא הצלחנו לטעון את ההזמנה')
      })
    return () => {
      cancelled = true
    }
  }, [loanId])

  const itemIdForQuote = loans?.[0]?.itemId
  const addressReady = Boolean(city.trim() && street.trim() && houseNumber.trim() && addressValid)

  useEffect(() => {
    if (!itemIdForQuote || method !== 'CourierPickup' || !addressReady) {
      setPrice(null)
      return undefined
    }
    let cancelled = false
    const timer = setTimeout(() => {
      loansApi
        .getShippingQuote({
          itemId: itemIdForQuote,
          deliveryAddress: { city: city.trim(), street: street.trim(), houseNumber: houseNumber.trim() },
        })
        .then((q) => !cancelled && setPrice(q.returnPrice ?? q.regularPrice))
        .catch(() => !cancelled && setPrice(null))
    }, 500)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [itemIdForQuote, method, addressReady, city, street, houseNumber])

  if (loadError) {
    return (
      <div className="page loan-action">
        <BrandMark className="loan-action-mark" />
        <h1>ההזמנה לא נמצאה</h1>
        <section className="loan-action-card">
          <p className="state-message" role="alert">
            {loadError}
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
  const current = first.returnMethod
  const isRenter = first.borrowerId === user?.id
  const active = first.status === 'Active'
  const wantsCourier = method === 'CourierPickup'
  const addressComplete = Boolean(city.trim() && street.trim() && houseNumber.trim() && addressValid && floor.trim() && apartment.trim())
  // אין שינוי כשנשארים במסירה עצמית; בשליח - תמיד אפשר להזין כתובת (גם החלפת כתובת)
  const unchanged = method === current && !wantsCourier
  const canSubmit = isRenter && active && !busy && !unchanged && (!wantsCourier || addressComplete)

  async function handleSubmit(e) {
    e.preventDefault()
    if (!canSubmit) return
    setBusy(true)
    setError('')
    setDone('')
    try {
      const updated = await loansApi.changeReturnMethod(first.id, {
        returnMethod: method,
        returnPickupAddress: wantsCourier
          ? {
              city: city.trim(),
              street: street.trim(),
              houseNumber: houseNumber.trim(),
              floor: floor.trim(),
              apartment: apartment.trim(),
            }
          : undefined,
      })
      setLoans(loans.map((l) => ({ ...l, returnMethod: updated.returnMethod, returnTrackable: updated.returnTrackable, returnTrackingId: updated.returnTrackingId })))
      setDone(
        updated.returnMethod === 'CourierPickup'
          ? 'עודכן! שליח יאסוף את הפריט בהחזרה. מועד האיסוף והשליח יופיעו במסך ההזמנה, ושלחנו אלייך מייל.'
          : 'עודכן! את מחזירה את הפריט בעצמך למשכירה. האיסוף בשליח בוטל, ושלחנו אלייך מייל.',
      )
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>שינוי אופן ההחזרה</h1>

      <section className="loan-action-card" aria-label="פרטי ההשכרה">
        <p>
          {loans.map((l) => l.itemName).join(', ')}
          <br />
          <span className="payment-status-note">החזרה עד {formatDateTime(first.expectedReturnDate)}</span>
        </p>
        <p>
          אופן ההחזרה כרגע: <strong>{current === 'CourierPickup' ? 'איסוף שליח' : 'מסירה עצמית למשכירה'}</strong>
        </p>
      </section>

      {!isRenter ? (
        <p className="state-message">רק השוכרת יכולה לשנות את אופן ההחזרה.</p>
      ) : !active ? (
        <p className="state-message">ההשכרה הסתיימה, ולכן אי אפשר לשנות את אופן ההחזרה.</p>
      ) : (
        <form className="loan-action-card return-options-form" onSubmit={handleSubmit}>
          <fieldset className="delivery-choice-block">
            <legend>איך תחזירי את הפריט?</legend>
            <div className="delivery-choice">
              <label>
                <input
                  type="radio"
                  name="returnMethod"
                  value="SelfDropoff"
                  checked={method === 'SelfDropoff'}
                  onChange={() => setMethod('SelfDropoff')}
                />
                מסירה עצמית למשכיר
              </label>
              <label>
                <input
                  type="radio"
                  name="returnMethod"
                  value="CourierPickup"
                  checked={method === 'CourierPickup'}
                  onChange={() => setMethod('CourierPickup')}
                />
                איסוף שליח
              </label>
            </div>
          </fieldset>

          {wantsCourier && (
            <fieldset className="delivery-address-fields">
              <legend>כתובת לאיסוף השליח</legend>
              <div className="rent-dates-row">
                <CityStreetFields
                  idPrefix="changeReturn"
                  city={city}
                  street={street}
                  onCityChange={setCity}
                  onStreetChange={setStreet}
                  onValidChange={setAddressValid}
                />
              </div>
              <div className="rent-dates-row">
                <div>
                  <label htmlFor="changeReturnHouse">מספר בית</label>
                  <input
                    id="changeReturnHouse"
                    type="text"
                    value={houseNumber}
                    onChange={(e) => setHouseNumber(e.target.value)}
                    required
                  />
                </div>
                <div>
                  <label htmlFor="changeReturnFloor">קומה</label>
                  <input id="changeReturnFloor" type="text" value={floor} onChange={(e) => setFloor(e.target.value)} required />
                </div>
              </div>
              <div className="rent-dates-row">
                <div>
                  <label htmlFor="changeReturnApartment">מספר דירה / שם משפחה</label>
                  <input
                    id="changeReturnApartment"
                    type="text"
                    value={apartment}
                    onChange={(e) => setApartment(e.target.value)}
                    required
                  />
                </div>
              </div>
              {price != null && (
                <p className="field-hint">
                  עלות איסוף השליח: <strong>₪{price}</strong>
                </p>
              )}
              <p className="field-hint">השליח יגיע במהלך יום ההחזרה. אין התחייבות לשעה מדויקת.</p>
            </fieldset>
          )}

          <p className="field-hint">אפשר לשנות עד שהשליח יוצא לאסוף את הפריט.</p>

          {error && (
            <p className="form-error" role="alert">
              {error}
            </p>
          )}
          {done && (
            <p className="form-success" role="status">
              {done}
            </p>
          )}

          <button type="submit" className="btn btn-primary" disabled={!canSubmit}>
            {busy ? 'מעדכנת...' : current === 'CourierPickup' && wantsCourier ? 'עדכון כתובת האיסוף' : 'עדכון אופן ההחזרה'}
          </button>
        </form>
      )}

      <div className="track-actions">
        <Link to={`/order/${first.id}`} className="btn btn-ghost">
          למסך ההזמנה
        </Link>
        <Link to="/profile" className="btn btn-ghost">
          לאזור האישי
        </Link>
      </div>
    </div>
  )
}
