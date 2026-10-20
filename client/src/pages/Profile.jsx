import { useCallback, useEffect, useMemo, useState } from 'react'
import { categoriesApi, commissionsApi, itemsApi, loansApi, usersApi } from '../api/endpoints.js'
import { Link, useSearchParams } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark.jsx'
import { ProofButton } from '../components/ProofButton.jsx'
import { CommissionCard } from '../components/CommissionCard.jsx'
import { ConfirmDialog } from '../components/ConfirmDialog.jsx'
import { AddItemModal } from '../components/AddItemModal.jsx'
import { EditItemModal } from '../components/EditItemModal.jsx'
import { CityStreetFields, joinStreetAndNumber, splitStreetAndNumber } from '../components/AddressFields.jsx'
import { Modal } from '../components/Modal.jsx'
import { ShipmentTracking } from '../components/ShipmentTracker.jsx'
import { CallPartyButton } from '../components/CallPartyButton.jsx'
import { PendingReviews } from '../components/PendingReviews.jsx'
import { SectionTitle } from '../components/SectionTitle.jsx'
import { IdeaBox } from '../components/IdeaBox.jsx'
import { isReturningVisitor } from '../utils/visits.js'
import { ItemScheduleModal } from '../components/ItemScheduleModal.jsx'
import { ProfileThanks } from '../components/ProfileThanks.jsx'
import { OwnerMonthMemo } from '../components/OwnerMonthMemo.jsx'
import { AnniversaryBanner } from '../components/AnniversaryBanner.jsx'
import { ProfileFavorites } from '../components/ProfileFavorites.jsx'
import { ProfileQuestions } from '../components/ProfileQuestions.jsx'
import { ProfileSales } from '../components/ProfileSales.jsx'
import { CONDITION_LABELS } from '../components/BuyModal.jsx'
import { emptyPayoutForm, PayoutFields } from '../components/PayoutFields.jsx'
import {
  ItemStatusBadge,
  LoanStatusBadge,
  deliveryMethodLabel,
  paymentMethodLabel,
  paymentStatusLabel,
  returnMethodLabel,
} from '../components/StatusBadge.jsx'
import { useAuth } from '../context/AuthContext.jsx'
import { isValidIsraeliPhone } from '../utils/phone.js'
import { QuietHoursField } from '../components/QuietHoursField.jsx'
import { quietFromUser, quietPayload, quietUserFields } from '../utils/quietHours.js'
import { showRegisteredNotice } from '../utils/registeredNotice.js'
import { Loader } from '../components/Loader.jsx'
import { formatDate, formatDateTime, formatMoney, israelNowWall, wallMs } from '../utils/format.js'

// איחור בהחזרה: כל יום (או חלק מיום) מעבר למועד ההחזרה נספר כיום השכרה נוסף. אחרי ההחזרה השרת קובע את
// מספר הימים והסכום (lateDays/lateFee); לפני - מחשבים את האיחור הנוכחי לפי השעה עכשיו.
// השכרה "חיה" - פעילה או באיחור (לא הוחזרה ולא בוטלה): רק עליה מציגים קישור למעקב
function isLiveLoan(loan) {
  return loan.status === 'Active' || loan.status === 'Overdue'
}

function canCancel(loan) {
  return loan.status === 'Active' && Boolean(loan.cancelDeadline) && wallMs(loan.cancelDeadline) >= wallMs(israelNowWall())
}

function canMarkReturned(loan) {
  if (loan.status !== 'Active') return false
  if (loan.pickedUpAt || !loan.startDate) return true
  const start = `${String(loan.startDate).slice(0, 10)}T${loan.startTime || '00:00'}:00`
  return wallMs(start) <= wallMs(israelNowWall())
}

function ownerPaymentText(loan) {
  if (loan.paymentMethod === 'BankTransfer' && loan.paymentStatus === 'Pending') return 'שולם, ממתין לאישורך'
  if (loan.paymentMethod === 'CreditCard' && loan.ownerPayoutNet != null) {
    const net = formatMoney(loan.ownerPayoutNet)
    if (loan.ownerPayoutPaid) return `שולם באשראי · הועבר אלייך ${net}`
    if (loan.ownerPayoutRelease) return `שולם באשראי · ${net} בדרך אלייך`
    return `שולם באשראי · ${net} יועברו אלייך מיד אחרי שתסמני שהפריט הוחזר`
  }
  return paymentStatusLabel(loan.paymentStatus)
}

function LateNote({ loan }) {
  if (loan.lateDays > 0) {
    return (
      <span className="late-note">
        איחור {loan.lateDays} {loan.lateDays === 1 ? 'יום' : 'ימים'} · +₪{loan.lateFee}
      </span>
    )
  }

  if (loan.status === 'Active' || loan.status === 'Overdue') {
    const lateMs = wallMs(israelNowWall()) - wallMs(loan.expectedReturnDate)
    if (lateMs > 0) {
      const chargedMs = lateMs - 90 * 60000
      const days = chargedMs > 0 ? Math.ceil(chargedMs / 86400000) : 0
      return (
        <span className="late-note is-live">
          {days > 0 ? `באיחור - ${days} ${days === 1 ? 'יום השכרה נוסף' : 'ימי השכרה נוספים'}` : 'באיחור'}
        </span>
      )
    }
  }

  return null
}

const ROLE_LABELS = { Renter: 'שוכר', Owner: 'צלמת בקהילה', Admin: 'מנהל מערכת' }

function initials(name) {
  return (name ?? '')
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0])
    .join('')
    .toUpperCase()
}

function timeGreeting() {
  const hour = new Date().getHours()
  if (hour >= 5 && hour < 12) return 'בוקר טוב'
  if (hour >= 12 && hour < 17) return 'צהריים טובים'
  if (hour >= 17 && hour < 22) return 'ערב טוב'
  return 'לילה טוב'
}

const HISTORY_PREVIEW = 10

export function Profile() {
  const { user, updateUser } = useAuth()
  const isOwner = user.role === 'Owner' || user.role === 'Admin'

  const [myLoans, setMyLoans] = useState([])
  const [showAllMyLoans, setShowAllMyLoans] = useState(false)
  const [showAllIncoming, setShowAllIncoming] = useState(false)
  const [myItems, setMyItems] = useState([])
  const [incomingLoans, setIncomingLoans] = useState([])
  const [categories, setCategories] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [editProfileOpen, setEditProfileOpen] = useState(false)
  const [editAddressOpen, setEditAddressOpen] = useState(false)
  const [editPayoutOpen, setEditPayoutOpen] = useState(false)
  const [payoutInfo, setPayoutInfo] = useState(null)
  const [debt, setDebt] = useState(null)
  const [deleteTarget, setDeleteTarget] = useState(null)
  const [cancelTarget, setCancelTarget] = useState(null)
  const [returnTarget, setReturnTarget] = useState(null)
  const [cancelling, setCancelling] = useState(false)
  const [cancelNotice, setCancelNotice] = useState('')
  const [savedToast, setSavedToast] = useState(0)

  useEffect(() => {
    if (!savedToast) return undefined
    const timer = window.setTimeout(() => setSavedToast(0), 4000)
    return () => window.clearTimeout(timer)
  }, [savedToast])
  const [editTarget, setEditTarget] = useState(null)
  const [scheduleTarget, setScheduleTarget] = useState(null)

  // קישור מחלון הפריט ("לעריכת הפריט"): /profile?edit=<itemId>
  const [searchParams, setSearchParams] = useSearchParams()
  const editParam = searchParams.get('edit')
  useEffect(() => {
    if (!editParam) return
    setSearchParams({}, { replace: true })
    itemsApi
      .getById(editParam)
      .then((item) => {
        if (item?.ownerId === user?.id) setEditTarget(item)
      })
      .catch((err) => setError(err.message || 'לא הצלחנו לטעון את הפריט לעריכה'))
  }, [editParam, setSearchParams, user?.id])

  // ברשימה מגיעה רק התמונה הראשית, ולכן לפני העריכה שולפים את הפריט המלא (אחרת השמירה הייתה מוחקת תמונות נוספות)
  async function openEditor(item) {
    if ((item.imageCount ?? 0) <= 1 || item.imageDataUrls?.length) {
      setEditTarget(item)
      return
    }
    try {
      setEditTarget(await itemsApi.getById(item.id))
    } catch (err) {
      setError(err.message || 'לא הצלחנו לטעון את הפריט לעריכה')
    }
  }
  const [addItemOpen, setAddItemOpen] = useState(false)
  const rentItems = myItems.filter((i) => !i.forSale)
  const saleItems = myItems.filter((i) => i.forSale)
  const [deletingItem, setDeletingItem] = useState(false)

  const categoryNameById = useMemo(
    () =>
      new Map(
        categories.map((c) => {
          const parent = c.parentId ? categories.find((p) => p.id === c.parentId) : null
          return [c.id, parent ? `${parent.name} › ${c.name}` : c.name]
        }),
      ),
    [categories],
  )

  // סדר הקטגוריות בחלון שיתוף הציוד - כמו בקטלוג (לפי הסדר שקבעה המנהלת, ובשוויון לפי א"ב)
  const sortedCategories = useMemo(
    () =>
      [...categories].sort(
        (a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name, 'he'),
      ),
    [categories],
  )

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const [loansResult, categoriesResult] = await Promise.all([
        loansApi.getByUser(user.id, showAllMyLoans ? undefined : HISTORY_PREVIEW + 1),
        categoriesApi.getAll(),
      ])
      setMyLoans(loansResult ?? [])
      setCategories(categoriesResult ?? [])

      if (isOwner) {
        commissionsApi.debt().then(setDebt).catch(() => setDebt(null))
        const [itemsResult, incoming, payoutResult] = await Promise.all([
          itemsApi.getByOwner(user.id),
          // נקודת הקצה החדשה מחזירה רק את ההשכרות על הציוד שלי; שרת ישן - נופלים חזרה לרשימה המלאה
          loansApi.getIncoming(user.id, showAllIncoming ? undefined : HISTORY_PREVIEW + 1).catch(async () => {
            const all = await loansApi.getAll()
            return { fallback: all }
          }),
          usersApi.getPayout(user.id).catch(() => null),
        ])
        setMyItems(itemsResult ?? [])
        const myItemIds = new Set((itemsResult ?? []).map((i) => i.id))
        setIncomingLoans(
          Array.isArray(incoming) ? incoming : (incoming?.fallback ?? []).filter((loan) => myItemIds.has(loan.itemId)),
        )
        setPayoutInfo(payoutResult)
      }
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }, [user.id, isOwner, showAllMyLoans, showAllIncoming])

  useEffect(() => {
    load()
  }, [load])

  async function handleReturn(loanId) {
    try {
      await loansApi.return(loanId)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setReturnTarget(null)
    }
  }

  async function handlePickedUp(loanId) {
    try {
      await loansApi.markPickedUp(loanId)
      await load()
    } catch (err) {
      setError(err.message)
    }
  }

  async function handleConfirmCancel() {
    if (!cancelTarget) return
    setCancelling(true)
    try {
      const result = await loansApi.cancel(cancelTarget.loan.id)
      setCancelNotice(`ההשכרה בוטלה. ${result.refundNote}`)
      setCancelTarget(null)
      await load()
    } catch (err) {
      setError(err.message)
      setCancelTarget(null)
    } finally {
      setCancelling(false)
    }
  }

  async function handleConfirmPayment(loanId) {
    try {
      await loansApi.confirmPayment(loanId)
      await load()
    } catch (err) {
      setError(err.message)
    }
  }

  async function handleConfirmDelete() {
    if (!deleteTarget) return
    setDeletingItem(true)
    try {
      await itemsApi.remove(deleteTarget.id)
      setDeleteTarget(null)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setDeletingItem(false)
    }
  }

  return (
    <div className="page profile-page">
      <section className="profile-hero">
        <BrandMark className="profile-avatar">
          <span className="profile-avatar-initials">{initials(user.name) || '👤'}</span>
        </BrandMark>
        <div className="profile-hero-info">
          <p className="profile-hero-greeting">
            {timeGreeting()}, {(user.name ?? '').trim().split(/\s+/)[0]}
          </p>
          <h1>{user.name}</h1>
          <p className="profile-hero-role">{ROLE_LABELS[user.role] ?? user.role}</p>
        </div>
        <button type="button" className="btn btn-ghost profile-edit-btn" onClick={() => setEditProfileOpen(true)}>
          עריכת פרטים
        </button>
      </section>

      <section className="profile-card profile-details-card">
        <dl className="profile-details-grid">
          <div className="profile-email">
            <dt>אימייל</dt>
            <dd dir="ltr">
              {user.email?.includes('@') ? (
                <>
                  {user.email.split('@')[0]}
                  <wbr />@{user.email.split('@').slice(1).join('@')}
                </>
              ) : (
                user.email
              )}
            </dd>
          </div>
          <div>
            <dt>טלפון</dt>
            <dd>{user.phone || '—'}</dd>
          </div>
          <div>
            <dt>שיחות מהאתר</dt>
            <dd>
              {user.quietHoursEnabled
                ? `לא בין ${user.quietFrom || '00:30'} ל-${user.quietTo || '07:00'}`
                : 'זמינה תמיד'}
            </dd>
          </div>
          <div>
            <dt>כתובת מגורים</dt>
            <dd>{user.address && user.city ? `${user.address}, ${user.city}` : 'לא הוגדרה'}</dd>
          </div>
          {user.addressNotes && (
            <div>
              <dt>הערות כתובת</dt>
              <dd>{user.addressNotes}</dd>
            </div>
          )}
        </dl>
        <button type="button" className="btn profile-card-action" onClick={() => setEditAddressOpen(true)}>
          {user.address ? 'עריכת כתובת' : 'הגדרת כתובת'}
        </button>
      </section>

      {isOwner && (
        <section className="profile-card profile-details-card">
          <SectionTitle storageKey="payout" className="profile-card-title" defaultCollapsed>פרטי חשבון לקבלת תשלומים</SectionTitle>
          {payoutInfo?.isConfigured ? (
            <dl className="profile-details-grid">
              <div>
                <dt>בעל/ת החשבון</dt>
                <dd>{payoutInfo.accountHolderName}</dd>
              </div>
              <div>
                <dt>בנק</dt>
                <dd>
                  {payoutInfo.bankName} · סניף {payoutInfo.branchNumber}
                </dd>
              </div>
              <div>
                <dt>מספר חשבון</dt>
                <dd>{payoutInfo.accountNumber}</dd>
              </div>
            </dl>
          ) : (
            <p className="state-message">
              עדיין לא הוגדרו פרטי חשבון לקבלת תשלומים - נדרש כדי לאפשר תשלום בהעברה בנקאית על הציוד שלכם.
            </p>
          )}
          <button
            type="button"
            className="btn profile-card-action"
            onClick={() => setEditPayoutOpen(true)}
          >
            {payoutInfo?.isConfigured ? 'עריכת פרטי תשלום' : 'הגדרת פרטי תשלום'}
          </button>
        </section>
      )}

      {isOwner && <CommissionCard debt={debt} />}

      {user.role === 'Admin' && (
        <Link to="/admin" className="btn profile-card-action admin-link">
          ניהול האתר
        </Link>
      )}

      {error && <p className="state-message state-error">{error}</p>}
      {loading && <Loader label="טוען נתונים" />}

      {!loading && (
        <>
          <AnniversaryBanner userId={user.id} />

          {cancelNotice && (
            <p className="form-success" role="status">
              {cancelNotice}
            </p>
          )}

          <PendingReviews />

          <section className="profile-section">
            <SectionTitle storageKey="myLoans">ההשכרות שלי</SectionTitle>
            {myLoans.length === 0 ? (
              <p className="state-message">
                עוד לא שכרת ציוד. הקטלוג מחכה לך
              </p>
            ) : (
              <>
                <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>פריט</th>
                      <th>תאריך השכרה</th>
                      <th>החזרה משוערת</th>
                      <th>אופן קבלה</th>
                      <th>אופן החזרה</th>
                      <th>תשלום</th>
                      <th>סטטוס</th>
                      <th><span className="visually-hidden">פעולות</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    {myLoans.slice(0, showAllMyLoans ? undefined : HISTORY_PREVIEW).map((loan) => (
                      <tr key={loan.id}>
                        <td>
                          {loan.itemName}
                          {isLiveLoan(loan) && (
                            <>
                              <br />
                              <Link to={`/order/${loan.id}`} className="order-link-btn">
                                פרטי ההזמנה ומעקב
                              </Link>
                            </>
                          )}
                        </td>
                        <td>
{formatDate(loan.startDate ?? loan.loanDate)}
{loan.startTime ? ` · ${loan.startTime}` : ''}
</td>
                        <td>
                          {formatDateTime(loan.expectedReturnDate)}
                          <LateNote loan={loan} />
                        </td>
                        <td>
                          {deliveryMethodLabel(loan.deliveryMethod)}
                          <ShipmentTracking loan={loan} direction="out" />
                        </td>
                        <td>
                          {returnMethodLabel(loan.returnMethod)}
                          <ShipmentTracking loan={loan} direction="return" />
                          {loan.status === 'Active' && (
                            <>
                              {' '}
                              <Link to={`/order/${loan.id}/return`} className="payment-status-note">
                                {loan.returnMethod === 'CourierPickup' ? 'שינוי לאיסוף עצמי' : 'שינוי לאיסוף במשלוח'}
                              </Link>
                            </>
                          )}
                        </td>
                        <td>
                          {paymentMethodLabel(loan.paymentMethod)}
                          {/* בהעברה בנקאית השוכרת רואה "שולם" - אישור המשכירה הוא סימון פנימי בלבד */}
                          <span className="payment-status-note">
                            {' '}
                            · {loan.paymentMethod === 'BankTransfer' ? 'שולם' : paymentStatusLabel(loan.paymentStatus)}
                          </span>
                        </td>
                        <td>
                          <LoanStatusBadge status={loan.status} />
                        </td>
                        <td>
                          {canCancel(loan) && (
                            <button
                              type="button"
                              className="btn btn-ghost btn-small"
                              onClick={() => setCancelTarget({ loan, role: 'renter' })}
                            >
                              ביטול השכרה
                            </button>
                          )}
                          {loan.status === 'Active' && !loan.pickedUpAt && (
                            <button type="button" className="btn btn-primary btn-small" onClick={() => handlePickedUp(loan.id)}>
                              {loan.deliveryMethod === 'Delivery' ? 'הפריט הגיע אליי' : 'אספתי את הפריט'}
                            </button>
                          )}
                          <CallPartyButton loan={loan} isRenter />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
                {!showAllMyLoans && myLoans.length > HISTORY_PREVIEW && (
                  <button type="button" className="btn btn-ghost btn-small history-more" onClick={() => setShowAllMyLoans(true)}>
                    להצגת ההיסטוריה המלאה
                  </button>
                )}
                </>
            )}
          </section>

          <ProfileFavorites userId={user.id} />

          <ProfileQuestions />

          {isOwner && <OwnerMonthMemo ownerId={user.id} />}
          <ProfileThanks />

          {isReturningVisitor() && <IdeaBox />}

          {(
            <section className="profile-section">
              <div className="profile-section-head">
                <SectionTitle storageKey="mySaleItems">לוח יד 2</SectionTitle>
                <button type="button" className="btn btn-primary btn-small" onClick={() => setAddItemOpen('sale')}>
                  + פרסום פריט למכירה
                </button>
              </div>
              <h3 className="profile-subtitle">הפריטים שלי למכירה</h3>
              {saleItems.length === 0 ? (
                <p className="state-message">
                  עוד לא פרסמת פריטים למכירה. <Link to="/yad2">ללוח יד 2</Link>
                </p>
              ) : (
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>שם</th>
                        <th>קטגוריה</th>
                        <th>מחיר</th>
                        <th>מצב</th>
                        <th>סטטוס</th>
                        <th><span className="visually-hidden">פעולות</span></th>
                      </tr>
                    </thead>
                    <tbody>
                      {saleItems.map((item) => (
                        <tr key={item.id}>
                          <td>{item.name}</td>
                          <td>{categoryNameById.get(item.categoryId) ?? '—'}</td>
                          <td>₪{item.salePrice}</td>
                          <td>{CONDITION_LABELS[item.saleCondition] ?? 'משומש'}</td>
                          <td>
                            {item.sold ? (
                              <span className="badge badge-item-Rented">נמכר ✓</span>
                            ) : (
                              <span className="badge badge-sale">למכירה</span>
                            )}
                          </td>
                          <td>
                            {!item.sold && (
                              <div className="table-actions">
                                <button type="button" className="btn btn-ghost btn-small" onClick={() => openEditor(item)}>
                                  עריכה
                                </button>
                                <button type="button" className="btn btn-ghost btn-small" onClick={() => setDeleteTarget(item)}>
                                  מחיקה
                                </button>
                              </div>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <ProfileSales embedded />
            </section>
          )}

          {isOwner && (
            <>
              <section className="profile-section">
                <div className="profile-section-head">
                  <SectionTitle storageKey="myItems">הציוד שלי</SectionTitle>
                  <button type="button" className="btn btn-primary btn-small" onClick={() => setAddItemOpen(true)}>
                    + העלאת ציוד להשכרה
                  </button>
                </div>
                {rentItems.length === 0 ? (
                  <p className="state-message">
                    עוד לא שיתפת ציוד עם הקהילה. לחצי על "שיתוף ציוד חדש" כדי להוסיף את הפריט הראשון.
                  </p>
                ) : (
                  <div className="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          <th>שם</th>
                          <th>קטגוריה</th>
                          <th>מחיר יומי</th>
                          <th>סטטוס</th>
                          <th><span className="visually-hidden">פעולות</span></th>
                        </tr>
                      </thead>
                      <tbody>
                        {rentItems.map((item) => (
                          <tr key={item.id}>
                            <td>{item.name}</td>
                            <td>{categoryNameById.get(item.categoryId) ?? '—'}</td>
                            <td>₪{item.dailyPrice}{item.addVat ? ' (כולל מע״מ)' : ''}</td>
                            <td>
                              <ItemStatusBadge status={item.status} />
                            </td>
                            <td>
                              <div className="table-actions">
                                <button
                                  type="button"
                                  className="btn btn-ghost btn-small"
                                  onClick={() => openEditor(item)}
                                >
                                  עריכת פריט
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-ghost btn-small"
                                  onClick={() => setScheduleTarget(item)}
                                >
                                  לוח זמנים
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-ghost btn-small"
                                  onClick={() => setDeleteTarget(item)}
                                >
                                  מחיקה
                                </button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>

              <section className="profile-section">
                <SectionTitle storageKey="ownerLoans">השכרות על הציוד שלי</SectionTitle>
                {incomingLoans.length === 0 ? (
                  <p className="state-message">עוד לא השכירו את הציוד שלך. זה יגיע</p>
                ) : (
                  <>
                <div className="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          <th>פריט</th>
                          <th>תאריך השכרה</th>
                          <th>החזרה משוערת</th>
                          <th>אופן קבלה</th>
                          <th>אופן החזרה</th>
                          <th>תשלום</th>
                          <th>סטטוס</th>
                          <th><span className="visually-hidden">פעולות</span></th>
                        </tr>
                      </thead>
                      <tbody>
                        {incomingLoans.slice(0, showAllIncoming ? undefined : HISTORY_PREVIEW).map((loan) => (
                          <tr key={loan.id}>
                            <td>
                              {loan.itemName}
                              {isLiveLoan(loan) && (
                                <>
                                  <br />
                                  <Link to={`/order/${loan.id}`} className="order-link-btn">
                                    פרטי ההזמנה ומעקב
                                  </Link>
                                </>
                              )}
                            </td>
                            <td>
{formatDate(loan.startDate ?? loan.loanDate)}
{loan.startTime ? ` · ${loan.startTime}` : ''}
</td>
                            <td>
                          {formatDateTime(loan.expectedReturnDate)}
                          <LateNote loan={loan} />
                        </td>
                            <td>
                              {deliveryMethodLabel(loan.deliveryMethod)}
                              <ShipmentTracking loan={loan} direction="out" />
                            </td>
                            <td>
                              {returnMethodLabel(loan.returnMethod)}
                              <ShipmentTracking loan={loan} direction="return" />
                            </td>
                            <td>
                              {paymentMethodLabel(loan.paymentMethod)}
                              <span className="payment-status-note">
                                {' '}
                                ·{' '}
                                {ownerPaymentText(loan)}
                              </span>
                            </td>
                            <td>
                              <LoanStatusBadge status={loan.status} />
                            </td>
                            <td>
                              <div className="table-actions">
                                {canCancel(loan) && (
                                  <button
                                    type="button"
                                    className="btn btn-ghost btn-small"
                                    onClick={() => setCancelTarget({ loan, role: 'owner' })}
                                  >
                                    ביטול השכרה
                                  </button>
                                )}
                                <CallPartyButton loan={loan} isRenter={false} />
                                {loan.hasPaymentProof && <ProofButton loanId={loan.id} />}
                                {loan.ownerPayoutHasProof && (
                                  <ProofButton loanId={loan.id} fetchProof={loansApi.getPayoutProof} label="אסמכתת העברה" />
                                )}
                                {loan.paymentStatus === 'Pending' && (
                                  <button
                                    type="button"
                                    className="btn btn-ghost btn-small"
                                    onClick={() => handleConfirmPayment(loan.id)}
                                  >
                                    אישור קבלת תשלום
                                  </button>
                                )}
                                {loan.status === 'Active' && !loan.pickedUpAt && (
                                  <button
                                    type="button"
                                    className="btn btn-primary btn-small"
                                    onClick={() => handlePickedUp(loan.id)}
                                  >
                                    הפריט נאסף
                                  </button>
                                )}
                                {canMarkReturned(loan) && (
                                  <button
                                    type="button"
                                    className="btn btn-ghost btn-small"
                                    onClick={() => setReturnTarget(loan)}
                                  >
                                    סימון כהוחזר
                                  </button>
                                )}
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                {!showAllIncoming && incomingLoans.length > HISTORY_PREVIEW && (
                  <button type="button" className="btn btn-ghost btn-small history-more" onClick={() => setShowAllIncoming(true)}>
                    להצגת ההיסטוריה המלאה
                  </button>
                )}
                </>
                )}
              </section>
            </>
          )}
        </>
      )}

      {editProfileOpen && (
        <EditProfileModal user={user} onClose={() => setEditProfileOpen(false)} onSaved={updateUser} />
      )}

      {editAddressOpen && (
        <EditAddressModal user={user} onClose={() => setEditAddressOpen(false)} onSaved={updateUser} />
      )}

      {editPayoutOpen && (
        <EditPayoutModal
          userId={user.id}
          initial={payoutInfo}
          onClose={() => setEditPayoutOpen(false)}
          onSaved={(payout) => {
            setPayoutInfo(payout)
            setEditPayoutOpen(false)
          }}
        />
      )}

      {addItemOpen && (
        <AddItemModal
          categories={sortedCategories}
          ownerId={user.id}
          initialForSale={addItemOpen === 'sale'}
          onClose={() => setAddItemOpen(false)}
          onCreated={(created) => setMyItems((prev) => [...prev, created])}
        />
      )}

      {editTarget && (
        <EditItemModal
          item={editTarget}
          categories={categories}
          ownerId={user.id}
          onClose={() => setEditTarget(null)}
          onSaved={(updated) => {
            setMyItems((prev) => prev.map((i) => (i.id === updated.id ? updated : i)))
            setSavedToast(Date.now())
          }}
        />
      )}

      {scheduleTarget && <ItemScheduleModal item={scheduleTarget} onClose={() => setScheduleTarget(null)} />}

      {savedToast > 0 && (
        <p key={savedToast} className="form-success thanks-toast saved-toast" role="status">
          ✓ השינויים נשמרו ופורסמו
        </p>
      )}

      {returnTarget && (
        <ConfirmDialog
          title="סימון כהוחזר"
          message={`לסמן ש"${returnTarget.itemName}" הוחזר אלייך? אחרי הסימון ההשכרה נסגרת.`}
          confirmLabel="כן, הוחזר"
          cancelLabel="עוד לא"
          onConfirm={() => handleReturn(returnTarget.id)}
          onCancel={() => setReturnTarget(null)}
        />
      )}

      {cancelTarget && (
        <ConfirmDialog
          title="ביטול השכרה"
          message={
            cancelTarget.role === 'owner'
              ? `לבטל את ההשכרה של "${cancelTarget.loan.itemName}"? השוכרת תקבל מייל והחזר מלא, והפריט יחזור להיות זמין.`
              : `לבטל את ההשכרה של "${cancelTarget.loan.itemName}"? אפשר לבטל עד 24 שעות לפני תחילת ההשכרה. בתשלום באשראי ההחזר מבוצע על ידי ההנהלה בימים הקרובים.`
          }
          confirmLabel="ביטול ההשכרה"
          cancelLabel="לא לבטל"
          busy={cancelling}
          onConfirm={handleConfirmCancel}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="מחיקת פריט"
          message={`למחוק את "${deleteTarget.name}" מקטלוג הציוד? הפריט יוסר מהקטלוג, והיסטוריית ההשכרות שלו תישמר.`}
          confirmLabel="מחיקה"
          busy={deletingItem}
          onConfirm={handleConfirmDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}

function EditProfileModal({ user, onClose, onSaved }) {
  const [form, setForm] = useState({ name: user.name, phone: user.phone ?? '' })
  const [quiet, setQuiet] = useState(() => quietFromUser(user))
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  function update(field) {
    return (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')

    if (!form.name.trim()) {
      setError('יש להזין שם מלא')
      return
    }
    if (!isValidIsraeliPhone(form.phone)) {
      setError('יש להזין מספר טלפון תקין')
      return
    }

    const phoneKey = (value) => (value ?? '').replace(/\D/g, '').slice(-9)
    const phoneChanged = phoneKey(form.phone) !== phoneKey(user.phone)

    setSubmitting(true)
    try {
      const updated = await usersApi.updateProfile(user.id, { ...form, ...quietPayload(quiet) })
      onSaved({ name: updated.name, phone: updated.phone, ...quietUserFields(updated) })
      onClose()
      if (phoneChanged) showRegisteredNotice('המספר עודכן בהצלחה! 📞')
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal title="עריכת פרטים אישיים" onClose={onClose}>
      <form className="modal-form" onSubmit={handleSubmit}>
        <label htmlFor="editName">שם מלא</label>
        <input id="editName" value={form.name} onChange={update('name')} required autoFocus />

        <label htmlFor="editPhone">טלפון</label>
        <input id="editPhone" type="tel" inputMode="tel" autoComplete="tel" dir="ltr" value={form.phone} onChange={update('phone')} required />
        <QuietHoursField value={quiet} onChange={setQuiet} idPrefix="editQuiet" />

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'שומר...' : 'שמירה'}
        </button>
      </form>
    </Modal>
  )
}

function EditAddressModal({ user, onClose, onSaved }) {
  const initial = splitStreetAndNumber(user.address)
  const [form, setForm] = useState({
    city: user.city ?? '',
    street: initial.street,
    number: initial.number,
    addressNotes: user.addressNotes ?? '',
    floor: user.addressFloor ?? '',
    apartment: user.addressApartment ?? '',
  })
  const [addressValid, setAddressValid] = useState(false)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  function update(field) {
    return (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')

    if (!form.city.trim() || !form.street.trim() || !form.number.trim()) {
      setError('יש להזין עיר, רחוב ומספר בית')
      return
    }
    if (!form.floor.trim() || !form.apartment.trim()) {
      setError('יש להזין קומה ומספר דירה או שם משפחה')
      return
    }
    if (!addressValid) {
      setError('יש לבחור עיר ורחוב מהרשימה שמופיעה בזמן ההקלדה')
      return
    }

    setSubmitting(true)
    try {
      const updated = await usersApi.updateAddress(user.id, {
        address: joinStreetAndNumber(form.street, form.number),
        street: form.street.trim(),
        city: form.city.trim(),
        addressNotes: form.addressNotes,
        addressFloor: form.floor.trim(),
        addressApartment: form.apartment.trim(),
      })
      onSaved({
        address: updated.address,
        city: updated.city,
        addressNotes: updated.addressNotes ?? '',
        addressFloor: updated.addressFloor ?? '',
        addressApartment: updated.addressApartment ?? '',
      })
      onClose()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal title="כתובת מגורים" onClose={onClose}>
      <form className="modal-form" onSubmit={handleSubmit}>
        <CityStreetFields
          idPrefix="edit"
          city={form.city}
          street={form.street}
          onCityChange={(city) => setForm((prev) => ({ ...prev, city }))}
          onStreetChange={(street) => setForm((prev) => ({ ...prev, street }))}
          onValidChange={setAddressValid}
        />

        <label htmlFor="editAddressNumber">מספר בית</label>
        <input id="editAddressNumber" value={form.number} onChange={update('number')} required inputMode="numeric" />

        <label htmlFor="editAddressFloor">קומה</label>
        <input id="editAddressFloor" value={form.floor} onChange={update('floor')} required />

        <label htmlFor="editAddressApartment">מספר דירה / שם משפחה</label>
        <input id="editAddressApartment" value={form.apartment} onChange={update('apartment')} required />
        <span className="field-hint">הפרטים האלה מועברים לשליח, ובאיסוף עצמי מוצגים גם לשוכרת, כדי שתמצא את הדלת.</span>

        <label htmlFor="editAddressNotes">הערות כתובת (אופציונלי)</label>
        <input
          id="editAddressNotes"
          value={form.addressNotes}
          onChange={update('addressNotes')}
          maxLength={200}
        />
        <span className="field-hint">ההערה תוצג לשוכרת באיסוף עצמי, ותועבר לשליח במשלוח.</span>

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'שומר...' : 'שמירה'}
        </button>
      </form>
    </Modal>
  )
}

function EditPayoutModal({ userId, initial, onClose, onSaved }) {
  const [form, setForm] = useState(
    initial?.isConfigured
      ? {
          accountHolderName: initial.accountHolderName,
          bankName: initial.bankName,
          branchNumber: initial.branchNumber,
          accountNumber: initial.accountNumber,
          consentAccepted: false,
        }
      : emptyPayoutForm,
  )
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')

    if (!form.accountHolderName.trim() || !form.bankName.trim() || !form.branchNumber.trim() || !form.accountNumber.trim()) {
      setError('יש למלא את כל פרטי החשבון')
      return
    }

    setSubmitting(true)
    try {
      const updated = await usersApi.updatePayout(userId, form)
      onSaved(updated)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal title="פרטי חשבון לקבלת תשלומים" onClose={onClose}>
      <form className="modal-form" onSubmit={handleSubmit}>
        <PayoutFields form={form} onChange={setForm} idPrefix="profilePayout" />

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'שומר...' : 'שמירה'}
        </button>
      </form>
    </Modal>
  )
}
