import { useEffect, useState } from 'react'
import { COMMISSION_PERCENT } from '../utils/format.js'
import { itemsApi, usersApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { CityStreetFields, joinStreetAndNumber, splitStreetAndNumber } from './AddressFields.jsx'
import { ImagePicker } from './ImagePicker.jsx'
import { Modal } from './Modal.jsx'
import { emptyPayoutForm, PayoutFields } from './PayoutFields.jsx'
import { Loader } from './Loader.jsx'
import { VAT_PERCENT, withVat } from '../utils/vat.js'
import { QuantityField } from './QuantityField.jsx'
import { ExtraCategoryField } from './ExtraCategoryField.jsx'

const emptyForm = { name: '', description: '', dailyPrice: '' }
const emptyAddressForm = { address: '', city: '', addressNotes: '', floor: '', apartment: '' }

// categoryId אופציונלי: כשהמשתמשת פותחת את החלון מהלשונית "הכל" אין קטגוריה מוגדרת מראש -
// ואז מוצגת רשימת בחירה של קטגוריה (categories) בראש הטופס.
export function AddItemModal({ categoryId = '', categories = [], ownerId, onClose, onCreated, initialForSale = false }) {
  const { updateUser } = useAuth()
  const [forSale, setForSale] = useState(initialForSale)
  const [salePrice, setSalePrice] = useState('')
  const [saleCondition, setSaleCondition] = useState('Used')
  // קטגוריה ראשית + תת-קטגוריה אופציונלית. אם נפתח מתוך קטגוריה (או תת-קטגוריה) - היא נבחרת מראש
  const initialCategory = categories.find((c) => c.id === categoryId)
  const [parentId, setParentId] = useState(initialCategory?.parentId || categoryId)
  const [subId, setSubId] = useState(initialCategory?.parentId ? categoryId : '')
  const topCategories = categories.filter((c) => !c.parentId)
  const subCategories = categories.filter((c) => c.parentId && c.parentId === parentId)
  const selectedCategoryId = subId || parentId
  // סטודיו/לוקיישן: מחיר לשעה ומחיר ליום שלם, מינימום בשעות
  // שיוך לקטגוריה נוספת - רק בקטגוריות שבהן פריט מתאים לעיתים קרובות לכמה מקומות
  const allowsExtraCategory = ['ניו בורן', 'לסטודיו ולחוץ', 'ציוד להפקות'].some((n) =>
    categories.find((c) => c.id === parentId)?.name?.includes(n),
  )
  const isLocationCategory =
    !forSale && [selectedCategoryId, parentId].some((id) => categories.find((c) => c.id === id)?.name?.includes('לוקיישן'))
  const categoryName = categories.find((c) => c.id === selectedCategoryId)?.name
  const modalTitle = forSale ? 'פריט חדש למכירה (יד 2)' : categoryName ? `שיתוף ציוד חדש - ${categoryName}` : 'שיתוף ציוד חדש'

  // שלב 1 (חד-פעמי): איסוף כתובת מגורים ועיר מהמשכיר, אם עדיין לא נשמרה - נבדק מול השרת בכל
  // פתיחה (ולא רק מהעותק המקומי) כדי לתפוס גם מצב שבו הפרופיל המקומי מיושן.
  const [checkingAddress, setCheckingAddress] = useState(true)
  const [needsAddress, setNeedsAddress] = useState(false)
  const [addressForm, setAddressForm] = useState(emptyAddressForm)
  const [addrStreet, setAddrStreet] = useState('')
  const [addrNumber, setAddrNumber] = useState('')
  const [addrValid, setAddrValid] = useState(false)
  const [addressError, setAddressError] = useState('')
  const [savingAddress, setSavingAddress] = useState(false)

  // שלב 2 (חד-פעמי): מיד לאחר הגדרת הכתובת, איסוף פרטי חשבון בנק לקבלת תשלומים - אם עדיין לא הוגדרו
  const [needsPayout, setNeedsPayout] = useState(false)

  // אישור תנאי עמלת השירות - פעם אחת, בפריט הראשון (נשמר בשרת עם חותמת זמן)
  const [needsConsent, setNeedsConsent] = useState(false)
  const [consentChecked, setConsentChecked] = useState(false)
  const [needsSaleConsent, setNeedsSaleConsent] = useState(false)
  const [saleConsentChecked, setSaleConsentChecked] = useState(false)
  const [payoutForm, setPayoutForm] = useState(emptyPayoutForm)
  const [payoutError, setPayoutError] = useState('')
  const [savingPayout, setSavingPayout] = useState(false)

  const [form, setForm] = useState(emptyForm)
  const [images, setImages] = useState([])

  // הגדרות תמחור/השכרה מתקדמות - כולן אופציונליות, מוסתרות מאחורי "הגדרות מתקדמות"
  const [advancedOpen, setAdvancedOpen] = useState(false)
  const [hourlyEnabled, setHourlyEnabled] = useState(false)
  const [hourlyPrice, setHourlyPrice] = useState('')
  const [addVat, setAddVat] = useState(false)
  const [quantity, setQuantity] = useState(1)
  const [extraCategoryId, setExtraCategoryId] = useState('')
  const [minRentalUnit, setMinRentalUnit] = useState('Days')
  const [minRentalAmount, setMinRentalAmount] = useState('1')

  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  // לאחר ההעלאה מוצג מסך הצלחה (במקום לסגור את החלון מיד) - ושם אפשר להוסיף פריט נוסף
  const [createdItem, setCreatedItem] = useState(null)
  const [celebrating, setCelebrating] = useState(false)
  const [step, setStep] = useState(1)

  useEffect(() => {
    if (!celebrating) return
    const t = setTimeout(() => setCelebrating(false), 2000)
    return () => clearTimeout(t)
  }, [celebrating])

  function goNext() {
    setError('')
    if (step === 1) {
      if (!selectedCategoryId) {
        setError('בחרי קטגוריה')
        return
      }
      if (!form.name.trim()) {
        setError('יש להזין שם לפריט')
        return
      }
    }
    setStep((s) => Math.min(3, s + 1))
  }

  useEffect(() => {
    let cancelled = false

    async function checkAddressAndPayout() {
      try {
        const freshUser = await usersApi.getById(ownerId)
        if (cancelled) return

        setNeedsConsent(!freshUser?.commissionConsentAtUtc)
        setNeedsSaleConsent(!freshUser?.saleCommissionConsentAtUtc)

        if (freshUser?.address && freshUser?.city && freshUser?.addressFloor && freshUser?.addressApartment) {
          updateUser({
            address: freshUser.address,
            city: freshUser.city,
            addressNotes: freshUser.addressNotes ?? '',
            addressFloor: freshUser.addressFloor,
            addressApartment: freshUser.addressApartment,
          })
          setNeedsAddress(false)
          await checkPayout()
        } else {
          if (freshUser?.address) {
            const parts = splitStreetAndNumber(freshUser.address)
            setAddrStreet(parts.street)
            setAddrNumber(parts.number)
            setAddressForm({
              address: '',
              city: freshUser.city ?? '',
              addressNotes: freshUser.addressNotes ?? '',
              floor: freshUser.addressFloor ?? '',
              apartment: freshUser.addressApartment ?? '',
            })
          }
          setNeedsAddress(true)
        }
      } catch {
        // אם הבדיקה נכשלה (למשל בעיית רשת רגעית) - עדיף לא לחסום את המשכיר; נניח שהכתובת קיימת
        // ואם היא באמת חסרה, השרת יחזיר שגיאה ברורה בעת השמירה עצמה.
        if (!cancelled) setNeedsAddress(false)
      } finally {
        if (!cancelled) setCheckingAddress(false)
      }
    }

    async function checkPayout() {
      try {
        const payout = await usersApi.getPayout(ownerId)
        if (!cancelled) setNeedsPayout(!payout?.isConfigured)
      } catch {
        // אותו עיקרון: לא לחסום את המשכיר אם הבדיקה נכשלה מסיבה רגעית
        if (!cancelled) setNeedsPayout(false)
      }
    }

    checkAddressAndPayout()
    return () => {
      cancelled = true
    }
  }, [ownerId, updateUser])

  function updateForm(field) {
    return (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  function updateAddressField(field) {
    return (e) => setAddressForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  async function handleSaveAddress(e) {
    e.preventDefault()
    setAddressError('')

    if (!addressForm.city.trim() || !addrStreet.trim() || !addrNumber.trim()) {
      setAddressError('יש להזין עיר, רחוב ומספר בית')
      return
    }
    if (!addrValid) {
      setAddressError('יש לבחור עיר ורחוב מהרשימה שמופיעה בזמן ההקלדה')
      return
    }

    if (!addressForm.floor.trim() || !addressForm.apartment.trim()) {
      setAddressError('יש להזין קומה ומספר דירה או שם משפחה')
      return
    }

    setSavingAddress(true)
    try {
      const updated = await usersApi.updateAddress(ownerId, {
        address: joinStreetAndNumber(addrStreet, addrNumber),
        street: addrStreet.trim(),
        city: addressForm.city.trim(),
        addressNotes: addressForm.addressNotes,
        addressFloor: addressForm.floor.trim(),
        addressApartment: addressForm.apartment.trim(),
      })
      updateUser({
        address: updated.address,
        city: updated.city,
        addressNotes: updated.addressNotes ?? '',
        addressFloor: updated.addressFloor ?? '',
        addressApartment: updated.addressApartment ?? '',
      })
      setNeedsAddress(false)

      try {
        const payout = await usersApi.getPayout(ownerId)
        setNeedsPayout(!payout?.isConfigured)
      } catch {
        setNeedsPayout(false)
      }
    } catch (err) {
      setAddressError(err.message)
    } finally {
      setSavingAddress(false)
    }
  }

  function updatePayoutForm(next) {
    setPayoutForm(next)
  }

  async function handleSavePayout(e) {
    e.preventDefault()
    setPayoutError('')

    if (
      !payoutForm.accountHolderName.trim() ||
      !payoutForm.bankName.trim() ||
      !payoutForm.branchNumber.trim() ||
      !payoutForm.accountNumber.trim()
    ) {
      setPayoutError('יש למלא את כל פרטי החשבון')
      return
    }
    if (!payoutForm.consentAccepted) {
      setPayoutError('יש לאשר את שמירת פרטי החשבון')
      return
    }

    setSavingPayout(true)
    try {
      await usersApi.updatePayout(ownerId, payoutForm)
      setNeedsPayout(false)
    } catch (err) {
      setPayoutError(err.message)
    } finally {
      setSavingPayout(false)
    }
  }

  function handleSkipPayout() {
    setNeedsPayout(false)
  }

  async function handleSubmit(e) {
    e.preventDefault()
    if (step < 3) {
      goNext()
      return
    }
    setError('')

    if (forSale && (!form.name.trim() || !(Number(salePrice) > 0))) {
      setError('יש להזין שם ומחיר מכירה')
      return
    }
    if (forSale && needsSaleConsent && !saleConsentChecked) {
      setError('יש לאשר את עמלת השירות על המכירה')
      return
    }
    if (!forSale && (!form.name.trim() || !form.dailyPrice)) {
      setError('יש להזין לפחות שם ומחיר יומי')
      return
    }

    if (isLocationCategory && !hourlyPrice) {
      setError('יש להזין מחיר לשעה')
      return
    }

    if (hourlyEnabled && !hourlyPrice) {
      setError('יש להזין מחיר לשעה, או לבטל את אפשרות התמחור השעתי')
      return
    }

    setSubmitting(true)
    try {
      if (forSale && needsSaleConsent) {
        try {
          await usersApi.acceptSaleCommission(ownerId)
          setNeedsSaleConsent(false)
        } catch {
          // ננסה שוב בפריט הבא
        }
      }
      if (needsConsent && !forSale) {
        // שמירת האישור בשרת היא "בנוסף": אם נקודת הקצה עוד לא קיימת (שרת ישן) או שנכשלה - לא חוסמים העלאת פריט
        try {
          await usersApi.acceptCommission(ownerId)
          setNeedsConsent(false)
        } catch {
          // ננסה שוב בפריט הבא
        }
      }

      const created = await itemsApi.create({
        name: form.name,
        description: form.description,
        dailyPrice: forSale ? 0 : Number(form.dailyPrice),
        hourlyPrice: !forSale && (hourlyEnabled || isLocationCategory) && hourlyPrice ? Number(hourlyPrice) : null,
        addVat: !forSale && addVat,
        forSale,
        salePrice: forSale ? Number(salePrice) : 0,
        saleCondition: forSale ? saleCondition : null,
        quantity: isLocationCategory || forSale ? 1 : quantity,
        minRentalUnit: isLocationCategory ? 'Hours' : minRentalUnit,
        minRentalAmount: Number(minRentalAmount) || 1,
        serialNumber: '',
        categoryId: selectedCategoryId,
        ownerId,
        extraCategoryId: extraCategoryId || null,
        imageDataUrl: images[0] ?? null,
        imageDataUrls: images,
      })
      onCreated(created)
      setCreatedItem(created)
      const reduced = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
      setCelebrating(!reduced)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  if (checkingAddress) {
    return (
      <Modal title={modalTitle} onClose={onClose}>
        <Loader label="טוען" compact />
      </Modal>
    )
  }

  if (needsAddress) {
    return (
      <Modal title="כתובת מגורים" onClose={onClose}>
        <p className="modal-intro">
          לפני שיתוף הפריט הראשון שלכם עם הקהילה, נא הזינו את כתובת המגורים שלכם. הכתובת תשמש
          להצגת האזור שלכם לשוכרים, ותוצג במלואה רק כאשר שוכר בוחר באיסוף עצמי.
        </p>
        <form className="modal-form" onSubmit={handleSaveAddress}>
          <CityStreetFields
            idPrefix="owner"
            city={addressForm.city}
            street={addrStreet}
            onCityChange={(city) => setAddressForm((prev) => ({ ...prev, city }))}
            onStreetChange={setAddrStreet}
            onValidChange={setAddrValid}
            autoFocus
          />

          <label htmlFor="ownerAddressNumber">מספר בית</label>
          <input
            id="ownerAddressNumber"
            value={addrNumber}
            onChange={(e) => setAddrNumber(e.target.value)}
            required
            inputMode="numeric"
          />

          <label htmlFor="ownerAddressFloor">קומה</label>
          <input id="ownerAddressFloor" value={addressForm.floor} onChange={updateAddressField('floor')} required />

          <label htmlFor="ownerAddressApartment">מספר דירה / שם משפחה</label>
          <input id="ownerAddressApartment" value={addressForm.apartment} onChange={updateAddressField('apartment')} required />
          <span className="field-hint">הפרטים האלה מועברים לשליח, ובאיסוף עצמי מוצגים גם לשוכרת, כדי שתמצא את הדלת.</span>

          <label htmlFor="ownerAddressNotes">הערות כתובת (אופציונלי)</label>
          <input
            id="ownerAddressNotes"
            value={addressForm.addressNotes}
            onChange={updateAddressField('addressNotes')}
            maxLength={200}
          />
          <span className="field-hint">ההערה תוצג לשוכרת באיסוף עצמי, ותועבר לשליח במשלוח.</span>

          {addressError && <p className="form-error">{addressError}</p>}

          <button type="submit" className="btn btn-primary" disabled={savingAddress}>
            {savingAddress ? 'שומר...' : 'שמירה והמשך'}
          </button>
        </form>
      </Modal>
    )
  }

  if (needsPayout) {
    return (
      <Modal title="פרטי חשבון לקבלת תשלומים" onClose={onClose}>
        <p className="modal-intro">
          כדי לקבל תשלומים עבור השכרות (ובכללן תשלום בהעברה בנקאית), הזינו את פרטי חשבון הבנק
          שלכם. ניתן לשנות את הפרטים בכל עת מהאזור האישי.
        </p>
        <form className="modal-form" onSubmit={handleSavePayout}>
          <PayoutFields form={payoutForm} onChange={updatePayoutForm} idPrefix="addItemPayout" />

          {payoutError && <p className="form-error">{payoutError}</p>}

          <button type="submit" className="btn btn-primary" disabled={savingPayout}>
            {savingPayout ? 'שומר...' : 'שמירה והמשך'}
          </button>
          <button type="button" className="advanced-toggle" onClick={handleSkipPayout}>
            דלגו לעת עתה (ניתן להגדיר מאוחר יותר באזור האישי)
          </button>
        </form>
      </Modal>
    )
  }

  if (createdItem && celebrating) {
    return (
      <Modal title="" onClose={onClose} className="add-item-dialog">
        <div className="celebrate" role="status">
          <div className="confetti" aria-hidden="true">
            {Array.from({ length: 28 }, (_, i) => (
              <span key={i} style={{ '--i': i }} />
            ))}
          </div>
          <div className="celebrate-emoji" aria-hidden="true">
            🎉
          </div>
          <h3>הפריט שלך באוויר!</h3>
        </div>
      </Modal>
    )
  }

  if (createdItem) {
    return (
      <Modal title="הפריט נוסף בהצלחה" onClose={onClose}>
        <div className="item-added">
          <div className="item-added-icon" aria-hidden="true">
            ✓
          </div>
          <h3>
            <bdi>{createdItem.name}</bdi> נוסף לקטלוג!
          </h3>
          <p>
            הפריט כבר מוצג לחברות הקהילה וזמין להשכרה. שלחנו אליך מייל אישור, ואפשר לערוך או למחוק את
            הפריט בכל עת באזור האישי.
          </p>
          <div className="item-added-actions">
            <button type="button" className="btn btn-primary" onClick={onClose}>
              סגירה
            </button>
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => {
                setCreatedItem(null)
                setForm(emptyForm)
                setImages([])
                setStep(1)
              }}
            >
              הוספת פריט נוסף
            </button>
          </div>
        </div>
      </Modal>
    )
  }

  return (
    <Modal title={modalTitle} onClose={onClose} className="add-item-dialog">
      <div className="brand-bubbles" aria-hidden="true">
        <span />
        <span />
        <span />
      </div>
      <ol className="add-steps" aria-label="שלבי השיתוף">
        {['מה משתפים?', 'תמונות', 'מחיר'].map((label, i) => (
          <li key={label} className={step === i + 1 ? 'is-current' : step > i + 1 ? 'is-done' : ''}>
            <span className="add-steps-dot">{step > i + 1 ? '✓' : i + 1}</span>
            <span className="add-steps-label">{label}</span>
          </li>
        ))}
      </ol>
      <form className="modal-form add-step-body" key={step} onSubmit={handleSubmit}>
        {step === 1 && (
          <>
        <div className="sale-mode-toggle" role="radiogroup" aria-label="סוג הפריט">
          <button type="button" role="radio" aria-checked={!forSale} className={!forSale ? 'is-on' : ''} onClick={() => setForSale(false)}>
            להשכרה
          </button>
          <button type="button" role="radio" aria-checked={forSale} className={forSale ? 'is-on' : ''} onClick={() => setForSale(true)}>
            למכירה (יד 2)
          </button>
        </div>
        {!categoryId && (
          <>
            <label htmlFor="addItemCategory">קטגוריה</label>
            <select
              id="addItemCategory"
              value={parentId}
              onChange={(e) => {
                setParentId(e.target.value)
                setSubId('')
                setExtraCategoryId('')
              }}
              required
            >
              <option value="" disabled>
                בחרו קטגוריה
              </option>
              {topCategories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </>
        )}

        {subCategories.length > 0 && (
          <>
            <label htmlFor="addItemSubCategory">תת-קטגוריה (אופציונלי)</label>
            <select id="addItemSubCategory" value={subId} onChange={(e) => setSubId(e.target.value)}>
              <option value="">ללא תת-קטגוריה</option>
              {subCategories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </>
        )}

        {allowsExtraCategory && (
          <ExtraCategoryField
            key={parentId}
            categories={categories}
            value={extraCategoryId}
            onChange={setExtraCategoryId}
            idPrefix="addItem"
            mainParentId={parentId}
            mainSubId={subId}
          />
        )}

        <label htmlFor="addItemName">שם הפריט</label>
        <input
          id="addItemName"
          value={form.name}
          onChange={updateForm('name')}
          placeholder={isLocationCategory ? "כתבי כאן בצורה מדויקת את סגנון הלוקיישן שלך (סטודיו שחור / בית עץ עתיק וכו')" : "הקפידי לכתוב את שם הדגם המדויק של הציוד שלך"}
          required
          autoFocus={!!categoryId}
        />

        <label htmlFor="addItemDescription">תיאור</label>
        <textarea
          id="addItemDescription"
          rows={3}
          placeholder={isLocationCategory ? "תארי בצורה מיטבית את הלוקיישן שלך, מה הוא כולל, פרטים מיוחדים אם יש וכל מה שצריך כדי לדייק את בחירת השוכרת. אין צורך לכתוב כתובת פיזית של המקום" : "תארי בצורה מיטבית את הפריט שלך, מה הוא כולל, פרטים מיוחדים אם יש וכל מה שאפשר כדי לדייק את בחירת השוכרות."}
          value={form.description}
          onChange={updateForm('description')}
        />

          </>
        )}

        {step === 2 && (
          <>
            <ImagePicker id="addItemImage" images={images} onChange={setImages} variant="drop" />
            {form.name.trim() && !isLocationCategory && (
              <a
                className="image-search-link"
                href={`https://www.google.com/search?tbm=isch&q=${encodeURIComponent(form.name.trim())}`}
                target="_blank"
                rel="noopener noreferrer"
              >
                🔍 חפשי תמונה של הפריט ברשת
              </a>
            )}
          </>
        )}

        {step === 3 && forSale && (
          <>
            <label htmlFor="addItemSalePrice">מחיר מכירה (₪)</label>
            <input
              id="addItemSalePrice"
              type="number"
              min="0"
              step="1"
              value={salePrice}
              onChange={(e) => setSalePrice(e.target.value)}
              required
              autoFocus
            />
            <label htmlFor="addItemCondition">מצב הפריט</label>
            <select id="addItemCondition" value={saleCondition} onChange={(e) => setSaleCondition(e.target.value)}>
              <option value="New">חדש בקופסה</option>
              <option value="LikeNew">כמו חדש - שומש פעמים בודדות</option>
              <option value="Used">משומש</option>
              <option value="Damaged">קצת פגום</option>
            </select>
            {needsSaleConsent && (
              <label className="checkbox-row commission-consent">
                <input type="checkbox" checked={saleConsentChecked} onChange={(e) => setSaleConsentChecked(e.target.checked)} required />
                <span>
                  ההעלאה חינם. אם הפריט נמכר דרך COMMON, אני מאשרת עמלת שירות של 5% מסכום המכירה.{' '}
                  <a href="/terms#yad2" target="_blank" rel="noreferrer">
                    לתנאים
                  </a>
                </span>
              </label>
            )}
          </>
        )}

        {step === 3 && !forSale && (
          <>
        {isLocationCategory && (
          <>
            <label htmlFor="addItemHourly">מחיר לשעה (₪)</label>
            <input
              id="addItemHourly"
              type="number"
              min="0"
              step="0.01"
              value={hourlyPrice}
              onChange={(e) => setHourlyPrice(e.target.value)}
              required
            />
            <label htmlFor="addItemMinHours">מינימום שעות להזמנה</label>
            <input
              id="addItemMinHours"
              type="number"
              min="1"
              step="1"
              value={minRentalAmount}
              onChange={(e) => setMinRentalAmount(e.target.value)}
            />
          </>
        )}
        <label htmlFor="addItemPrice">{isLocationCategory ? 'מחיר ליום צילום שלם (₪)' : 'מחיר יומי (₪)'}</label>
        <input
          id="addItemPrice"
          type="number"
          min="0"
          step="0.01"
          value={form.dailyPrice}
          onChange={updateForm('dailyPrice')}
          required
          autoFocus
        />

        <label className="checkbox-row vat-row">
          <input type="checkbox" checked={addVat} onChange={(e) => setAddVat(e.target.checked)} />
          <span>הוסיפי למחיר {VAT_PERCENT}% מע״מ באופן אוטומטי</span>
        </label>
        {addVat && Number(form.dailyPrice) > 0 && (
          <p className="field-hint">
            המחיר לשוכרות: ₪{form.dailyPrice} + מע״מ · סה״כ <strong>₪{withVat(form.dailyPrice)}</strong> ליום
          </p>
        )}

        {!isLocationCategory && <QuantityField quantity={quantity} onChange={setQuantity} />}

        <button
          type="button"
          className="advanced-toggle"
          hidden={isLocationCategory}
          onClick={() => setAdvancedOpen((prev) => !prev)}
          aria-expanded={advancedOpen}
        >
          {advancedOpen ? '▾' : '◂'} הגדרות השכרה מתקדמות (אופציונלי)
        </button>

        {advancedOpen && !isLocationCategory && (
          <div className="advanced-panel">
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={hourlyEnabled}
                onChange={(e) => setHourlyEnabled(e.target.checked)}
              />
              אפשר גם תמחור לפי שעה
            </label>

            {hourlyEnabled && (
              <>
                <label htmlFor="hourlyPrice">מחיר לשעה (₪)</label>
                <input
                  id="hourlyPrice"
                  type="number"
                  min="0"
                  step="0.01"
                  value={hourlyPrice}
                  onChange={(e) => setHourlyPrice(e.target.value)}
                />
              </>
            )}

            <label htmlFor="minRentalAmount">משך השכרה מינימלי</label>
            <div className="min-rental-row">
              <input
                id="minRentalAmount"
                type="number"
                min="1"
                step="1"
                value={minRentalAmount}
                onChange={(e) => setMinRentalAmount(e.target.value)}
              />
              <select value={minRentalUnit} onChange={(e) => setMinRentalUnit(e.target.value)}>
                <option value="Days">ימים</option>
                <option value="Hours">שעות</option>
              </select>
            </div>
          </div>
        )}

        {needsConsent && (
          <label className="checkbox-row commission-consent">
            <input
              type="checkbox"
              checked={consentChecked}
              onChange={(e) => setConsentChecked(e.target.checked)}
              required
            />
            <span>
              אני מאשרת שתחויב עמלת שירות של {COMMISSION_PERCENT}% מסכום הציוד בכל השכרה שהוחזרה, בדוח חודשי
              שישלח אליי במייל.{' '}
              <a href="/terms#fees" target="_blank" rel="noreferrer">
                לתנאים
              </a>
            </span>
          </label>
        )}

          </>
        )}

        {error && <p className="form-error">{error}</p>}

        <div className="add-step-actions">
          {step > 1 && (
            <button type="button" className="btn btn-ghost" onClick={() => { setError(''); setStep((s) => s - 1) }}>
              → חזרה
            </button>
          )}
          {step < 3 ? (
            <button type="submit" className="btn btn-primary">
              {step === 2 && images.length === 0 ? 'דילוג - בלי תמונות ←' : 'המשך ←'}
            </button>
          ) : (
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? 'משתפת...' : 'שיתוף הפריט'}
            </button>
          )}
        </div>
      </form>
    </Modal>
  )
}
