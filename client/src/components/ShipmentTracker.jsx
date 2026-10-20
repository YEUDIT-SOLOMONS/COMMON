import { useCallback, useEffect, useState } from 'react'
import { loansApi } from '../api/endpoints.js'
import { Modal } from './Modal.jsx'
import { Loader } from './Loader.jsx'
import { formatDate, todayIsraelIso } from '../utils/format.js'

// סדר השלבים להצגת התקדמות המשלוח
const STEPS = [
  { key: 'Pending', label: 'הוזמן' },
  { key: 'Assigned', label: 'שליח שובץ' },
  { key: 'OnTheWay', label: 'בדרך לאיסוף' },
  { key: 'PickedUp', label: 'בדרך אליך' },
  { key: 'Delivered', label: 'נמסר' },
]
// רענון: כל חצי דקה כשיש שליח בפעולה; בדקות ספורות כשעדיין מחכים למועד (חוסך קריאות לחברת השילוח)
const REFRESH_LIVE_MS = 30000
const REFRESH_IDLE_MS = 120000
const LIVE_STATUSES = ['Assigned', 'OnTheWay', 'PickedUp']

const DEMO_LABELS = {
  Pending: 'ההזמנה התקבלה. שליח ישובץ סמוך למועד',
  Assigned: 'שליח שובץ למשלוח',
  OnTheWay: 'השליח בדרך לאיסוף',
  PickedUp: 'החבילה אצל השליח ובדרך ליעד',
  Delivered: 'המשלוח נמסר',
  Canceled: 'המשלוח בוטל',
}

// נתוני הדגמה למסך "ההזמנה בוצעה" (/order/demo) - מאפשרים לראות את העיצוב בלי הזמנה אמיתית
export function demoShipping(stage = 'OnTheWay') {
  const status = DEMO_LABELS[stage] ? stage : 'OnTheWay'
  const active = status !== 'Pending' && status !== 'Canceled'
  const moving = status === 'OnTheWay' || status === 'PickedUp'
  const tomorrow = new Date(Date.now() + 86400000).toISOString()
  return {
    outbound: {
      status,
      statusLabel: DEMO_LABELS[status],
      etaDate: tomorrow,
      etaTimeRange: '08:00-10:00',
      courierName: active ? 'שליח לדוגמה' : null,
      courierPhone: active ? '0501234567' : null,
      latitude: moving ? 32.0853 : null,
      longitude: moving ? 34.8328 : null,
      locationUpdatedAt: moving ? new Date().toISOString() : null,
      legTarget: moving ? (status === 'PickedUp' ? 'Dropoff' : 'Pickup') : null,
      remainingKm: moving ? 4.2 : null,
      etaMinutes: moving ? 12 : null,
      progress: status === 'PickedUp' ? 0.55 : null,
      trackingUrl: null,
    },
    return: {
      status: 'Pending',
      statusLabel: `ההזמנה התקבלה ומתוזמנת ל-${new Date(Date.now() + 3 * 86400000).toLocaleDateString('he-IL', { day: '2-digit', month: '2-digit' })} · 12:00-15:00. שליח ישובץ סמוך למועד`,
      etaDate: tomorrow,
      etaTimeRange: '12:00-15:00',
    },
  }
}

function stepIndex(status) {
  const i = STEPS.findIndex((s) => s.key === status)
  return i < 0 ? 0 : i
}

function mapUrl(lat, lng) {
  return `https://www.google.com/maps?q=${lat},${lng}`
}

function timeHe(date) {
  return date.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Jerusalem' })
}

// סמן השליח: עיגול סגול עם חבילה (במקום משאית) שנע לאורך המסלול
function CourierMarker({ className = '' }) {
  return (
    <svg className={className} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <circle cx="16" cy="16" r="14.5" className="route-marker-disc" />
      <path className="route-marker-box" d="M16 7.5 23.5 11.8v8.4L16 24.5 8.5 20.2v-8.4L16 7.5Z" />
      <path className="route-marker-lines" d="M8.7 12 16 16.2 23.3 12M16 16.2v8" />
    </svg>
  )
}

// השוואת שעות "HH:mm" (מחרוזות עם אפס מוביל משתוות לקסיקוגרפית)
function endOfRange(range) {
  const m = /-\s*(\d{1,2}):(\d{2})\s*$/.exec(range || '')
  return m ? `${m[1].padStart(2, '0')}:${m[2]}` : null
}

// האם ההגעה צפויה אחרי השעה שהשוכרת ביקשה: לפי חלון חברת השילוח ולפי ההערכה החיה
function lateAgainst(deadline, shipment, arrival) {
  if (!deadline?.time) return null
  const etaDay = shipment.etaDate ? String(shipment.etaDate).slice(0, 10) : null
  if (etaDay && deadline.date && etaDay > deadline.date) return true
  const end = endOfRange(shipment.etaTimeRange)
  if (end && end > deadline.time) return true
  // ההערכה החיה (שעה היום) רלוונטית רק אם המשלוח אמור להגיע היום
  const liveToday = arrival && deadline.date === todayIsraelIso()
  if (liveToday && timeHe(arrival) > deadline.time) return true
  return end || liveToday ? false : null
}

function PinIcon() {
  return (
    <svg className="track-map-icon" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
      <path
        fill="currentColor"
        d="M10 1.5a6 6 0 0 0-6 6c0 4.2 5.1 10.4 5.4 10.7a.8.8 0 0 0 1.2 0C10.9 17.9 16 11.7 16 7.5a6 6 0 0 0-6-6Zm0 8.2a2.2 2.2 0 1 1 0-4.4 2.2 2.2 0 0 1 0 4.4Z"
      />
    </svg>
  )
}

// חיווי נסיעה: קו מסלול עם משאית שזזה. כשהחבילה אצל השליח והמיקום ידוע - המשאית במיקום האמיתי לפי
// ההתקדמות לעבר היעד; אחרת (בדרך לאיסוף / בלי מיקום) - תנועה מתמשכת לציון שהשליח בנסיעה
function RouteTrack({ shipment, direction }) {
  if (shipment.status !== 'OnTheWay' && shipment.status !== 'PickedUp') return null

  const toDropoff = shipment.status === 'PickedUp'
  const progress = toDropoff && shipment.progress != null ? shipment.progress : null
  const startLabel = toDropoff ? 'נאסף' : 'השליח'
  const endLabel = toDropoff ? (direction === 'return' ? 'המשכירה' : 'השוכרת') : 'נקודת האיסוף'
  const percent = progress != null ? Math.round(progress * 100) : null

  return (
    <div
      className="route-track"
      role="img"
      aria-label={
        percent != null ? `השליח בדרך ליעד, כ-${percent}% מהדרך` : toDropoff ? 'השליח בדרך ליעד' : 'השליח בדרך לאיסוף'
      }
    >
      <span className="route-end">{startLabel}</span>
      <div className={`route-line${progress == null ? ' is-indeterminate' : ''}`}>
        <span className="route-fill" style={progress != null ? { width: `${percent}%` } : undefined} />
        <span className="route-truck" style={progress != null ? { insetInlineStart: `${percent}%` } : undefined}>
          <CourierMarker className="route-truck-icon" />
        </span>
      </div>
      <span className="route-end">{endLabel}</span>
    </div>
  )
}

// שורת הסטטוס: משפט ראשון בולט, והמשך (אם יש) כשורה משנית
function StatusLine({ label, bad }) {
  const [headline, ...rest] = String(label || '').split('. ')
  const more = rest.join('. ')
  return (
    <p className={`track-status ${bad ? 'track-status-bad' : ''}`}>
      <strong className="track-status-main">
        {headline}
        {more ? '.' : ''}
      </strong>
      {more && <span className="track-status-sub">{more}</span>}
    </p>
  )
}

function ShipmentCard({ title, shipment, loanDirection, fetchedAt, deadline }) {
  const ended = shipment.status === 'Canceled' || shipment.status === 'Failed'
  const current = stepIndex(shipment.status)
  const hasWindow = shipment.etaDate || shipment.etaTimeRange
  const hasLocation = shipment.latitude != null && shipment.longitude != null
  const liveEta = shipment.etaMinutes != null && shipment.legTarget
  const arrival = liveEta ? new Date(fetchedAt + shipment.etaMinutes * 60000) : null
  const finished = shipment.status === 'Delivered' || ended
  const late = !finished && loanDirection === 'out' ? lateAgainst(deadline, shipment, arrival) : null
  const liveLabel =
    shipment.legTarget === 'Pickup'
      ? 'השליח יגיע לאיסוף בעוד'
      : loanDirection === 'return'
        ? 'הגעה למשכירה בעוד'
        : 'הגעה אליך בעוד'

  return (
    <section className="track-card" aria-label={title}>
      <h3 className="track-card-title">{title}</h3>
      <StatusLine label={shipment.statusLabel} bad={ended} />

      {!ended && (
        <ol className={`track-steps${shipment.status === 'Delivered' ? '' : ' is-live'}`} aria-label="התקדמות המשלוח">
          {STEPS.map((step, i) => (
            <li
              key={step.key}
              className={i < current ? 'is-done' : i === current ? 'is-current' : ''}
              aria-current={i === current ? 'step' : undefined}
            >
              <span className="track-step-dot" aria-hidden="true" />
              <span className="track-step-label">
                {loanDirection === 'return' && step.key === 'PickedUp' ? 'בדרך למשכירה' : step.label}
              </span>
            </li>
          ))}
        </ol>
      )}

      <RouteTrack shipment={shipment} direction={loanDirection} />

      {late !== null && (
        <p className={`track-deadline ${late ? 'is-late' : 'is-ok'}`} role={late ? 'alert' : 'status'}>
          {late
            ? `⚠ חלון ההגעה מסתיים אחרי השעה שביקשת (${deadline.time}). אנחנו בודקים - אפשר לפנות אלינו.`
            : `✓ מתוכנן להגיע לפני השעה שביקשת (${deadline.time})`}
        </p>
      )}

      {liveEta && (
        <p className="track-eta" aria-live="polite">
          <span className="track-eta-label">{liveLabel}</span>
          <strong className="track-eta-value">כ-{shipment.etaMinutes} דקות</strong>
          <span className="payment-status-note">
            {' '}
            (בסביבות {timeHe(arrival)}
            {shipment.remainingKm != null ? ` · כ-${shipment.remainingKm} ק"מ` : ''})
          </span>
        </p>
      )}

      <dl className="track-meta">
        {hasWindow && shipment.status !== 'Delivered' && (
          <div>
            <dt>{loanDirection === 'return' ? 'חלון איסוף לפי חברת השילוח' : 'חלון הגעה לפי חברת השילוח'}</dt>
            <dd>
              {shipment.etaDate ? formatDate(shipment.etaDate) : ''}
              {shipment.etaTimeRange ? ` · ${shipment.etaTimeRange}` : ''}
            </dd>
          </div>
        )}
        {shipment.courierName && (
          <div>
            <dt>שליח</dt>
            <dd>
              {shipment.courierName}
              {shipment.courierPhone && (
                <>
                  {' · '}
                  <a href={`tel:${shipment.courierPhone}`} dir="ltr">
                    {shipment.courierPhone}
                  </a>
                </>
              )}
            </dd>
          </div>
        )}
      </dl>

      {hasLocation && (
        <div className="track-map-row">
          <a
            className="btn btn-primary track-map-btn"
            href={mapUrl(shipment.latitude, shipment.longitude)}
            target="_blank"
            rel="noopener noreferrer"
          >
            <PinIcon />
            הצגת מיקום השליח במפה
          </a>
          {shipment.locationUpdatedAt && (
            <span className="payment-status-note">מיקום עודכן ב-{timeHe(new Date(shipment.locationUpdatedAt))}</span>
          )}
        </div>
      )}

      {shipment.note && <p className="payment-status-note">{shipment.note}</p>}
      {shipment.trackingUrl && (
        <p>
          <a href={shipment.trackingUrl} target="_blank" rel="noopener noreferrer">
            דף המעקב של חברת השילוח
          </a>
        </p>
      )}
    </section>
  )
}

// תצוגת המעקב עצמה (שני הכיוונים: משלוח אל השוכרת ואיסוף ההחזרה), מתרעננת אוטומטית.
// משמשת גם בחלון "מעקב משלוח" וגם במסך "ההזמנה בוצעה"
export function ShipmentPanel({ loan, demoStage }) {
  const [data, setData] = useState(null)
  const [fetchedAt, setFetchedAt] = useState(() => Date.now())
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    if (demoStage) {
      setData(demoShipping(demoStage))
      setFetchedAt(Date.now())
      return
    }
    try {
      const fresh = await loansApi.getShippingStatus(loan.id)
      setData(fresh)
      setFetchedAt(Date.now())
      setError('')
    } catch {
      setError('לא הצלחנו לטעון את מצב המשלוח. נסו שוב בעוד רגע.')
    }
  }, [loan.id, demoStage])

  const live = [data?.outbound, data?.return].some((shipment) => shipment && LIVE_STATUSES.includes(shipment.status))
  const refreshMs = live ? REFRESH_LIVE_MS : REFRESH_IDLE_MS

  // מרעננים רק כשהלשונית גלויה, וגם מיד כשחוזרים אליה
  useEffect(() => {
    load()
    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') load()
    }, refreshMs)
    const onVisible = () => {
      if (document.visibilityState === 'visible') load()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [load, refreshMs])

  const cards = []
  if (loan.outboundTrackable && data?.outbound) cards.push({ id: 'out', title: 'משלוח אל השוכרת', shipment: data.outbound, dir: 'out' })
  if (loan.returnTrackable && data?.return) cards.push({ id: 'ret', title: 'איסוף החזרה', shipment: data.return, dir: 'return' })

  return (
    <div className="track-body">
      {!data && !error && <Loader />}
      {error && <p className="form-error" role="alert">{error}</p>}
      {data && cards.length === 0 && !error && (
        <p className="state-message">אין כרגע מידע מעקב חי למשלוח הזה.</p>
      )}
      {cards.map((c) => (
        <ShipmentCard key={c.id} title={c.title} shipment={c.shipment} loanDirection={c.dir} fetchedAt={fetchedAt}
          deadline={loan.startTime ? { time: loan.startTime, date: String(loan.startDate || '').slice(0, 10) } : null}
        />
      ))}
      <div className="track-actions">
        <button type="button" className="btn btn-ghost btn-small" onClick={load}>
          רענון
        </button>
        <span className="payment-status-note" aria-live="polite">
          {live ? 'המידע מתעדכן אוטומטית כל חצי דקה' : 'המידע מתעדכן אוטומטית כל כמה דקות'}
        </span>
      </div>
    </div>
  )
}

function TrackerModal({ loan, onClose }) {
  return (
    <Modal title="מעקב משלוח" subtitle={loan.itemName} onClose={onClose}>
      <ShipmentPanel loan={loan} />
    </Modal>
  )
}

// כפתור "מעקב משלוח" שפותח חלון עם סטטוס חי, זמן הגעה, שליח ומיקום.
// אם אין מעקב חי (מזהה ישן / מצב מדומה) מוצג ערך המעקב כטקסט, או כקישור אם הוא כתובת.
export function ShipmentTracking({ loan, direction }) {
  const [open, setOpen] = useState(false)
  // רק השכרות פעילות (לא הוחזרו / לא בוטלו) מציגות מעקב
  if (loan.status === 'Returned' || loan.status === 'Cancelled') return null

  const trackable = direction === 'return' ? loan.returnTrackable : loan.outboundTrackable
  const ref = direction === 'return' ? loan.returnTrackingId : loan.outboundTrackingId

  if (!ref && !trackable) return null

  return (
    <>
      {trackable ? (
        <>
          {' '}
          <button type="button" className="btn btn-ghost btn-small track-trigger" onClick={() => setOpen(true)}>
            מעקב משלוח וזמן הגעה
          </button>
          {open && <TrackerModal loan={loan} onClose={() => setOpen(false)} />}
        </>
      ) : /^https?:\/\//i.test(ref) ? (
        <>
          {' · '}
          <a href={ref} target="_blank" rel="noopener noreferrer">
            מעקב משלוח
          </a>
        </>
      ) : (
        <span className="payment-status-note"> · מעקב: {ref}</span>
      )}
    </>
  )
}
