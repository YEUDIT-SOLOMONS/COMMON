import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { itemsApi, orderPayApi, salesApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { CategoryIcon } from './CategoryIcon.jsx'
import { categoryTrailForItem, iconTextsForItem } from '../utils/categoryIcon.js'
import { readProofFile } from '../utils/proof.js'
import { MapPinIcon } from './icons.jsx'
import { Modal } from './Modal.jsx'
import { Loader } from './Loader.jsx'
import { CityStreetFields } from './AddressFields.jsx'
import { AskOwner } from './AskOwner.jsx'
import { PhoneUpdateModal } from './PhoneUpdateModal.jsx'

export const CONDITION_LABELS = { New: 'חדש בקופסה', LikeNew: 'כמו חדש - שומש פעמים בודדות', Used: 'משומש', Damaged: 'קצת פגום' }

// חלון פריט יד 2: פרטים, שאלה למוכרת, איסוף/שליח ותשלום (מזומן, העברה בנקאית או אשראי) - כמו בהשכרה
export function BuyModal({ item, categoryChains, onClose }) {
  const { user, isAuthenticated } = useAuth()
  const navigate = useNavigate()
  const [details, setDetails] = useState(null)
  const [galleryIndex, setGalleryIndex] = useState(0)
  const [method, setMethod] = useState('SelfPickup')
  const [city, setCity] = useState('')
  const [street, setStreet] = useState('')
  const [addressValid, setAddressValid] = useState(false)
  const [houseNumber, setHouseNumber] = useState('')
  const [floor, setFloor] = useState('')
  const [apartment, setApartment] = useState('')
  const [fee, setFee] = useState(null)
  const [quoting, setQuoting] = useState(false)
  const [error, setError] = useState('')
  const [paying, setPaying] = useState(false)
  const [paymentMethod, setPaymentMethod] = useState(null)
  const [orderPay, setOrderPay] = useState(null)
  const [ownerPayout, setOwnerPayout] = useState(null)
  const [payoutLoading, setPayoutLoading] = useState(false)
  const [proof, setProof] = useState(null)
  const [proofBusy, setProofBusy] = useState(false)
  const [proofError, setProofError] = useState('')
  const [phoneConfirmed, setPhoneConfirmed] = useState(false)
  const [phoneEditOpen, setPhoneEditOpen] = useState(false)
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [checkout, setCheckout] = useState(false)
  const isOwnItem = user?.id && user.id === item.ownerId

  useEffect(() => {
    itemsApi
      .getById(item.id)
      .then((full) => setDetails(full))
      .catch(() => {})
  }, [item.id])

  useEffect(() => {
    if (!isAuthenticated) return
    orderPayApi
      .status()
      .then(setOrderPay)
      .catch(() => {})
  }, [isAuthenticated])

  // משלוח עם שליח - רק באשראי
  useEffect(() => {
    if (method === 'Delivery' && paymentMethod && paymentMethod !== 'CreditCard') setPaymentMethod(null)
  }, [method, paymentMethod])

  useEffect(() => {
    if (paymentMethod !== 'BankTransfer' || ownerPayout) return
    setPayoutLoading(true)
    itemsApi
      .getOwnerPayout(item.id)
      .then(setOwnerPayout)
      .catch((err) => setError(err.message))
      .finally(() => setPayoutLoading(false))
  }, [paymentMethod, ownerPayout, item.id])

  const address = { city, street, houseNumber, floor, apartment }
  const addressComplete = addressValid && houseNumber.trim() && floor.trim() && apartment.trim()

  useEffect(() => {
    setFee(null)
    if (method !== 'Delivery' || !addressComplete) return undefined
    let cancelled = false
    setQuoting(true)
    setError('')
    const t = setTimeout(() => {
      salesApi
        .quote(item.id, { city, street, houseNumber, floor, apartment })
        .then((r) => !cancelled && setFee(r.fee))
        .catch((err) => !cancelled && setError(err.message))
        .finally(() => !cancelled && setQuoting(false))
    }, 500)
    return () => {
      cancelled = true
      clearTimeout(t)
    }
  }, [method, addressComplete, item.id, city, street, houseNumber, floor, apartment])

  const gallery = details?.imageDataUrls?.length ? details.imageDataUrls : item.imageDataUrl ? [item.imageDataUrl] : []
  const place = details?.neighborhood || details?.street
  const deliveryFee = method === 'Delivery' ? fee : 0
  const total = deliveryFee == null ? null : item.salePrice + deliveryFee
  const canSubmit =
    total != null &&
    termsAccepted &&
    phoneConfirmed &&
    (paymentMethod === 'CreditCard' || paymentMethod === 'Cash' || (paymentMethod === 'BankTransfer' && ownerPayout?.isConfigured && proof))

  async function handleProofChange(e) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setProofError('')
    setProofBusy(true)
    try {
      setProof(await readProofFile(file))
    } catch (err) {
      setProof(null)
      setProofError(err.message)
    } finally {
      setProofBusy(false)
    }
  }

  async function confirmDirect() {
    setError('')
    setPaying(true)
    try {
      const sale = await salesApi.create({
        itemId: item.id,
        deliveryMethod: 'SelfPickup',
        paymentMethod,
        ...(paymentMethod === 'BankTransfer' ? { paymentProofDataUrl: proof?.dataUrl, paymentProofFileName: proof?.fileName } : {}),
      })
      navigate(`/purchase/${sale.id}?t=${sale.token}`)
    } catch (err) {
      setError(err.message)
      setPaying(false)
    }
  }

  async function pay() {
    setError('')
    setPaying(true)
    try {
      const pending = {
        draft: { sale: { itemId: item.id, deliveryMethod: method, paymentMethod: 'CreditCard', deliveryAddress: method === 'Delivery' ? address : null } },
        isBatch: false,
      }
      sessionStorage.setItem('pr.orderDraft', JSON.stringify(pending))
      const r = await orderPayApi.start(total, false, pending)
      window.location.href = r.redirectUrl
    } catch (err) {
      setError(err.message)
      setPaying(false)
    }
  }

  return (
    <Modal title={item.name} eyebrow={categoryTrailForItem(item, categoryChains)} onClose={onClose}>
      <div className="rent-modal">
        {!checkout && (
          <>
        <div className="rent-modal-media">
          {gallery.length > 0 ? (
            <img key={galleryIndex} src={gallery[galleryIndex]} alt={item.name} className="rent-gallery-image" />
          ) : (
            <CategoryIcon className="item-details-icon" texts={iconTextsForItem(item, categoryChains)} />
          )}
          {gallery.length > 1 && (
            <>
              <button type="button" className="rent-gallery-arrow rent-gallery-prev" aria-label="התמונה הקודמת"
                onClick={() => setGalleryIndex((galleryIndex + gallery.length - 1) % gallery.length)}>‹</button>
              <button type="button" className="rent-gallery-arrow rent-gallery-next" aria-label="התמונה הבאה"
                onClick={() => setGalleryIndex((galleryIndex + 1) % gallery.length)}>›</button>
            </>
          )}
        </div>

        {item.city && (
          <span className="item-card-city rent-modal-city">
            <MapPinIcon className="item-card-city-icon" />
            {place ? `${place}, ${item.city}` : item.city}
          </span>
        )}

        <p className="sale-condition">מצב: {CONDITION_LABELS[item.saleCondition] ?? 'משומש'}</p>
        <p className="item-details-desc">{item.description}</p>

          </>
        )}

        <div className="item-price-big">
          <span className="price-tag">₪{item.salePrice}</span>
          <span className="item-price-unit"> · למכירה</span>
        </div>

        {item.sold ? (
          <p className="state-message">הפריט נמכר.</p>
        ) : isOwnItem ? (
          <p className="field-hint">זה הפריט שלך למכירה.</p>
        ) : !isAuthenticated ? (
          <>
            <p className="field-hint">
              כדי לקנות צריך <Link to="/login">להתחבר</Link>.
            </p>
            <AskOwner key={item.id} item={item} />
          </>
        ) : (
          !checkout ? (
            <>
              <AskOwner key={item.id} item={item} />
              <button type="button" className="btn btn-primary sale-buy-btn" onClick={() => setCheckout(true)}>
                אני רוצה לקנות
              </button>
            </>
          ) : (
          <>
            <button type="button" className="link-button sale-back" onClick={() => setCheckout(false)}>
              → חזרה לפרטי הפריט
            </button>
            <ol className="rent-steps" aria-label="שלבי הרכישה">
              {['קבלת הפריט', 'תשלום'].map((label, i) => (
                <li key={label} className={i === 0 ? (paymentMethod ? 'is-done' : 'is-current') : paymentMethod ? 'is-current' : ''}>
                  <span className="rent-step-num">{i + 1}</span>
                  {label}
                </li>
              ))}
            </ol>

            <section className="rent-step" aria-label="קבלת הפריט">
            <h3 className="rent-step-title">1. קבלת הפריט</h3>
            <fieldset className="delivery-choice-block">
              <legend>אופן קבלת הפריט</legend>
              <div className="delivery-choice">
                <label>
                  <input type="radio" name="saleMethod" checked={method === 'SelfPickup'} onChange={() => setMethod('SelfPickup')} />
                  איסוף עצמי
                </label>
                <label>
                  <input type="radio" name="saleMethod" checked={method === 'Delivery'} onChange={() => setMethod('Delivery')} />
                  משלוח{fee != null ? ` (₪${fee})` : ''}
                </label>
              </div>
            </fieldset>

            {method === 'SelfPickup' && <p className="field-hint sale-pickup-note">הכתובת המדויקת תיחשף לך מיד לאחר אישור הקנייה</p>}

            {method === 'Delivery' && (
              <div className="courier-fee-line" role="status">
                <strong>תוספת תשלום לשליח</strong>
                {fee != null ? <span>משלוח: ₪{fee}</span> : <span className={error && !quoting ? 'form-error' : undefined}>{quoting ? 'מחשבים את המחיר...' : error || 'הסכום יופיע מיד אחרי מילוי הכתובת'}</span>}
              </div>
            )}

            {method === 'Delivery' && (
              <fieldset className="delivery-address-fields">
                <legend>כתובת למשלוח</legend>

                <div className="rent-dates-row">
                  <CityStreetFields
                    idPrefix="saleDelivery"
                    city={city}
                    street={street}
                    onCityChange={setCity}
                    onStreetChange={setStreet}
                    onValidChange={setAddressValid}
                  />
                </div>

                <div className="rent-dates-row">
                  <div>
                    <label htmlFor="saleHouse">מספר בית</label>
                    <input id="saleHouse" type="text" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} required />
                  </div>
                  <div>
                    <label htmlFor="saleFloor">קומה</label>
                    <input id="saleFloor" type="text" value={floor} onChange={(e) => setFloor(e.target.value)} required />
                  </div>
                </div>

                <div className="rent-dates-row">
                  <div>
                    <label htmlFor="saleApartment">מספר דירה / שם משפחה</label>
                    <input id="saleApartment" type="text" value={apartment} onChange={(e) => setApartment(e.target.value)} required />
                  </div>
                </div>

                {addressValid && houseNumber.trim() && !addressComplete && (
                  <p className="field-hint">יש להשלים קומה ומספר דירה או שם משפחה, כדי שהשליח ימצא את הדלת.</p>
                )}
                {addressComplete && (
                  <p className="field-hint">{quoting ? 'מחשבים מחיר משלוח אמיתי לפי הכתובת...' : fee != null ? `משלוח: ₪${fee}` : null}</p>
                )}
              </fieldset>
            )}

            </section>

            <section className="rent-step" aria-label="תשלום">
            <h3 className="rent-step-title">2. תשלום</h3>
            <p className="sale-total">
              סה"כ לתשלום: <strong>{total == null ? '—' : `₪${total.toFixed(2)}`}</strong>
              {method === 'Delivery' && fee != null && <span className="item-price-hourly-note"> (כולל ₪{fee} משלוח)</span>}
            </p>

            <fieldset className="delivery-choice-block">
              <legend>אמצעי תשלום</legend>
              <div className="delivery-choice payment-choice">
                {method === 'SelfPickup' && (
                  <label>
                    <input type="radio" name="salePayment" checked={paymentMethod === 'Cash'} onChange={() => setPaymentMethod('Cash')} />
                    מזומן
                  </label>
                )}
                {method === 'SelfPickup' && (
                  <label>
                    <input type="radio" name="salePayment" checked={paymentMethod === 'BankTransfer'} onChange={() => setPaymentMethod('BankTransfer')} />
                    העברה בנקאית
                  </label>
                )}
                {orderPay?.enabled && (
                  <label>
                    <input type="radio" name="salePayment" checked={paymentMethod === 'CreditCard'} onChange={() => setPaymentMethod('CreditCard')} />
                    כרטיס אשראי
                  </label>
                )}
              </div>
            </fieldset>

            {paymentMethod === 'BankTransfer' && (
              <div className="bank-transfer-panel payment-space">
                <h3 className="payment-space-title">תשלום בהעברה בנקאית</h3>
                {payoutLoading && <Loader label="טוען פרטי חשבון" compact />}
                {!payoutLoading && ownerPayout && (ownerPayout.isConfigured ? (
                  <>
                    <p className="payment-space-amount">
                      לתשלום: <strong>₪{item.salePrice.toFixed(2)}</strong>
                    </p>
                    <dl className="item-meta payment-space-details">
                      <div>
                        <dt>בעל/ת החשבון</dt>
                        <dd>{ownerPayout.accountHolderName}</dd>
                      </div>
                      <div>
                        <dt>בנק</dt>
                        <dd>{ownerPayout.bankName} · סניף {ownerPayout.branchNumber}</dd>
                      </div>
                      <div>
                        <dt>מספר חשבון</dt>
                        <dd>{ownerPayout.accountNumber}</dd>
                      </div>
                    </dl>
                    <div className="payment-proof">
                      <label htmlFor="saleProof">אחרי ההעברה - העלי אסמכתא (צילום מסך, PDF או קובץ אקסל)</label>
                      <input id="saleProof" type="file" accept="image/*,application/pdf,.xlsx,.xls,.csv" onChange={handleProofChange} disabled={proofBusy} />
                      {proofBusy && <p className="field-hint">מעלה את האסמכתא...</p>}
                      {proofError && <p className="form-error">{proofError}</p>}
                      {proof && (
                        <p className="payment-proof-ok">
                          ✓ האסמכתא הועלתה: <bdi>{proof.fileName}</bdi>
                        </p>
                      )}
                    </div>
                  </>
                ) : (
                  <p className="state-message state-error">המוכרת טרם הגדירה פרטי חשבון לקבלת תשלומים - יש לבחור אמצעי תשלום אחר.</p>
                ))}
              </div>
            )}

            <label className="checkbox-row">
              <input type="checkbox" checked={phoneConfirmed} onChange={(e) => setPhoneConfirmed(e.target.checked)} />
              <span>
                אני מאשרת שהמספר <bdi dir="ltr">{user?.phone}</bdi> הוא שלי ומתפקד{' '}
                <small className="payment-status-note">(לצורך קבלת עדכונים)</small>{' '}
                <button
                  type="button"
                  className="call-party-link link-button"
                  onClick={(e) => {
                    e.preventDefault()
                    setPhoneEditOpen(true)
                  }}
                >
                  עדכון המספר
                </button>
              </span>
            </label>
            {phoneEditOpen && <PhoneUpdateModal onClose={() => setPhoneEditOpen(false)} onSaved={() => setPhoneConfirmed(true)} />}

            <label className="checkbox-row">
              <input type="checkbox" checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} />
              <span>
                קראתי ואני מאשרת את{' '}
                <Link to="/terms" target="_blank" rel="noopener noreferrer" className="call-party-link">
                  תנאי השימוש
                </Link>
              </span>
            </label>

            {error && <p className="form-error">{error}</p>}
            {paymentMethod === 'CreditCard' ? (
              <button type="button" className="btn btn-primary" disabled={paying || !canSubmit} onClick={pay}>
                {paying ? 'מעבירה לתשלום...' : 'מעבר לתשלום מאובטח באשראי'}
              </button>
            ) : (
              <button type="button" className="btn btn-primary" disabled={paying || !canSubmit} onClick={confirmDirect}>
                {paying ? 'שולח...' : paymentMethod === 'BankTransfer' ? 'שליחת האסמכתא ואישור הרכישה' : 'אישור הרכישה'}
              </button>
            )}
            </section>
          </>
          )
        )}
      </div>
    </Modal>
  )
}
