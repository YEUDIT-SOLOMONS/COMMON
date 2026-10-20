import { useEffect, useMemo, useRef, useState } from 'react'
import { ShipmentTracking } from './ShipmentTracker.jsx'
import { Link, useNavigate } from 'react-router-dom'
import { availabilityCallsApi, itemsApi, loansApi, orderPayApi, shippingPayApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { CategoryIcon } from './CategoryIcon.jsx'
import { categoryTrailForItem, iconTextsForItem } from '../utils/categoryIcon.js'
import { MapPinIcon, PhoneRingingIcon } from './icons.jsx'
import { Modal } from './Modal.jsx'
import { PickupDetails } from './PickupDetails.jsx'
import { readProofFile } from '../utils/proof.js'
import { CityStreetFields } from './AddressFields.jsx'
import { Loader } from './Loader.jsx'
import { ItemExtras } from './ItemExtras.jsx'
import { MonthCalendar } from './MonthCalendar.jsx'
import { PhoneUpdateModal } from './PhoneUpdateModal.jsx'
import { busyRangesFor, daysInRange, freeUnits, isDayBusy, rangeOverlaps } from '../utils/schedule.js'
import { formatDateTime, israelNowWall, todayIsraelIso, wallMs } from '../utils/format.js'
import { Price } from './Price.jsx'
import { beforeVat } from '../utils/vat.js'
import { AskOwner } from './AskOwner.jsx'

// תאריך מקומי (YYYY-MM-DD) - לא UTC, כדי שאחרי חצות בישראל לא ייחשב "אתמול"
function todayIso() {
  return todayIsraelIso()
}

function addDaysIso(iso, days) {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}

function daysBetween(fromIso, toIso) {
  return Math.round((new Date(`${toIso}T00:00:00Z`) - new Date(`${fromIso}T00:00:00Z`)) / 86400000)
}

const HEBREW_DAY = new Intl.DateTimeFormat('en-u-ca-hebrew', { month: 'long', day: 'numeric', timeZone: 'UTC' })
const HOLY = { Tishri: [1, 2, 10, 15, 22], Nisan: [15, 21], Sivan: [6] }

// שבת או יום טוב - כמו CallWindow.IsHolyDay בשרת
function isHolyDay(iso) {
  const d = new Date(`${iso}T00:00:00Z`)
  if (d.getUTCDay() === 6) return true
  const parts = HEBREW_DAY.formatToParts(d)
  const month = parts.find((p) => p.type === 'month')?.value
  const day = Number(parts.find((p) => p.type === 'day')?.value)
  return (HOLY[month] ?? []).includes(day)
}

// יום השכרה שמתחיל בשבת/חג (לא היום הראשון) לא מחויב - כמו ShabbatPricing בשרת
function chargedDaysFor(startIso, span) {
  if (span <= 1) return Math.max(1, span)
  let free = 0
  for (let i = 1; i < span; i++) if (isHolyDay(addDaysIso(startIso, i))) free++
  return Math.max(1, span - free)
}

function toMinutes(time) {
  const [h, m] = (time || '0:0').split(':').map(Number)
  return (h || 0) * 60 + (m || 0)
}

// השעה עכשיו בישראל (HH:mm)
function nowTimeIso() {
  return israelNowWall().slice(11, 16)
}

// שעת התחלה ברירת מחדל: 12:00, ואם כבר עברה היום - הרבע-שעה/חמש הדקות הקרובות
// ברירת המחדל להתחלה: שעה מעכשיו (שעון ישראל), כולל מעבר לתאריך הבא אם חוצים חצות
function defaultStart() {
  const wall = israelNowWall()
  const plusHour = new Date(`${wall.slice(0, 16)}:00Z`)
  plusHour.setUTCHours(plusHour.getUTCHours() + 1)
  const iso = plusHour.toISOString()
  return { date: iso.slice(0, 10), time: iso.slice(11, 16) }
}

// כשבוחרים את היום עצמו: שעה מעכשיו, ואם זה כבר עובר לחצות - סוף היום
function defaultStartTime() {
  const { date, time } = defaultStart()
  return date === todayIso() ? time : '23:59'
}

function formatDateHe(iso) {
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

const CALL_POLL_INTERVAL_MS = 1500
const AUTO_REDIRECT_MS = 2500

// השהיה לפני שליחת בקשת הצעת מחיר משלוח בכל הקלדה בשדה הכתובת - כדי לא להציף את השרת (ואת
// חברת השילוח בפועל) בבקשה על כל תו שמוקלד
const SHIPPING_QUOTE_DEBOUNCE_MS = 600

function UploadProgress({ label }) {
  return (
    <div className="upload-progress" role="progressbar" aria-label={label} aria-busy="true">
      <span className="upload-progress-track">
        <span className="upload-progress-bar" />
      </span>
      <span className="upload-progress-label">{label}</span>
    </div>
  )
}

export function RentModal({
  item,
  onClose,
  onRented,
  categoryChains,
  initialStartDate,
  initialReturnDate,
  initialStartTime,
  initialReturnTime,
  confirmedCallId,
  loginReturnTo,
  initialQuantity,
}) {
  const { user, isAuthenticated } = useAuth()
  const navigate = useNavigate()

  const isOwnItem = user && item.ownerId === user.id

  // גלריית תמונות הפריט (אם יש כמה) - מעבר בין תמונות בחצים/נקודות
  // "מציגים" פריט נוסף של אותו חבר קהילה בתוך אותו חלון: התמונות/תיאור/מחיר מתחלפים, אבל כל הבחירות
  // (תאריכים, שעות, אופן קבלה והחזרה, תשלום והפריטים שצורפו להזמנה) נשארות מסונכרנות - הזמנה אחת
  const [viewedItemId, setViewedItemId] = useState(null)
  const [galleryIndex, setGalleryIndex] = useState(0)

  const [deliveryMethod, setDeliveryMethod] = useState('SelfPickup')
  // תאריכים מהסינון בקטלוג ("פנוי בתאריכים שלי") - אם נבחרו, החלון נפתח איתם
  const [startDate, setStartDate] = useState(() => initialStartDate || defaultStart().date)
  const minRentalDays = item.minRentalUnit === 'Days' ? Math.max(1, item.minRentalAmount || 1) : 1
  // סטודיו/לוקיישן: ביום אחד, לפי שעות, הגעה עצמית
  const isLocation = Boolean(item.isLocation)
  const minHours = isLocation && item.minRentalUnit === 'Hours' ? Math.max(1, item.minRentalAmount || 1) : 1
  // תאריך ההחזרה מתחיל כיום אחרי תאריך הלקיחה (או אחרי משך ההשכרה המינימלי של הפריט) ועוקב אחרי
  // תאריך ההתחלה עד שהמשתמשת בוחרת תאריך החזרה בעצמה
  const [returnDate, setReturnDate] = useState(() => {
    const start = initialStartDate || defaultStart().date
    const minimum = addDaysIso(start, minRentalDays)
    // קישור עם שעות: התאריך בקישור הוא תאריך ההחזרה בפועל (כולל היום הנוסף כששעת ההחזרה מאוחרת יותר)
    if (initialReturnDate && initialStartTime && initialReturnTime && initialReturnDate >= start) {
      const later = toMinutes(initialReturnTime) > toMinutes(initialStartTime)
      return later && initialReturnDate > start ? addDaysIso(initialReturnDate, -1) : initialReturnDate
    }
    return initialReturnDate && initialReturnDate >= minimum ? initialReturnDate : minimum
  })
  const [returnDateTouched, setReturnDateTouched] = useState(Boolean(initialReturnDate))
  const [startTime, setStartTime] = useState(() => initialStartTime || defaultStart().time)
  const [returnTime, setReturnTime] = useState(() => initialReturnTime || initialStartTime || defaultStart().time)
  const [returnTimeTouched, setReturnTimeTouched] = useState(Boolean(initialReturnTime))

  // שעת ההחזרה עוקבת אחרי שעת ההתחלה (24 שעות בדיוק) עד שהמשתמשת בוחרת שעת החזרה בעצמה
  function applyStartTime(value) {
    setStartTime(value)
    if (!returnTimeTouched && value) setReturnTime(value)
  }

  function handleReturnTimeChange(value) {
    setReturnTime(value)
    setReturnTimeTouched(true)
  }
  const [expressDelivery, setExpressDelivery] = useState(false)

  function handleStartDateChange(value) {
    setDatesChosen(true)
    setStartDate(value)
    if (!value) return
    if (value === todayIso() && toMinutes(startTime) < toMinutes(nowTimeIso())) applyStartTime(defaultStartTime())
    const earliestReturn = addDaysIso(value, minRentalDays)
    if (!returnDateTouched || !returnDate || daysBetween(value, returnDate) < minRentalDays) {
      setReturnDate(earliestReturn)
    }
  }

  function handleReturnDateChange(value) {
    setDatesChosen(true)
    setReturnDate(value)
    setReturnDateTouched(true)
  }

  const isStartingToday = startDate === todayIso()
  // מועד התחלה בעבר (תאריך שעבר, או היום בשעה שכבר חלפה) - אי אפשר להזמין. נבדק גם בשרת
  const isStartInPast = () =>
    !startDate || startDate < todayIso() || (startDate === todayIso() && toMinutes(startTime) < toMinutes(nowTimeIso()))
  const startInPast = isStartInPast()
  const expressDay = deliveryMethod === 'Delivery' && isStartingToday

  // ===== אופן ומועד החזרה - נקבעים כבר עכשיו, מראש, יחד עם פתיחת ההשכרה =====
  const [returnMethod, setReturnMethod] = useState('SelfDropoff')

  // ===== משך ההשכרה והמחיר לפי הטווח (תאריך+שעת התחלה עד תאריך+שעת החזרה) =====
  // המחיר הוא לפי ימים מלאים: אם שעת ההחזרה מאוחרת משעת ההתחלה, נספר יום נוסף (עיגול כלפי מעלה).
  // רק הימים נשמרים בשרת (StartDate + RentalDays + ReturnTime) - ראו LoanService.
  const calendarDays = startDate && returnDate ? daysBetween(startDate, returnDate) : 0
  const startsEarlierThanReturn = toMinutes(returnTime) > toMinutes(startTime)
  const sameDayReturn = calendarDays === 0 && startsEarlierThanReturn && minRentalDays === 1
  const rentalDays = sameDayReturn
    ? 1
    : calendarDays >= 1
      ? Math.max(minRentalDays, calendarDays + (startsEarlierThanReturn ? 1 : 0))
      : 0
  const chargedDays = rentalDays >= 1 && !sameDayReturn ? chargedDaysFor(startDate, rentalDays) : rentalDays
  const shabbatFree = rentalDays - chargedDays
  const daysText = `${chargedDays} ${chargedDays === 1 ? 'יום' : 'ימים'}${shabbatFree > 0 ? ' - שבת/חג לא נספרים' : ''}`
  const locationHours = isLocation ? Math.max(0, (toMinutes(returnTime) - toMinutes(startTime)) / 60) : 0
  const rangeValid = isLocation ? returnDate === startDate && locationHours >= minHours : rentalDays >= 1
  const locationPrice = isLocation
    ? Math.min(Math.round(locationHours * (item.hourlyPrice || 0) * 100) / 100, item.dailyPrice || Infinity)
    : 0
  const hoursText = (h) => (h === 1 ? 'שעה אחת' : `${Number.isInteger(h) ? h : h.toFixed(1)} שעות`)
  const effectiveReturnDate = sameDayReturn ? startDate : rangeValid ? addDaysIso(startDate, rentalDays) : returnDate
  const returnWasExtended = rangeValid && effectiveReturnDate !== returnDate
  // כתובת מסירה מפורטת (עיר/רחוב/מספר בית/קומה) - עדיפה על שדה חופשי אחד, כי חברת השילוח
  // (GetPackage) דורשת כתובת "מזוהה" בשדות נפרדים לקבלת מחיר ואיסוף/מסירה מדויקים; קומה אינה
  // חלק מהכתובת עצמה ומועברת רק כהערה לשליח (ראו GetPackageShippingService)
  const [deliveryCity, setDeliveryCity] = useState('')
  const [deliveryStreet, setDeliveryStreet] = useState('')
  const [deliveryHouseNumber, setDeliveryHouseNumber] = useState('')
  const [deliveryFloor, setDeliveryFloor] = useState('')
  const [deliveryApartment, setDeliveryApartment] = useState('')
  // איסוף ההחזרה ממקום אחר מזה שאליו נשלח הפריט (רק כשהקבלה במשלוח והחזרה בשליח)
  const [returnOtherAddress, setReturnOtherAddress] = useState(false)
  const [returnCity, setReturnCity] = useState('')
  const [returnStreet, setReturnStreet] = useState('')
  const [returnHouseNumber, setReturnHouseNumber] = useState('')
  const [returnFloor, setReturnFloor] = useState('')
  const [returnApartment, setReturnApartment] = useState('')
  const [returnAddressValid, setReturnAddressValid] = useState(false)
  const offersOtherReturnAddress = deliveryMethod === 'Delivery' && returnMethod === 'CourierPickup'
  const usesOtherReturnAddress = offersOtherReturnAddress && returnOtherAddress
  const isReturnStreetComplete =
    !usesOtherReturnAddress ||
    Boolean(returnCity.trim() && returnStreet.trim() && returnHouseNumber.trim() && returnAddressValid)
  const isReturnAddressComplete =
    isReturnStreetComplete && (!usesOtherReturnAddress || Boolean(returnFloor.trim() && returnApartment.trim()))
  const needsDeliveryAddress = deliveryMethod === 'Delivery' || returnMethod === 'CourierPickup'
  // עיר ורחוב חייבים להיבחר מהרשימה הרשמית (CityStreetFields) - כך הכתובת מדויקת מול חברת השילוח
  const [deliveryAddressValid, setDeliveryAddressValid] = useState(false)
  const isDeliveryStreetComplete = Boolean(
    deliveryCity.trim() && deliveryStreet.trim() && deliveryHouseNumber.trim() && deliveryAddressValid,
  )
  const isDeliveryAddressComplete = isDeliveryStreetComplete && Boolean(deliveryFloor.trim() && deliveryApartment.trim())

  // ===== מחיר משלוח אמיתי - מתקבל מחברת השילוח בפועל (ראו loansApi.getShippingQuote) ברגע
  // שנבחר "משלוח" והושלמה כתובת מלאה, ולא סכום קבוע מנוחש =====
  const [shippingQuote, setShippingQuote] = useState(null) // null | { regularPrice, expressPrice }
  const [shippingQuoteLoading, setShippingQuoteLoading] = useState(false)
  const [shippingQuoteError, setShippingQuoteError] = useState('')
  // מחיר אקספרס 0 = חברת השילוח לא נתנה הצעה לאקספרס (ראו GetPackageShippingService) - מסתירים את האפשרות
  // וגם: אקספרס מוצע רק אם הוא באמת יגיע עד השעה שביקשה (ראו expressArrival מהשרת)
  // כשאין משלוח רגיל לכתובת/לתאריך - מציעים אקספרס גם ביום עתידי (השליח מוזמן ביום ההשכרה)
  const regularUnavailable = !expressDay && shippingQuote?.regularArrival?.possible === false && Boolean(shippingQuote?.expressArrival)
  const showExpressOption =
    (expressDay || regularUnavailable) &&
    (!shippingQuote || (shippingQuote.expressPrice > 0 && shippingQuote.expressArrival?.possible !== false))

  useEffect(() => {
    if (!needsDeliveryAddress || !isDeliveryStreetComplete || !isReturnStreetComplete) {
      setShippingQuote(null)
      setShippingQuoteError('')
      return
    }

    let cancelled = false
    setShippingQuoteLoading(true)
    setShippingQuoteError('')

    const timeoutId = setTimeout(async () => {
      try {
        const quote = await loansApi.getShippingQuote({
          itemId: item.id,
          deliveryAddress: {
            city: deliveryCity.trim(),
            street: deliveryStreet.trim(),
            houseNumber: deliveryHouseNumber.trim(),
            floor: deliveryFloor.trim() || undefined,
            apartment: deliveryApartment.trim() || undefined,
          },
          ...(deliveryMethod === 'Delivery' ? { startDate, startTime } : {}),
          ...(returnMethod === 'CourierPickup' ? { returnDate: effectiveReturnDate, returnTime } : {}),
          ...(usesOtherReturnAddress
            ? {
                returnPickupAddress: {
                  city: returnCity.trim(),
                  street: returnStreet.trim(),
                  houseNumber: returnHouseNumber.trim(),
                  floor: returnFloor.trim() || undefined,
                  apartment: returnApartment.trim() || undefined,
                },
              }
            : {}),
        })
        if (!cancelled) setShippingQuote(quote)
      } catch (err) {
        if (!cancelled) {
          setShippingQuote(null)
          setShippingQuoteError(err.message)
        }
      } finally {
        if (!cancelled) setShippingQuoteLoading(false)
      }
    }, SHIPPING_QUOTE_DEBOUNCE_MS)

    return () => {
      cancelled = true
      clearTimeout(timeoutId)
    }
  }, [
    deliveryMethod,
    needsDeliveryAddress,
    isDeliveryStreetComplete,
    isReturnStreetComplete,
    usesOtherReturnAddress,
    deliveryCity,
    deliveryStreet,
    deliveryHouseNumber,
    deliveryFloor,
    returnCity,
    returnStreet,
    returnHouseNumber,
    returnFloor,
    returnApartment,
    deliveryApartment,
    item.id,
    startDate,
    startTime,
    returnMethod,
    effectiveReturnDate,
    returnTime,
  ])

  // ===== שיחת אימות זמינות (IVR) - מדומה כברירת מחדל, או שיחה טלפונית אמיתית דרך Twilio אם הוגדר =====
  const [callId, setCallId] = useState(null)
  const [retryId, setRetryId] = useState(null)
  const [retryCanceled, setRetryCanceled] = useState(false)
  const [quietRetryAt, setQuietRetryAt] = useState('')
  const [quietNotify, setQuietNotify] = useState('email')
  const [quietScheduled, setQuietScheduled] = useState(false)
  const [ownerEmailed, setOwnerEmailed] = useState(false)
  const [callStatus, setCallStatus] = useState(null) // null | Ringing | ConfirmedAvailable | ConfirmedUnavailable | NoAnswer
  const [callError, setCallError] = useState('')

  // לוח הזמנים של הפריט: ימים תפוסים (השכרות וימים שהמשכירה חסמה)
  const [schedule, setSchedule] = useState(null)
  const [calendarOpen, setCalendarOpen] = useState(false)
  const [pickingReturn, setPickingReturn] = useState(false)
  const [calendarNote, setCalendarNote] = useState('')
  const [datesChosen, setDatesChosen] = useState(Boolean(initialStartDate))
  useEffect(() => {
    if (callStatus !== 'ConfirmedUnavailable') return
    const t = setTimeout(() => {
      itemsApi
        .schedule(item.id)
        .then((sch) => setSchedule(sch ?? null))
        .catch(() => {})
    }, 2500)
    return () => clearTimeout(t)
  }, [callStatus, item.id])
  useEffect(() => {
    let cancelled = false
    itemsApi
      .schedule(item.id)
      .then((s) => {
        if (!cancelled) setSchedule(s ?? null)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [item.id])
  const [callBusy, setCallBusy] = useState(false)
  const [detailsDone, setDetailsDone] = useState(false)
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [shipPay, setShipPay] = useState(null)
  const [cardConsent, setCardConsent] = useState(false)
  const [orderPay, setOrderPay] = useState(null)
  const [saveCardLater, setSaveCardLater] = useState(true)

  useEffect(() => {
    let cancelled = false
    orderPayApi
      .status()
      .then((r) => !cancelled && setOrderPay(r))
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    shippingPayApi
      .status()
      .then((r) => !cancelled && setShipPay(r))
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])
  const [phoneConfirmed, setPhoneConfirmed] = useState(false)
  const [phoneEditOpen, setPhoneEditOpen] = useState(false)
  // כפתורי הדימוי הידניים (1/2) מוצגים רק כשמדובר בהדמיה - בשיחה אמיתית הלחיצה מגיעה מהמשכיר בפועל
  const [callIsSimulated, setCallIsSimulated] = useState(true)
  const pollRef = useRef(null)

  // הכתובת המדויקת נטענת לאחר אימות זמינות מוצלח, אבל מוצגת רק לאחר אישור ההשכרה (ראו PickupDetails) - ורק כשנבחר איסוף עצמי
  const [revealedAddress, setRevealedAddress] = useState(null)


  // ===== אמצעי תשלום =====
  const [paymentMethod, setPaymentMethod] = useState(null) // null | Cash | BankTransfer | CreditCard
  const [ownerPayout, setOwnerPayout] = useState(null)
  // אסמכתת העברה בנקאית: חובה לפני אישור ההשכרה (ראו "מרחב התשלום" למטה)
  const [proof, setProof] = useState(null) // { dataUrl, fileName, contentType }
  const [proofError, setProofError] = useState('')
  const [proofBusy, setProofBusy] = useState(false)
  const [payoutLoading, setPayoutLoading] = useState(false)
  const [payoutError, setPayoutError] = useState('')

  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState('')
  const [successMessage, setSuccessMessage] = useState('')
  const [successTracking, setSuccessTracking] = useState(null)

  useEffect(() => {
    if (!successMessage || !successTracking?.id) return undefined
    const id = successTracking.id
    const timer = setTimeout(() => {
      onClose?.()
      navigate(`/order/${id}`)
    }, AUTO_REDIRECT_MS)
    return () => clearTimeout(timer)
  }, [successMessage, successTracking, navigate, onClose])

  // ===== עוד פריטים מאותו חבר קהילה - ניתן לצרף כמה מהם לאותה הזמנה (ראו LoanBatchRequestDto) =====
  const [otherItems, setOtherItems] = useState([])
  const [selectedOtherIds, setSelectedOtherIds] = useState([])
  const [showAllOthers, setShowAllOthers] = useState(false)

  function toggleOtherItem(other) {
    setSelectedOtherIds((prev) =>
      prev.includes(other.id) ? prev.filter((id) => id !== other.id) : [...prev, other.id],
    )
  }

  const selectedOtherItems = otherItems.filter((other) => selectedOtherIds.includes(other.id))

  // כמה יחידות מכל פריט (לפריטים שיש מהם כמה עותקים)
  const [quantities, setQuantities] = useState(() => ({
    [item.id]: Math.min(Math.max(1, Number(initialQuantity) || 1), Math.max(1, item.quantity || 1)),
  }))
  const unitsOf = (o) => Math.min(Math.max(1, quantities[o.id] || 1), Math.max(1, o.quantity || 1))
  const setUnits = (o, n) => setQuantities((prev) => ({ ...prev, [o.id]: Math.min(Math.max(1, n), Math.max(1, o.quantity || 1)) }))
  const nameWithUnits = (o) => (unitsOf(o) > 1 ? `${o.name} (${unitsOf(o)} יחידות)` : o.name)
  const busyRanges = useMemo(() => busyRangesFor(schedule, unitsOf(item)), [schedule, quantities])
  // בלוח: בסטודיו רק ימים חסומים כולם; השעות התפוסות ביום שנבחר מוצגות מתחת
  const calendarBusy = isLocation ? schedule?.blocked ?? [] : busyRanges
  const takenHours = isLocation
    ? (schedule?.bookings ?? [])
        .filter((b) => b.from.slice(0, 10) <= startDate && b.to.slice(0, 10) >= startDate)
        .map((b) => `${b.from.slice(0, 10) < startDate ? '00:00' : b.from.slice(11, 16)}-${b.to.slice(0, 10) > startDate ? '24:00' : b.to.slice(11, 16)}`)
    : []
  useEffect(() => {
    if (isLocation && returnDate !== startDate) setReturnDate(startDate)
  }, [isLocation, startDate, returnDate])
  const orderItems = [item, ...selectedOtherItems]
  const isBatchOrder = selectedOtherItems.length > 0
  const totalUnits = orderItems.reduce((sum, o) => sum + unitsOf(o), 0)
  const countText =
    totalUnits > orderItems.length
      ? `${orderItems.length === 1 ? 'פריט אחד' : `${orderItems.length} פריטים`}, ${totalUnits} יחידות`
      : `${orderItems.length} פריטים`

  const viewedItem = (viewedItemId && otherItems.find((other) => other.id === viewedItemId)) || item
  const isViewingOther = viewedItem.id !== item.id
  // ברשימת הקטלוג מגיעים רק התמונה הראשית והעיר; הגלריה המלאה ושם הרחוב נשלפים עם הפריט כשהוא נפתח
  const [itemDetails, setItemDetails] = useState({})
  const viewedId = viewedItem.id
  const needsDetails = !itemDetails[viewedId]
  useEffect(() => {
    if (!needsDetails) return undefined
    let cancelled = false
    itemsApi
      .getById(viewedId)
      .then((full) => {
        if (!cancelled && full)
          setItemDetails((prev) => ({
            ...prev,
            [viewedId]: { imageDataUrls: full.imageDataUrls, place: full.neighborhood || full.street },
          }))
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [needsDetails, viewedId])
  const viewedPlace = itemDetails[viewedId]?.place
  const fullGallery = itemDetails[viewedId]?.imageDataUrls?.length ? itemDetails[viewedId].imageDataUrls : viewedItem.imageDataUrls
  const gallery = fullGallery?.length ? fullGallery : viewedItem.imageDataUrl ? [viewedItem.imageDataUrl] : []

  function showItem(id) {
    setViewedItemId(id === item.id ? null : id)
    setGalleryIndex(0)
  }

  useEffect(() => {
    if (!item.ownerId) return
    let cancelled = false

    itemsApi
      .getByOwner(item.ownerId)
      .then((result) => {
        if (!cancelled) setOtherItems((result ?? []).filter((other) => other.id !== item.id && !other.forSale && !other.sold))
      })
      .catch(() => {
        if (!cancelled) setOtherItems([])
      })

    return () => {
      cancelled = true
    }
  }, [item.ownerId, item.id])

  useEffect(() => {
    return () => {
      if (pollRef.current) clearInterval(pollRef.current)
    }
  }, [])

  // אם אופן הקבלה כבר אינו איסוף עצמי, תשלום במזומן אינו אפשרי - מבטלים בחירה קודמת אם קיימת
  useEffect(() => {
    if (deliveryMethod !== 'SelfPickup' && paymentMethod === 'Cash') {
      setPaymentMethod(null)
    }
  }, [deliveryMethod, paymentMethod])

  useEffect(() => {
    if (paymentMethod !== 'BankTransfer' || ownerPayout) return

    let cancelled = false
    setPayoutLoading(true)
    setPayoutError('')

    itemsApi
      .getOwnerPayout(item.id)
      .then((result) => {
        if (!cancelled) setOwnerPayout(result)
      })
      .catch((err) => {
        if (!cancelled) setPayoutError(err.message)
      })
      .finally(() => {
        if (!cancelled) setPayoutLoading(false)
      })

    return () => {
      cancelled = true
    }
    // payoutLoading מכוון בכוונה מחוץ למערך התלויות - הוספתו הייתה גורמת לאפקט לרוץ שוב מיד
    // כשהוא עצמו קורא ל-setPayoutLoading(true), מה שמפעיל את ה-cleanup (cancelled=true) על
    // הריצה המקורית לפני שהבקשה הראשונה בכלל הספיקה לחזור מהשרת.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [paymentMethod, ownerPayout, item.id])

  function stopPolling() {
    if (pollRef.current) {
      clearInterval(pollRef.current)
      pollRef.current = null
    }
  }

  async function revealAddress() {
    try {
      const fresh = await itemsApi.getAddress(item.id)
      setRevealedAddress(fresh)
    } catch {
      // אם הרענון נכשל - ממשיכים בלי הכתובת המדויקת (העיר עדיין מוצגת)
    }
  }

  // בחירת תאריכים מהלוח: לחיצה ראשונה - תחילת ההשכרה, שנייה - ההחזרה
  function handleCalendarPick(day) {
    if (isOwnItem || callStatus !== null) return
    setDatesChosen(true)
    setCalendarNote('')
    if (isLocation) {
      setStartDate(day)
      setReturnDate(day)
      return
    }
    if (!pickingReturn || day < startDate || (day === startDate && minRentalDays > 1)) {
      setStartDate(day)
      setReturnDate(addDaysIso(day, minRentalDays))
      setReturnDateTouched(false)
      setPickingReturn(true)
      return
    }
    if (daysInRange(startDate, day).some((d) => isDayBusy(d, busyRanges))) {
      setCalendarNote('יש ימים תפוסים בטווח הזה - בחרי טווח אחר')
      return
    }
    setReturnDate(day)
    setReturnDateTouched(true)
    setPickingReturn(false)
  }

  const [dialIn, setDialIn] = useState(0)
  const dialTimerRef = useRef(null)
  useEffect(() => () => clearInterval(dialTimerRef.current), [])

  // הפריט כבר אומת כפנוי לאחרונה במועד הזה (עד 5 שעות לכל צד) - אין צורך בשיחה
  const [recentlyConfirmed, setRecentlyConfirmed] = useState(false)
  const recentKey = `${item.id}|${startDate}|${startTime}|${effectiveReturnDate}|${returnTime}|${unitsOf(item)}`
  useEffect(() => {
    setRecentlyConfirmed(false)
    if (!isAuthenticated || isOwnItem || !rangeValid || !datesChosen || callStatus !== null) return undefined
    let cancelled = false
    const t = setTimeout(() => {
      availabilityCallsApi
        .recent(item.id, { startDate, returnDate: effectiveReturnDate, startTime, returnTime, quantity: unitsOf(item) })
        .then((r) => !cancelled && setRecentlyConfirmed(Boolean(r?.confirmed)))
        .catch(() => {})
    }, 400)
    return () => {
      cancelled = true
      clearTimeout(t)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recentKey, isAuthenticated, isOwnItem, rangeValid, datesChosen, callStatus])

  function handleStartCall() {
    if ((item.quantity || 1) > 1 && mainFreeUnits !== null && mainFreeUnits > 0 && mainFreeUnits < unitsOf(item)) {
      setCallError(`בתאריכים שבחרת פנויות רק ${mainFreeUnits} יחידות - אפשר להקטין את הכמות או לבחור תאריכים אחרים`)
      return
    }
    if (rangeOverlaps(startDate, startTime, effectiveReturnDate, returnTime, busyRanges)) {
      setCallError('הפריט תפוס בחלק מהתאריכים שבחרת. אפשר לראות את הימים הפנויים בלוח הזמנים')
      setCalendarOpen(true)
      return
    }
    if (isStartInPast()) {
      setCallError('מועד ההתחלה שבחרת כבר עבר - נא לבחור תאריך ושעה עתידיים')
      return
    }
    setCallError('')
    if (recentlyConfirmed) {
      placeCall()
      return
    }
    // 3 שניות לפני החיוג - אפשר להתחרט ולבטל
    clearInterval(dialTimerRef.current)
    setDialIn(3)
    let left = 3
    dialTimerRef.current = setInterval(() => {
      left -= 1
      if (left > 0) {
        setDialIn(left)
        return
      }
      clearInterval(dialTimerRef.current)
      setDialIn(0)
      placeCall()
    }, 1000)
  }

  function cancelDial() {
    clearInterval(dialTimerRef.current)
    setDialIn(0)
  }

  async function placeCall() {
    setCallBusy(true)
    try {
      const status = await availabilityCallsApi.start(item.id, {
        startDate,
        returnDate: effectiveReturnDate,
        startTime,
        returnTime,
        totalPrice: itemsSubtotal,
        quantity: unitsOf(item),
        extraItemNames: orderItems.filter((o) => o.id !== item.id).map(nameWithUnits),
      })
      setCallId(status.callId)
      setCallStatus(status.status)
      setCallIsSimulated(status.isSimulated)
      setQuietRetryAt(status.retryAtText ?? '')
      setOwnerEmailed(Boolean(status.ownerEmailed))
      setQuietScheduled(false)
      if (status.status === 'OwnerQuiet') return

      stopPolling()
      pollRef.current = setInterval(async () => {
        try {
          const polled = await availabilityCallsApi.getStatus(status.callId)
          setCallStatus(polled.status)
          if (polled.status !== 'Ringing') {
            stopPolling()
            if (polled.status === 'ConfirmedAvailable') {
              await revealAddress()
            }
          }
        } catch (err) {
          stopPolling()
          setCallError(err.message)
        }
      }, CALL_POLL_INTERVAL_MS)
    } catch (err) {
      setCallError(err.message)
    } finally {
      setCallBusy(false)
    }
  }

  async function handleSimulateDigit(digit) {
    if (!callId) return
    setCallBusy(true)
    try {
      const status = await availabilityCallsApi.respond(callId, digit)
      setCallStatus(status.status)
      stopPolling()
      if (status.status === 'ConfirmedAvailable') {
        await revealAddress()
      }
    } catch (err) {
      setCallError(err.message)
    } finally {
      setCallBusy(false)
    }
  }

  function handleChangeDates() {
    setDetailsDone(false)
    handleRetryCall()
  }

  function handleRetryCall() {
    setCallId(null)
    setCallStatus(null)
    setCallError('')
    setRetryId(null)
    setRetryCanceled(false)
  }

  useEffect(() => {
    if (callStatus !== 'NoAnswer' || !callId || callIsSimulated) return
    availabilityCallsApi
      .retryLater(callId)
      .then((r) => setRetryId(r?.retryId ?? null))
      .catch(() => {})
  }, [callStatus, callId, callIsSimulated])

  async function handleQuietRetry() {
    try {
      const r = await availabilityCallsApi.retryLater(callId, quietNotify)
      setRetryId(r?.retryId ?? null)
      setQuietScheduled(true)
    } catch (err) {
      setCallError(err.message)
    }
  }

  async function handleCancelRetry() {
    if (retryId) {
      try {
        await availabilityCallsApi.cancelRetry(retryId)
      } catch {
        // ממשיכים גם אם הביטול לא נקלט
      }
    }
    setRetryId(null)
    setRetryCanceled(true)
  }

  useEffect(() => {
    if (!confirmedCallId || !isAuthenticated) return
    availabilityCallsApi
      .request(confirmedCallId)
      .then(async (s) => {
        if (s?.status !== 'ConfirmedAvailable') return
        setCallId(confirmedCallId)
        jumpToDetailsRef.current = true
        setCallStatus('ConfirmedAvailable')
        setDatesChosen(true)
        await revealAddress()
      })
      .catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [confirmedCallId, isAuthenticated])

  const isAvailable = callStatus === 'ConfirmedAvailable'
  // הגעה מהקישור "להמשך ההזמנה" - גוללים ישר לשלב הבא אחרי התאריכים
  const jumpToDetailsRef = useRef(false)
  const detailsStepRef = useRef(null)
  useEffect(() => {
    if (!isAvailable || !jumpToDetailsRef.current) return
    jumpToDetailsRef.current = false
    detailsStepRef.current?.scrollIntoView({ block: 'start', behavior: 'smooth' })
  }, [isAvailable])
  const extrasLocked = isAvailable || callStatus === 'Ringing'
  const currentStep = !isAvailable ? 1 : detailsDone ? 3 : 2

  useEffect(() => {
    if (deliveryMethod !== 'SelfPickup' && paymentMethod === 'Cash') setPaymentMethod(null)
  }, [deliveryMethod, paymentMethod])

  const isExpressSelected = showExpressOption && expressDelivery
  // כל עוד לא התקבלה הצעת מחיר אמיתית למשלוח - אין מחיר משלוח ידוע, ולכן גם אי אפשר להמשיך
  // (ראו deliveryQuoteReady) - לא מציגים סכום מנוחש שעלול לא להתאים למה שבאמת ייגבה
  const deliveryFee =
    deliveryMethod === 'Delivery' && shippingQuote
      ? isExpressSelected
        ? shippingQuote.expressPrice
        : shippingQuote.regularPrice
      : 0
  const returnFee = returnMethod === 'CourierPickup' && shippingQuote ? shippingQuote.returnPrice ?? 0 : 0
  const itemsSubtotal = isLocation
    ? locationPrice
    : orderItems.reduce((sum, orderItem) => sum + chargedDays * orderItem.dailyPrice * unitsOf(orderItem), 0)
  const mainFreeUnits = rangeValid ? freeUnits(schedule, startDate, startTime, effectiveReturnDate, returnTime) : null
  const totalPrice = itemsSubtotal + deliveryFee + returnFee
  const shippingFee = deliveryFee + returnFee
  const shippingByCard = Boolean(shipPay?.enabled) && shippingFee > 0 && paymentMethod !== 'CreditCard'
  const needsShippingCard = shippingByCard && !shipPay?.hasCard
  // האם המשלוח יגיע עד השעה שביקשה: חוסמים המשך אם חברת השילוח אינה יכולה לעמוד בכך
  const arrivalCheck =
    deliveryMethod === 'Delivery' && shippingQuote
      ? isExpressSelected
        ? shippingQuote.expressArrival
        : shippingQuote.regularArrival
      : null
  const returnArrival = returnMethod === 'CourierPickup' && shippingQuote ? shippingQuote.returnArrival : null
  const arrivalBlocked = Boolean((arrivalCheck && arrivalCheck.possible === false) || (returnArrival && returnArrival.possible === false))
  const deliveryQuoteReady = (!needsDeliveryAddress || Boolean(shippingQuote)) && !arrivalBlocked
  const canConfirmDirectly =
    (paymentMethod === 'Cash' || (paymentMethod === 'BankTransfer' && ownerPayout?.isConfigured && proof)) &&
    (!needsDeliveryAddress || isDeliveryAddressComplete) &&
    isReturnAddressComplete &&
    !startInPast &&
    deliveryQuoteReady

  // פרטי האיסוף העצמי (רק כשנבחר איסוף עצמי): הכתובת המלאה נחשפת לאחר אימות הזמינות מול חבר הקהילה
  const pickupAddress = revealedAddress?.address || ''
  const pickupCity = revealedAddress?.city || item.city || ''
  const pickupPlace = [pickupAddress, pickupCity].filter(Boolean).join(', ')
  const pickup =
    deliveryMethod === 'SelfPickup'
      ? {
          place: pickupPlace,
          notes: revealedAddress?.addressNotes || '',
          hasFullAddress: Boolean(pickupAddress),
          date: startDate,
          time: startTime,
        }
      : null

  function buildCommonFields() {
    return {
      borrowerId: user.id,
      // צהרי UTC של אותו תאריך לוח - כך התאריך זהה בשרת בלי קשר לאזור הזמן של המכשיר
      startDate: `${startDate}T12:00:00Z`,
      rentalDays: Math.max(1, rentalDays),
      sameDayReturn,
      startTime,
      deliveryMethod,
      expressDelivery: showExpressOption && expressDelivery,
      returnMethod,
      returnTime,
      termsAccepted,
      phoneConfirmed,
      deliveryAddress: needsDeliveryAddress
        ? {
            city: deliveryCity.trim(),
            street: deliveryStreet.trim(),
            houseNumber: deliveryHouseNumber.trim(),
            floor: deliveryFloor.trim() || undefined,
            apartment: deliveryApartment.trim() || undefined,
          }
        : undefined,
      returnPickupAddress: usesOtherReturnAddress
        ? {
            city: returnCity.trim(),
            street: returnStreet.trim(),
            houseNumber: returnHouseNumber.trim(),
            floor: returnFloor.trim() || undefined,
            apartment: returnApartment.trim() || undefined,
          }
        : undefined,
    }
  }

  function buildLoanDraft() {
    return isBatchOrder
      ? {
          ...buildCommonFields(),
          itemIds: orderItems.map((orderItem) => orderItem.id),
          quantities: Object.fromEntries(orderItems.map((o) => [o.id, unitsOf(o)])),
        }
      : { ...buildCommonFields(), itemId: item.id, quantity: unitsOf(item) }
  }

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

  function buildConfirmDraft() {
    return {
      ...buildLoanDraft(),
      paymentMethod,
      ...(paymentMethod === 'BankTransfer'
        ? { paymentProofDataUrl: proof?.dataUrl, paymentProofFileName: proof?.fileName }
        : {}),
    }
  }

  async function handleShippingCardFirst() {
    if (isStartInPast()) {
      setSubmitError('מועד ההתחלה שבחרת כבר עבר - נא לבחור תאריך ושעה עתידיים')
      return
    }
    setSubmitError('')
    setSubmitting(true)
    try {
      const pending = { draft: buildConfirmDraft(), isBatch: isBatchOrder }
      try {
        sessionStorage.setItem('pr.shippingDraft', JSON.stringify(pending))
      } catch {
        throw new Error('לא ניתן להמשיך עם הקובץ שהועלה - נא לבחור אמצעי תשלום אחר או קובץ קטן יותר')
      }
      const r = await shippingPayApi.start(shippingFee, true, pending)
      window.location.href = r.redirectUrl
    } catch (err) {
      setSubmitError(err.message)
      setSubmitting(false)
    }
  }

  async function handleConfirm() {
    if (isStartInPast()) {
      setSubmitError('מועד ההתחלה שבחרת כבר עבר - נא לבחור תאריך ושעה עתידיים')
      return
    }
    setSubmitError('')
    setSubmitting(true)
    try {
      const draft = buildConfirmDraft()
      const created = isBatchOrder ? await loansApi.createBatch(draft) : await loansApi.create(draft)
      setSuccessMessage(
        isBatchOrder
          ? `יש! ההזמנה נפתחה (${countText}) 🎉 אפשר לעקוב אחריה בכל רגע באזור האישי`
          : 'יש! ההשכרה נפתחה 🎉 אפשר לעקוב אחריה בכל רגע באזור האישי',
      )
      setSuccessTracking(isBatchOrder ? created[0] : created)
      onRented?.()
    } catch (err) {
      setSubmitError(err.status === 409 ? `הפריט נתפס ממש עכשיו: ${err.message}` : err.message)
    } finally {
      setSubmitting(false)
    }
  }

  async function handleCardPayment() {
    if (isStartInPast()) {
      setSubmitError('מועד ההתחלה שבחרת כבר עבר - נא לבחור תאריך ושעה עתידיים')
      return
    }
    setSubmitError('')
    setSubmitting(true)
    try {
      const pending = { draft: buildConfirmDraft(), isBatch: isBatchOrder }
      try {
        sessionStorage.setItem('pr.orderDraft', JSON.stringify(pending))
      } catch {
        throw new Error('לא ניתן להמשיך לתשלום בדפדפן הזה - נא לנסות שוב או לבחור אמצעי תשלום אחר')
      }
      const r = await orderPayApi.start(totalPrice, saveCardLater, pending)
      window.location.href = r.redirectUrl
    } catch (err) {
      setSubmitError(err.message)
      setSubmitting(false)
    }
  }

  const hasPhone = String(user?.phone ?? '').replace(/\D/g, '').length >= 9
  const joinAddress = (street, number, floor, apartment, city) =>
    `${street} ${number}${floor ? `, קומה ${floor}` : ''}${apartment ? `, ${apartment}` : ''}, ${city}`
  // לוח השנה של הפריט (ימים תפוסים באפור; בחירת תחילה וסיום בלחיצה)
  const calendarBlock = (
    <>
              <MonthCalendar
                dayClass={(day) =>
                  [
                    isDayBusy(day, calendarBusy) ? 'is-busy' : '',
                    !isOwnItem && day === startDate ? 'is-selected' : '',
                    !isOwnItem && day > startDate && day <= returnDate && !pickingReturn ? 'is-in-range' : '',
                  ].join(' ')
                }
                isDisabled={(day) => isOwnItem || isDayBusy(day, calendarBusy)}
                onDayClick={handleCalendarPick}
                dayTitle={(day) => (isDayBusy(day, calendarBusy) ? 'תפוס' : undefined)}
              />
              <p className="field-hint">
                {isOwnItem
                  ? 'את ההשכרות והחסימות של הפריט אפשר לנהל באזור האישי, בלחיצה על "לוח זמנים" ליד הפריט.'
                  : pickingReturn
                    ? 'בחרי עכשיו את יום ההחזרה'
                    : 'ימים באפור תפוסים. לחצי על יום פנוי כדי לבחור את תחילת ההשכרה.'}
              </p>
              {calendarNote && <p className="form-error">{calendarNote}</p>}
    </>
  )
  const busyNowNote =
    viewedItem.status === 'Rented' ? (
      <p className="field-hint rent-busy-hint">
        {viewedItem.busyUntil && wallMs(viewedItem.busyUntil) > wallMs(israelNowWall())
          ? `הפריט מושכר כרגע - יתפנה ב-${formatDateTime(viewedItem.busyUntil)}. אפשר להזמין לתאריכים פנויים אחרים.`
          : 'הפריט מושכר כרגע - ממתינים להחזרתו. אפשר להזמין לתאריכים פנויים אחרים.'}
      </p>
    ) : null
  const shortDate = (iso) => (iso ? `${Number(iso.slice(8, 10))}.${Number(iso.slice(5, 7))}` : '')

  const deliveryAddressText = joinAddress(deliveryStreet, deliveryHouseNumber, deliveryFloor, deliveryApartment, deliveryCity)
  const returnAddressText = usesOtherReturnAddress
    ? joinAddress(returnStreet, returnHouseNumber, returnFloor, returnApartment, returnCity)
    : deliveryAddressText

  return (
    <Modal title={viewedItem.name} eyebrow={categoryTrailForItem(viewedItem, categoryChains)} onClose={onClose}>
      <div className="rent-modal">
        <div className="rent-modal-media">
          {gallery.length > 0 ? (
            <img key={galleryIndex} src={gallery[galleryIndex]} alt={viewedItem.name} className="rent-gallery-image" />
          ) : (
            <CategoryIcon className="item-details-icon" texts={iconTextsForItem(viewedItem, categoryChains)} />
          )}
          {gallery.length > 1 && (
            <>
              <button
                type="button"
                className="rent-gallery-arrow rent-gallery-prev"
                onClick={() => setGalleryIndex((galleryIndex + gallery.length - 1) % gallery.length)}
                aria-label="התמונה הקודמת"
              >
                ‹
              </button>
              <button
                type="button"
                className="rent-gallery-arrow rent-gallery-next"
                onClick={() => setGalleryIndex((galleryIndex + 1) % gallery.length)}
                aria-label="התמונה הבאה"
              >
                ›
              </button>
              <div className="rent-gallery-dots">
                {gallery.map((_, i) => (
                  <button
                    type="button"
                    key={i}
                    className={`rent-gallery-dot ${i === galleryIndex ? 'rent-gallery-dot-active' : ''}`}
                    onClick={() => setGalleryIndex(i)}
                    aria-label={`תמונה ${i + 1}`}
                  />
                ))}
              </div>
            </>
          )}
        </div>

        {viewedItem.city && (
          <span className="item-card-city rent-modal-city">
            <MapPinIcon className="item-card-city-icon" />
            {viewedPlace ? `${viewedPlace}, ${viewedItem.city}` : viewedItem.city}
          </span>
        )}

        <ItemExtras item={viewedItem} isOwnItem={isOwnItem} />


        <p className="item-details-desc">{viewedItem.description}</p>

        <div className="item-price-big">
          {viewedItem.addVat && (
            <span className="price-vat-base">₪{beforeVat(viewedItem.dailyPrice)} + מע״מ · סה״כ </span>
          )}
          {viewedItem.isLocation ? (
            <>
              <span className="price-tag">₪{viewedItem.hourlyPrice}</span>
              <span className="item-price-unit"> / שעה</span>
              <span className="item-price-hourly-note"> (יום שלם ₪{viewedItem.dailyPrice})</span>
            </>
          ) : (
            <>
              <span className="price-tag">₪{viewedItem.dailyPrice}</span>
              <span className="item-price-unit"> / יום</span>
            </>
          )}
          {viewedItem.hourlyPrice && !viewedItem.isLocation && (
            <span className="item-price-hourly-note"> (או ₪{viewedItem.hourlyPrice} לשעה{viewedItem.addVat ? ' כולל מע״מ' : ''})</span>
          )}
          {viewedItem.quantity > 1 && <span className="item-units-note">{viewedItem.quantity} יחידות</span>}
        </div>

        {isViewingOther && !isOwnItem && isAuthenticated && !successMessage && (
          <div className="viewing-bar">
            <span>
              מציגים עכשיו פריט נוסף מהצלמת בקהילה. כל הבחירות שלך (תאריכים, שעות, קבלה והחזרה) נשמרות
              ומשותפות לכל הפריטים בהזמנה.
            </span>
            <div className="viewing-bar-actions">
              <button
                type="button"
                className={`btn btn-small ${selectedOtherIds.includes(viewedItem.id) ? 'btn-ghost' : 'btn-primary'}`}
                onClick={() => toggleOtherItem(viewedItem)}
                disabled={extrasLocked}
              >
                {selectedOtherIds.includes(viewedItem.id) ? 'הסרה מההזמנה' : 'הוספה להזמנה'}
              </button>
              <button type="button" className="btn btn-ghost btn-small" onClick={() => showItem(item.id)}>
                חזרה ל{item.name}
              </button>
            </div>
          </div>
        )}


        {(isOwnItem || !isAuthenticated) && (
          <div className="item-schedule">
            <button
              type="button"
              className="section-toggle item-schedule-toggle"
              onClick={() => setCalendarOpen((v) => !v)}
              aria-expanded={calendarOpen}
            >
              <span>📅 תאריכים פנויים</span>
              <span className={`section-toggle-arrow ${calendarOpen ? 'is-open' : ''}`} aria-hidden="true">
                ‹
              </span>
            </button>
            {calendarOpen && (
              <>
                {busyNowNote}
                {calendarBlock}
              </>
            )}
          </div>
        )}

        {isOwnItem ? (
          <>
            <p className="state-message">זה פריט שלך 😊</p>
            <button
              type="button"
              className="btn btn-primary btn-small own-item-edit"
              onClick={() => navigate(`/profile?edit=${encodeURIComponent(viewedItem.id)}`)}
            >
              לעריכת הפריט
            </button>
          </>
        ) : !isAuthenticated ? (
          <>
            <p className="state-message">התחברי כדי לשכור את הפריט</p>
            <button type="button" className="btn btn-primary" onClick={() => navigate('/login', loginReturnTo ? { state: { from: loginReturnTo } } : undefined)}>
              התחברות
            </button>
            {!isOwnItem && <AskOwner key={viewedItem.id} item={viewedItem} />}
          </>
        ) : !hasPhone && !successMessage ? (
          <>
            <p className="state-message">כדי להזמין יש להוסיף מספר טלפון תקין בחשבון. הוא משמש לשיחות דרך common.</p>
            <Link to="/profile" className="btn btn-primary" onClick={onClose}>
              לעדכון הפרטים באזור האישי
            </Link>
          </>
        ) : successMessage ? (
          <div className="rent-form">
            <p className="form-success">{successMessage}</p>
            {pickup && <PickupDetails {...pickup} />}
            {successTracking?.id && (
              <p>
                <Link to={`/order/${successTracking.id}`} className="btn btn-primary" onClick={onClose}>
                  למסך ההזמנה: פרטים, זמן הגעה ומעקב משלוח
                </Link>
              </p>
            )}
            {successTracking?.id && (
              <p className="payment-status-note">מעבירות אותך אוטומטית למסך ההזמנה...</p>
            )}
            {(successTracking?.outboundTrackingId || successTracking?.returnTrackingId) && (
              <dl className="item-meta">
                {successTracking.outboundTrackingId && (
                  <div>
                    <dt>משלוח אליך</dt>
                    <dd><ShipmentTracking loan={successTracking} direction="out" /></dd>
                  </div>
                )}
                {successTracking.returnTrackingId && (
                  <div>
                    <dt>איסוף החזרה</dt>
                    <dd><ShipmentTracking loan={successTracking} direction="return" /></dd>
                  </div>
                )}
              </dl>
            )}
          </div>
        ) : (
          <>
            <ol className="rent-steps" aria-label="שלבי ההזמנה">
              {[isLocation ? 'מועד וזמינות' : 'תאריכים וזמינות', isLocation ? 'הגעה' : 'קבלה והחזרה', 'תשלום'].map((label, i) => (
                <li
                  key={label}
                  className={i + 1 < currentStep ? 'is-done' : i + 1 === currentStep ? 'is-current' : ''}
                  aria-current={i + 1 === currentStep ? 'step' : undefined}
                >
                  <span className="rent-step-num">{i + 1}</span>
                  {label}
                </li>
              ))}
            </ol>

            <section className="rent-step" aria-label="תאריכים וזמינות">
              <h3 className="rent-step-title">1. {isLocation ? 'מועד וזמינות' : 'תאריכים וזמינות'}</h3>
              <fieldset className="step-fields" disabled={callStatus === 'Ringing' || isAvailable}>
            <div className="dates-tab">
              <button
                type="button"
                className="section-toggle item-schedule-toggle"
                onClick={() => setCalendarOpen((v) => !v)}
                aria-expanded={calendarOpen}
              >
                <span>
                  📅 {isLocation ? 'בחירת יום ושעות' : 'בחירת תאריכים'}
                  <span className="dates-tab-summary">
                    {' '}
                    ·{' '}
                    {isLocation
                      ? `${shortDate(startDate)} ${startTime}-${returnTime}`
                      : `${shortDate(startDate)} ${startTime} - ${shortDate(effectiveReturnDate)} ${returnTime}`}
                  </span>
                </span>
                <span className={`section-toggle-arrow ${calendarOpen ? 'is-open' : ''}`} aria-hidden="true">
                  ‹
                </span>
              </button>
              {calendarOpen && (
                <div className="dates-tab-body">
                  {busyNowNote}
                  {calendarBlock}
                  <div className="rent-dates-row">
                    <div>
                      <label htmlFor="rentStartDate">{isLocation ? 'תאריך' : 'תאריך התחלה'}</label>
                      <input
                        id="rentStartDate"
                        type="date"
                        value={startDate}
                        min={todayIso()}
                        onChange={(e) => handleStartDateChange(e.target.value)}
                        disabled={callStatus === 'Ringing'}
                      />
                    </div>
                    <div>
                      <label htmlFor="rentStartTime">{isLocation ? 'משעה' : 'שעת התחלה'}</label>
                      <input
                        id="rentStartTime"
                        type="time"
                        value={startTime}
                        min={isStartingToday ? nowTimeIso() : undefined}
                        onChange={(e) => applyStartTime(e.target.value)}
                        disabled={callStatus === 'Ringing'}
                      />
                    </div>
                  </div>

                  {isLocation && takenHours.length > 0 && (
                    <p className="field-hint">שעות תפוסות ביום הזה: {takenHours.join(', ')}</p>
                  )}
                  <div className="rent-dates-row">
                    <div hidden={isLocation}>
                      <label htmlFor="rentReturnDate">תאריך החזרה</label>
                      <input
                        id="rentReturnDate"
                        type="date"
                        value={returnDate}
                        min={startDate ? (minRentalDays === 1 ? startDate : addDaysIso(startDate, minRentalDays)) : todayIso()}
                        onChange={(e) => handleReturnDateChange(e.target.value)}
                        disabled={callStatus === 'Ringing'}
                      />
                    </div>
                    <div>
                      <label htmlFor="rentReturnTime">{isLocation ? 'עד שעה' : 'שעת החזרה'}</label>
                      <input
                        id="rentReturnTime"
                        type="time"
                        value={returnTime}
                        onChange={(e) => handleReturnTimeChange(e.target.value)}
                        disabled={callStatus === 'Ringing'}
                      />
                    </div>
                  </div>
                </div>
              )}
            </div>

            {!rangeValid && (
              <p className="field-hint field-hint-error">
                {isLocation
                  ? locationHours <= 0
                    ? 'שעת הסיום צריכה להיות אחרי שעת ההתחלה.'
                    : `מינימום הזמנה למקום הזה: ${minHours} שעות.`
                  : calendarDays === 0 && minRentalDays === 1
                  ? 'בהחזרה באותו יום - שעת ההחזרה צריכה להיות אחרי שעת ההתחלה.'
                  : calendarDays === 0
                    ? `משך ההשכרה המינימלי לפריט זה הוא ${minRentalDays} ימים.`
                    : 'תאריך ההחזרה חייב להיות אחרי תאריך ההתחלה.'}
              </p>
            )}
            {startInPast && (
              <p className="field-hint field-hint-error" role="alert">
                אי אפשר להזמין למועד שכבר עבר - נא לבחור תאריך ושעה עתידיים.
              </p>
            )}
            {returnWasExtended && !isLocation && (
              <p className="field-hint">
                שעת ההחזרה מאוחרת משעת ההתחלה, ולכן נספר יום נוסף - ההחזרה עד {formatDateHe(effectiveReturnDate)}{' '}
                בשעה {returnTime}.
              </p>
            )}

            {item.minRentalUnit === 'Days' && item.minRentalAmount > 1 && (
              <p className="field-hint">משך ההשכרה המינימלי לפריט זה: {item.minRentalAmount} ימים</p>
            )}

              </fieldset>

              {(isBatchOrder || item.quantity > 1) && (
                <div className="order-lines">
                  <p className="order-lines-title">מה בהזמנה:</p>
                  <ul>
                    {orderItems.map((o) => {
                      const isMain = o.id === item.id
                      const max = isMain ? mainFreeUnits ?? o.quantity : o.quantity
                      return (
                        <li key={o.id} className="order-line">
                          <span className="order-line-name">{o.name}</span>
                          {o.quantity > 1 ? (
                            <span className="order-line-units">
                              <span className="qty-stepper qty-stepper-small">
                                <button
                                  type="button"
                                  onClick={() => setUnits(o, unitsOf(o) - 1)}
                                  disabled={callStatus !== null || unitsOf(o) <= 1}
                                  aria-label={`פחות ${o.name}`}
                                >
                                  −
                                </button>
                                <span aria-live="polite">{unitsOf(o)}</span>
                                <button
                                  type="button"
                                  onClick={() => setUnits(o, unitsOf(o) + 1)}
                                  disabled={callStatus !== null || unitsOf(o) >= max}
                                  aria-label={`עוד ${o.name}`}
                                >
                                  +
                                </button>
                              </span>
                              <span className="order-line-of">מתוך {o.quantity}</span>
                            </span>
                          ) : (
                            <span className="order-line-of">יחידה אחת</span>
                          )}
                          <span className="order-line-price">₪{(chargedDays * o.dailyPrice * unitsOf(o)).toFixed(2)}</span>
                          {isMain && o.quantity > 1 && mainFreeUnits !== null && mainFreeUnits < o.quantity && (
                            <span className="field-hint order-line-hint">
                              {mainFreeUnits === 0
                                ? 'בתאריכים שבחרת כל היחידות כבר מוזמנות'
                                : `בתאריכים שבחרת אפשר להזמין עד ${mainFreeUnits} ${mainFreeUnits === 1 ? 'יחידה' : 'יחידות'}`}
                            </span>
                          )}
                        </li>
                      )
                    })}
                  </ul>
                </div>
              )}

              <p className="rent-total-price">
                מחיר ההשכרה: <span className="rent-total-amount">₪{itemsSubtotal.toFixed(2)}</span>
                {rangeValid && (
                  <span className="item-price-hourly-note">
                    {' '}
                    ({isLocation ? hoursText(locationHours) : daysText})
                  </span>
                )}
                {(isBatchOrder || totalUnits > 1) && <span className="item-price-hourly-note"> ({countText})</span>}
              </p>

            {callError && <p className="form-error">{callError}</p>}

            {callStatus === null && dialIn > 0 && (
              <div className="call-panel call-panel-ringing">
                <PhoneRingingIcon className="call-ringing-icon" />
                <p className="call-status-text">מתקשרים אל הצלמת בקהילה לאימות זמינות...</p>
                <button type="button" className="btn btn-ghost btn-small" onClick={cancelDial}>
                  ביטול השיחה
                </button>
              </div>
            )}
            {callStatus === null && dialIn === 0 && recentlyConfirmed && (
              <p className="form-success">הפריט אומת כפנוי לתאריכים האלה ✓ - אפשר להמשיך</p>
            )}
            {callStatus === null && dialIn === 0 && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleStartCall}
                disabled={callBusy || !rangeValid || startInPast || !datesChosen}
              >
                {callBusy ? 'מתחבר...' : recentlyConfirmed ? 'המשך להזמנה' : 'אימות זמינות מול הצלמת בקהילה'}
              </button>
            )}
            {callStatus === null && !datesChosen && (
              <p className="field-hint">כדי להמשיך - בחרי תאריכים ב"בחירת תאריכים"</p>
            )}
            {!isOwnItem && <AskOwner key={viewedItem.id} item={viewedItem} />}

            {callStatus === 'Ringing' && (
              <div className="call-panel call-panel-ringing">
                <PhoneRingingIcon className="call-ringing-icon" />
                <p className="call-status-text">
                  מתקשרים אל הצלמת בקהילה לאימות זמינות...
                </p>
                {callIsSimulated ? (
                  <>
                    <p className="call-sim-note">
                      זוהי הדמיה של שיחה טלפונית אוטומטית - בסביבת פיתוח אפשר לדמות את תגובת הצלמת בקהילה ישירות:
                    </p>
                    <div className="call-sim-buttons">
                      <button
                        type="button"
                        className="btn btn-primary btn-small"
                        onClick={() => handleSimulateDigit(1)}
                        disabled={callBusy}
                      >
                        1 - הפריט פנוי
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => handleSimulateDigit(2)}
                        disabled={callBusy}
                      >
                        2 - הפריט תפוס
                      </button>
                    </div>
                  </>
                ) : null}
              </div>
            )}

            {callStatus === 'ConfirmedUnavailable' && (
              <p className="state-message state-error">
                {item.name} אינו פנוי לתאריך שביקשת. בקשי פריט אחר או תאריך אחר ונסי שוב
              </p>
            )}

            {callStatus === 'OwnerQuiet' &&
              (quietScheduled && !retryCanceled ? (
                <>
                  <p className="state-message">
                    ✓ נתקשר לצלמת {quietRetryAt || 'בבוקר'} ונעדכן אותך {quietNotify === 'phone' ? 'בטלפון' : 'במייל'}.
                  </p>
                  <button type="button" className="btn btn-ghost btn-small" onClick={handleCancelRetry}>
                    ביטול שיחת אימות הזמינות
                  </button>
                </>
              ) : retryCanceled ? (
                <p className="state-message">שיחת אימות הזמינות בוטלה.</p>
              ) : (
                <div className="quiet-owner-box">
                  <p className="state-message state-error">
                    {ownerEmailed
                      ? 'המשכירה לא זמינה בשעות אלו. שלחנו לה מייל, ואם לא תענה - נתקשר אליה מיד בבוקר ונעדכן אותך.'
                      : `המשכירה לא זמינה בשעות אלו. אפשר להתקשר אליה ${quietRetryAt || 'בבוקר'}.`}
                  </p>
                  <div className="delivery-choice" role="radiogroup" aria-label="איך לעדכן אותך">
                    <label>
                      <input type="radio" name="quietNotify" checked={quietNotify === 'email'} onChange={() => setQuietNotify('email')} />
                      עדכון במייל
                    </label>
                    <label>
                      <input type="radio" name="quietNotify" checked={quietNotify === 'phone'} onChange={() => setQuietNotify('phone')} />
                      עדכון בטלפון
                    </label>
                  </div>
                  <button type="button" className="btn btn-primary btn-small" onClick={handleQuietRetry}>
                    נסו שוב מיד בבוקר ועדכנו אותי
                  </button>
                </div>
              ))}

            {callStatus === 'NoAnswer' && (callIsSimulated || retryCanceled ? (
              <p className="state-message state-error">
                {retryCanceled ? 'הניסיון החוזר בוטל.' : 'הצלמת בקהילה לא ענתה לשיחה.'}
              </p>
            ) : (
              <>
                <p className="state-message state-error">
                  הצלמת בקהילה לא ענתה לשיחה. ננסה לאמת זמינות שוב בעוד 12 דקות ונודיע לך במייל אם הפריט פנוי לתאריך שביקשת
                </p>
                <button type="button" className="btn btn-ghost btn-small" onClick={handleCancelRetry}>
                  ביטול שיחת אימות זמינות בעוד 12 דקות
                </button>
              </>
            ))}

        {otherItems.length > 0 && !isLocation && !isOwnItem && isAuthenticated && !successMessage && (
          <div className="other-owner-items">
            <h3>עוד ציוד מהצלמת הזו - אפשר לצרף להזמנה אחת</h3>
            {extrasLocked && (
              <p className="field-hint">
                אי אפשר לצרף פריטים אחרי אימות הזמינות. כדי לצרף פריט נוסף, לחצי "שינוי תאריכים" ואמתי שוב.
              </p>
            )}
            <div className="other-owner-items-grid">
              {otherItems.slice(0, showAllOthers ? undefined : 4).map((other) => {
                const isSelected = selectedOtherIds.includes(other.id)
                return (
                  <div
                    key={other.id}
                    className={`other-owner-item${isSelected ? ' selected' : ''}${viewedItem.id === other.id ? ' viewing' : ''}`}
                  >
                    {isSelected && <span className="other-owner-item-check">✓</span>}
                    <button
                      type="button"
                      className="other-owner-item-view"
                      onClick={() => showItem(other.id)}
                      aria-label={`הצגת ${other.name}`}
                    >
                      <span className="other-owner-item-media">
                        {other.imageDataUrl ? (
                          <img src={other.imageDataUrl} alt={other.name} loading="lazy" decoding="async" />
                        ) : (
                          <CategoryIcon texts={iconTextsForItem(other, categoryChains)} />
                        )}
                      </span>
                      <span className="other-owner-item-name">{other.name}</span>
                      <span className="other-owner-item-price">
                        <Price amount={other.dailyPrice} vat={other.addVat} compact />
                      </span>
                      {other.quantity > 1 && <span className="other-owner-item-units">{other.quantity} יחידות</span>}
                    </button>
                    <div className="other-owner-item-actions">
                      <button type="button" className="other-owner-item-show" onClick={() => showItem(other.id)}>
                        הצגה
                      </button>
                      <button
                        type="button"
                        className="other-owner-item-toggle"
                        onClick={() => toggleOtherItem(other)}
                        disabled={extrasLocked}
                        aria-pressed={isSelected}
                      >
                        {isSelected ? 'בהזמנה ✓' : '+ צירוף'}
                      </button>
                    </div>
                  </div>
                )
              })}
            </div>
            {!showAllOthers && otherItems.length > 4 && (
              <button type="button" className="link-button" onClick={() => setShowAllOthers(true)}>
                הצגת כל {otherItems.length} הפריטים שלה
              </button>
            )}
            {isBatchOrder && (
              <p className="other-owner-items-total">
                נבחרו {countText} מהצלמת הזו - יפתחו יחד כהזמנה אחת עם עלות
                משלוח בודדת.
              </p>
            )}
          </div>
        )}
              {isAvailable && (
                <>
                  <p className="form-success">הצלמת בקהילה אישרה בטלפון שהפריט פנוי!</p>
                  <button type="button" className="btn btn-ghost btn-small" onClick={handleChangeDates}>
                    שינוי תאריכים
                  </button>
                </>
              )}
            </section>

            {isAvailable && (
              <section className="rent-step" aria-label="קבלה והחזרה" ref={detailsStepRef}>
                <h3 className="rent-step-title">2. {isLocation ? 'הגעה למקום' : 'אופן קבלה והחזרה'}</h3>
                {isLocation && (
                  <p className="field-hint">
                    ההגעה למקום עצמאית. הכתובת המלאה תופיע אחרי סיום ההזמנה. מחיר: ₪{locationPrice.toFixed(2)} (
                    {hoursText(locationHours)}).
                  </p>
                )}
                <fieldset className="step-fields" disabled={detailsDone} hidden={isLocation}>
            <fieldset className="delivery-choice-block">
<legend>אופן קבלת הפריט</legend>
<div className="delivery-choice">
              <label>
                <input
                  type="radio"
                  name="deliveryMethod"
                  value="SelfPickup"
                  checked={deliveryMethod === 'SelfPickup'}
                  onChange={() => setDeliveryMethod('SelfPickup')}
                />
                איסוף עצמי
              </label>
              <label>
                <input
                  type="radio"
                  name="deliveryMethod"
                  value="Delivery"
                  checked={deliveryMethod === 'Delivery'}
                  onChange={() => setDeliveryMethod('Delivery')}
                />
                משלוח{shippingQuote ? ` (₪${shippingQuote.regularPrice})` : ''}
              </label>
            </div>
</fieldset>

            {showExpressOption && (
              <label className="checkbox-row express-option">
                <input
                  type="checkbox"
                  checked={expressDelivery}
                  onChange={(e) => setExpressDelivery(e.target.checked)}
                  disabled={callStatus === 'Ringing'}
                />
                {expressDay ? (
                  <>
                    משלוח אקספרס{shippingQuote ? ` (₪${shippingQuote.expressPrice})` : ''} - קבלת הפריט תוך
                    זמן קצר, כיוון שההשכרה מתחילה היום
                  </>
                ) : (
                  <>
                    משלוח אקספרס{shippingQuote ? ` (₪${shippingQuote.expressPrice})` : ''} - משלוח רגיל לא זמין לכתובת או
                    לתאריך האלה, ולכן השליח יוזמן ביום ההשכרה
                  </>
                )}
                {shippingQuote?.expressArrival?.estimatedArrival && (
                  <> · הגעה משוערת בסביבות {shippingQuote.expressArrival.estimatedArrival}</>
                )}
              </label>
            )}

            <fieldset className="delivery-choice-block">
<legend>אופן ההחזרה</legend>
<div className="delivery-choice">
              <label>
                <input
                  type="radio"
                  name="returnMethod"
                  value="SelfDropoff"
                  checked={returnMethod === 'SelfDropoff'}
                  onChange={() => setReturnMethod('SelfDropoff')}
                  disabled={callStatus === 'Ringing'}
                />
                מסירה עצמית למשכיר
              </label>
              <label>
                <input
                  type="radio"
                  name="returnMethod"
                  value="CourierPickup"
                  checked={returnMethod === 'CourierPickup'}
                  onChange={() => setReturnMethod('CourierPickup')}
                  disabled={callStatus === 'Ringing'}
                />
                איסוף שליח{shippingQuote && returnMethod === 'CourierPickup' ? ` (₪${shippingQuote.returnPrice})` : ''}
              </label>
            </div>
</fieldset>

            {needsDeliveryAddress && (
              <div className="courier-fee-line" role="status">
                <strong>תוספת תשלום לשליח</strong>
                {shippingQuote ? (
                  <span>
                    {[
                      deliveryMethod === 'Delivery' ? `משלוח: ₪${deliveryFee}` : null,
                      returnMethod === 'CourierPickup' ? `איסוף בהחזרה: ₪${returnFee}` : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  </span>
                ) : (
                  <span>{shippingQuoteLoading ? 'מחשבים את המחיר...' : 'הסכום יופיע מיד אחרי מילוי הכתובת'}</span>
                )}
              </div>
            )}

            {needsDeliveryAddress && (
              <fieldset className="delivery-address-fields">
                <legend>
                  {deliveryMethod === 'Delivery' ? 'כתובת למשלוח' : 'כתובת לאיסוף השליח בהחזרה'}
                </legend>

                <div className="rent-dates-row">
                  <CityStreetFields
                    idPrefix="delivery"
                    city={deliveryCity}
                    street={deliveryStreet}
                    onCityChange={setDeliveryCity}
                    onStreetChange={setDeliveryStreet}
                    onValidChange={setDeliveryAddressValid}
                    disabled={callStatus === 'Ringing'}
                  />
                </div>

                <div className="rent-dates-row">
                  <div>
                    <label htmlFor="deliveryHouseNumber">מספר בית</label>
                    <input
                      id="deliveryHouseNumber"
                      type="text"
                      value={deliveryHouseNumber}
                      onChange={(e) => setDeliveryHouseNumber(e.target.value)}
                      disabled={callStatus === 'Ringing'}
                      required
                    />
                  </div>
                  <div>
                    <label htmlFor="deliveryFloor">קומה</label>
                    <input
                      id="deliveryFloor"
                      type="text"
                      value={deliveryFloor}
                      onChange={(e) => setDeliveryFloor(e.target.value)}
                      disabled={callStatus === 'Ringing'}
                      required
                    />
                  </div>
                </div>

                <div className="rent-dates-row">
                  <div>
                    <label htmlFor="deliveryApartment">מספר דירה / שם משפחה</label>
                    <input
                      id="deliveryApartment"
                      type="text"
                      value={deliveryApartment}
                      onChange={(e) => setDeliveryApartment(e.target.value)}
                      disabled={callStatus === 'Ringing'}
                      required
                    />
                  </div>
                </div>

                {isDeliveryStreetComplete && !isDeliveryAddressComplete && (
                  <p className="field-hint">יש להשלים קומה ומספר דירה או שם משפחה, כדי שהשליח ימצא את הדלת.</p>
                )}

                {isDeliveryAddressComplete && (
                  <p className="field-hint">
                    {shippingQuoteLoading
                      ? 'מחשבים מחיר משלוח אמיתי לפי הכתובת...'
                      : shippingQuoteError
                        ? `שגיאה בקבלת מחיר משלוח: ${shippingQuoteError}`
                        : shippingQuote
                          ? [
                              deliveryMethod === 'Delivery'
                                ? `משלוח: ₪${shippingQuote.regularPrice} (רגיל) · ₪${shippingQuote.expressPrice} (אקספרס)`
                                : null,
                              returnMethod === 'CourierPickup' ? `איסוף החזרה בשליח: ₪${shippingQuote.returnPrice}` : null,
                            ]
                              .filter(Boolean)
                              .join(' · ')
                          : null}
                  </p>
                )}
                {deliveryMethod === 'Delivery' && arrivalCheck?.message && (
                  <p
                    className={`field-hint ${arrivalBlocked ? 'field-hint-error' : 'field-hint-ok'}`}
                    role={arrivalBlocked ? 'alert' : 'status'}
                  >
                    {arrivalBlocked ? '' : '✓ '}
                    {arrivalCheck.message}
                  </p>
                )}
                {returnMethod === 'CourierPickup' && returnArrival?.message && (
                  <p
                    className={`field-hint ${returnArrival.possible === false ? 'field-hint-error' : 'field-hint-ok'}`}
                    role={returnArrival.possible === false ? 'alert' : 'status'}
                  >
                    {returnArrival.possible === false ? '' : '✓ '}
                    איסוף ההחזרה: {returnArrival.message}
                  </p>
                )}
                {returnArrival?.possible === false && returnArrival.suggestedTime && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    onClick={() => handleReturnTimeChange(returnArrival.suggestedTime)}
                  >
                    שינוי שעת ההחזרה ל-{returnArrival.suggestedTime}
                  </button>
                )}
                {arrivalCheck?.possible === false && arrivalCheck.suggestedTime && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    onClick={() => applyStartTime(arrivalCheck.suggestedTime)}
                  >
                    שינוי שעת ההתחלה ל-{arrivalCheck.suggestedTime}
                  </button>
                )}
              </fieldset>
            )}

            {offersOtherReturnAddress && (
              <>
                <label className="checkbox-row">
                  <input
                    type="checkbox"
                    checked={returnOtherAddress}
                    onChange={(e) => setReturnOtherAddress(e.target.checked)}
                    disabled={callStatus === 'Ringing'}
                  />
                  לאסוף את הפריט בהחזרה ממקום אחר (לא מכתובת המשלוח)
                </label>
                {returnOtherAddress && (
                  <fieldset className="delivery-address-fields">
                    <legend>כתובת לאיסוף השליח בהחזרה</legend>
                    <div className="rent-dates-row">
                      <CityStreetFields
                        idPrefix="returnPickup"
                        city={returnCity}
                        street={returnStreet}
                        onCityChange={setReturnCity}
                        onStreetChange={setReturnStreet}
                        onValidChange={setReturnAddressValid}
                        disabled={callStatus === 'Ringing'}
                      />
                    </div>
                    <div className="rent-dates-row">
                      <div>
                        <label htmlFor="returnHouseNumber">מספר בית</label>
                        <input
                          id="returnHouseNumber"
                          type="text"
                          value={returnHouseNumber}
                          onChange={(e) => setReturnHouseNumber(e.target.value)}
                          disabled={callStatus === 'Ringing'}
                          required
                        />
                      </div>
                      <div>
                        <label htmlFor="returnFloor">קומה</label>
                        <input
                          id="returnFloor"
                          type="text"
                          value={returnFloor}
                          onChange={(e) => setReturnFloor(e.target.value)}
                          disabled={callStatus === 'Ringing'}
                          required
                        />
                      </div>
                    </div>
                    <div className="rent-dates-row">
                      <div>
                        <label htmlFor="returnApartment">מספר דירה / שם משפחה</label>
                        <input
                          id="returnApartment"
                          type="text"
                          value={returnApartment}
                          onChange={(e) => setReturnApartment(e.target.value)}
                          disabled={callStatus === 'Ringing'}
                          required
                        />
                      </div>
                    </div>
                  </fieldset>
                )}
              </>
            )}

            <p className="rent-total-price">
              סה"כ לתשלום: <span key={totalPrice} className="rent-total-amount">₪{totalPrice.toFixed(2)}</span>
              {orderItems.some((o) => o.addVat) && <span className="item-price-hourly-note"> (כולל מע״מ)</span>}
              {rangeValid && (
                <span className="item-price-hourly-note">
                  {' '}
                  ({isLocation ? hoursText(locationHours) : daysText})
                </span>
              )}
              {(isBatchOrder || totalUnits > 1) && <span className="item-price-hourly-note"> ({countText})</span>}
              {deliveryFee > 0 && <span className="item-price-hourly-note"> (כולל ₪{deliveryFee} משלוח)</span>}
              {returnFee > 0 && <span className="item-price-hourly-note"> (כולל ₪{returnFee} איסוף החזרה)</span>}
            </p>

                </fieldset>
                {detailsDone ? (
                  <button type="button" className="btn btn-ghost btn-small" onClick={() => setDetailsDone(false)}>
                    עריכת קבלה והחזרה
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => setDetailsDone(true)}
                    disabled={
                      startInPast ||
                      !deliveryQuoteReady ||
                      (needsDeliveryAddress && !isDeliveryAddressComplete) ||
                      !isReturnAddressComplete
                    }
                  >
                    {needsDeliveryAddress && shippingQuoteLoading ? 'מחשבים מחיר משלוח...' : 'המשך לתשלום'}
                  </button>
                )}
              </section>
            )}

            {isAvailable && detailsDone && (
              <section className="rent-step rent-form" aria-label="תשלום">
                <h3 className="rent-step-title">3. סיכום ותשלום</h3>
                <dl className="item-meta order-summary">
                  <div>
                    <dt>פריטים</dt>
                    <dd>{orderItems.map(nameWithUnits).join(', ')}</dd>
                  </div>
                  <div>
                    <dt>{isLocation ? 'מועד' : 'תחילת ההשכרה'}</dt>
                    <dd>
                      {formatDateHe(startDate)} · {startTime}
                      {isLocation && `-${returnTime}`}
                    </dd>
                  </div>
                  <div hidden={isLocation}>
                    <dt>החזרה</dt>
                    <dd>
                      {formatDateHe(effectiveReturnDate)} · {returnTime}
                    </dd>
                  </div>
                  <div hidden={isLocation}>
                    <dt>קבלת הפריט</dt>
                    <dd>
                      {deliveryMethod === 'Delivery'
                        ? `${isExpressSelected ? 'משלוח אקספרס' : 'משלוח'} אל ${deliveryAddressText}`
                        : 'איסוף עצמי'}
                    </dd>
                  </div>
                  <div hidden={isLocation}>
                    <dt>החזרת הפריט</dt>
                    <dd>{returnMethod === 'CourierPickup' ? `איסוף שליח מ${returnAddressText}` : 'מסירה עצמית למשכיר'}</dd>
                  </div>
                </dl>

                <div className="late-policy-card">
                  <span className="late-policy-icon" aria-hidden="true">⏰</span>
                  <div>
                    <strong>תזכורת</strong>
                    <p>{isLocation ? 'נא לסיים ולפנות את המקום בזמן.' : 'איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור.'}</p>
                  </div>
                </div>
            <p className="rent-total-price">
              סה"כ לתשלום: <span key={totalPrice} className="rent-total-amount">₪{totalPrice.toFixed(2)}</span>
              {orderItems.some((o) => o.addVat) && <span className="item-price-hourly-note"> (כולל מע״מ)</span>}
              {rangeValid && (
                <span className="item-price-hourly-note">
                  {' '}
                  ({isLocation ? hoursText(locationHours) : daysText})
                </span>
              )}
              {(isBatchOrder || totalUnits > 1) && <span className="item-price-hourly-note"> ({countText})</span>}
              {deliveryFee > 0 && <span className="item-price-hourly-note"> (כולל ₪{deliveryFee} משלוח)</span>}
              {returnFee > 0 && <span className="item-price-hourly-note"> (כולל ₪{returnFee} איסוף החזרה)</span>}
            </p>

<fieldset className="delivery-choice-block">
<legend>אמצעי תשלום</legend>
<div className="delivery-choice payment-choice">
                  {deliveryMethod === 'SelfPickup' && (
                    <label>
                      <input
                        type="radio"
                        name="paymentMethod"
                        value="Cash"
                        checked={paymentMethod === 'Cash'}
                        onChange={() => setPaymentMethod('Cash')}
                      />
                      מזומן
                    </label>
                  )}
                  <label>
                    <input
                      type="radio"
                      name="paymentMethod"
                      value="BankTransfer"
                      checked={paymentMethod === 'BankTransfer'}
                      onChange={() => setPaymentMethod('BankTransfer')}
                    />
                    העברה בנקאית
                  </label>
                  {orderPay?.enabled && (
                    <label>
                      <input
                        type="radio"
                        name="paymentMethod"
                        value="CreditCard"
                        checked={paymentMethod === 'CreditCard'}
                        onChange={() => setPaymentMethod('CreditCard')}
                      />
                      כרטיס אשראי
                    </label>
                  )}
                </div>
</fieldset>

                {paymentMethod === 'BankTransfer' && (
                  <div className="bank-transfer-panel payment-space">
                    <h3 className="payment-space-title">תשלום בהעברה בנקאית</h3>
                    {payoutLoading && <Loader label="טוען פרטי חשבון" compact />}
                    {payoutError && <p className="form-error">{payoutError}</p>}
                    {!payoutLoading && ownerPayout && (
                      ownerPayout.isConfigured ? (
                        <>
                          <p className="payment-space-amount">
                            לתשלום: <strong>₪{itemsSubtotal.toFixed(2)}</strong>
                          </p>
                          <dl className="item-meta payment-space-details">
                            <div>
                              <dt>בעל/ת החשבון</dt>
                              <dd>{ownerPayout.accountHolderName}</dd>
                            </div>
                            <div>
                              <dt>בנק</dt>
                              <dd>
                                {ownerPayout.bankName} · סניף {ownerPayout.branchNumber}
                              </dd>
                            </div>
                            <div>
                              <dt>מספר חשבון</dt>
                              <dd>{ownerPayout.accountNumber}</dd>
                            </div>
                          </dl>

                          <div className="payment-proof">
                            <label htmlFor="paymentProof">אחרי ההעברה - העלי אסמכתא (צילום מסך, PDF או קובץ אקסל)</label>
                            <input
                              id="paymentProof"
                              type="file"
                              accept="image/*,application/pdf,.xlsx,.xls,.csv"
                              onChange={handleProofChange}
                              disabled={proofBusy}
                            />
                            {proofBusy && <UploadProgress label="מעלה את האסמכתא..." />}
                            {proofError && <p className="form-error">{proofError}</p>}
                            {proof && (
                              <p className="payment-proof-ok">
                                ✓ האסמכתא הועלתה: <bdi>{proof.fileName}</bdi>
                              </p>
                            )}
                            {!proof && !proofBusy && (
                              <p className="field-hint">
                                אפשר לאשר את ההשכרה רק אחרי העלאת האסמכתא. היא תישלח למשכירה במייל.
                              </p>
                            )}
                          </div>
                        </>
                      ) : (
                        <p className="state-message state-error">
                          הצלמת בקהילה טרם הגדירה פרטי חשבון לקבלת תשלומים - יש לבחור אמצעי תשלום אחר.
                        </p>
                      )
                    )}
                  </div>
                )}

                {shipPay?.enabled && shippingFee > 0 && (
                  <div className="shipping-card-box">
                    {paymentMethod === 'CreditCard' && shipPay.hasCard ? (
                      <p>דמי המשלוח (₪{shippingFee}) כלולים בתשלום באשראי.</p>
                    ) : shipPay.hasCard ? (
                      <p>
                        דמי המשלוח (₪{shippingFee}) ייגבו מכרטיס האשראי השמור שלך. אין צורך להזין אותו שוב.
                      </p>
                    ) : (
                      <>
                        <p>
                          דמי המשלוח (₪{shippingFee}) משולמים בכרטיס אשראי. בפעם הראשונה תועברי לדף תשלום מאובטח, ובפעמים
                          הבאות הכרטיס יחויב אוטומטית, בהתאם ל
                          <Link to="/terms" target="_blank" rel="noopener noreferrer" className="call-party-link">
                            תנאי השימוש
                          </Link>
                          . פרטי הכרטיס אינם נשמרים באתר אלא מאובטחים אצל חברת הסליקה סאמיט.
                        </p>
                        <label className="checkbox-row">
                          {paymentMethod === 'CreditCard' ? (
                            <input type="checkbox" checked={saveCardLater} onChange={(e) => setSaveCardLater(e.target.checked)} />
                          ) : (
                            <input type="checkbox" checked={cardConsent} onChange={(e) => setCardConsent(e.target.checked)} />
                          )}
                          <span>אני מאשרת לשמור את הכרטיס לחיוב דמי משלוח בהזמנות הבאות</span>
                        </label>
                      </>
                    )}
                  </div>
                )}

                {paymentMethod === 'CreditCard' && !(shipPay?.enabled && shippingFee > 0 && !shipPay.hasCard) &&
                  (shipPay?.hasCard ? (
                    <label className="checkbox-row">
                      <input type="checkbox" checked={saveCardLater} onChange={(e) => setSaveCardLater(e.target.checked)} />
                      <span>לשמור את הכרטיס להזמנות הבאות (גם לדמי משלוח, בלי להזין שוב)</span>
                    </label>
                  ) : (
                    <div className="shipping-card-box">
                      <p>
                        התשלום (₪{totalPrice.toFixed(2)}) מתבצע בכרטיס אשראי. בפעם הראשונה תועברי לדף תשלום מאובטח, ובפעמים
                        הבאות הכרטיס יחויב אוטומטית, בהתאם ל
                        <Link to="/terms" target="_blank" rel="noopener noreferrer" className="call-party-link">
                          תנאי השימוש
                        </Link>
                        . פרטי הכרטיס אינם נשמרים באתר אלא מאובטחים אצל חברת הסליקה סאמיט.
                      </p>
                      <label className="checkbox-row">
                        <input type="checkbox" checked={saveCardLater} onChange={(e) => setSaveCardLater(e.target.checked)} />
                        <span>אני מאשרת לשמור את הכרטיס לחיוב בהזמנות הבאות</span>
                      </label>
                    </div>
                  ))}

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

                {phoneEditOpen && (
                  <PhoneUpdateModal onClose={() => setPhoneEditOpen(false)} onSaved={() => setPhoneConfirmed(true)} />
                )}

                <label className="checkbox-row">
                  <input type="checkbox" checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} />
                  <span>
                    קראתי ואני מאשרת את{' '}
                    <Link to="/terms" target="_blank" rel="noopener noreferrer" className="call-party-link">
                      תנאי השימוש
                    </Link>
                  </span>
                </label>

                {submitting && paymentMethod === 'BankTransfer' && (
                  <UploadProgress label="שולחת את האסמכתא ופותחת את ההזמנה..." />
                )}

                {submitError && <p className="form-error">{submitError}</p>}

                {paymentMethod === 'CreditCard' ? (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={handleCardPayment}
                    disabled={submitting || (needsDeliveryAddress && !isDeliveryAddressComplete) || !isReturnAddressComplete || startInPast || arrivalBlocked || !termsAccepted || !phoneConfirmed}
                  >
                    {submitting ? 'מעבירה לתשלום...' : 'מעבר לתשלום מאובטח באשראי'}
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={needsShippingCard ? handleShippingCardFirst : handleConfirm}
                    disabled={submitting || !canConfirmDirectly || !termsAccepted || !phoneConfirmed || (needsShippingCard && !cardConsent)}
                  >
                    {submitting
                      ? 'שולח...'
                      : needsShippingCard
                        ? 'מעבר לתשלום דמי משלוח ואישור ההשכרה'
                        : paymentMethod === 'BankTransfer'
                          ? 'שליחת האסמכתא ואישור השכרה'
                          : 'אישור השכרה'}
                  </button>
                )}
              </section>
            )}
          </>
        )}

      </div>
    </Modal>
  )
}
