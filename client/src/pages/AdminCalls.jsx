import { useCallback, useEffect, useState } from 'react'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { formatDateTime } from '../utils/format.js'

const KIND_LABELS = {
  Availability: 'אימות זמינות (למשכירה)',
  AvailabilityRetry: 'אימות זמינות - ניסיון חוזר',
  AvailabilityOwnerEmail: 'אימות זמינות - מייל למשכירה',
  Reminder: 'תזכורת החזרה (לשוכרת)',
  NotReturned: 'הפריט לא הוחזר (לשוכרת)',
  ExtensionRequest: 'אישור הארכה (למשכירה)',
  ExtensionApproved: 'הארכה אושרה (לשוכרת)',
  ExtensionDeclined: 'הארכה לא אושרה (לשוכרת)',
  Bridge: 'שיחה מתווכת בין שוכרת למשכירה',
  Welcome: 'שיחת היכרות לחברה חדשה',
  SaleNotice: 'יד 2: הודעת מכירה (למוכרת)',
}

const STATUS_LABELS = {
  Scheduled: 'מתוזמנת',
  RetryScheduled: 'מתוזמנת',
  EmailScheduled: 'מייל מתוזמן',
  Placed: 'חויגה - ממתינה לתוצאה',
  Answered: 'נענתה',
  NoAnswer: 'לא נענתה',
  Busy: 'קו תפוס',
  Failed: 'נכשלה',
  Canceled: 'בוטלה',
}

// כמה מצבים נחשבים "תקינים" (ירוק) ואילו "בעייתיים" (אדום)
const STATUS_TONE = {
  Answered: 'ok',
  Placed: 'pending',
  Scheduled: 'pending',
  RetryScheduled: 'pending',
  NoAnswer: 'bad',
  Busy: 'bad',
  Failed: 'bad',
  Canceled: 'muted',
}

// לשונית "שיחות טלפון" במסך הניהול: יומן כל השיחות האוטומטיות - למי, מתי, ומה התוצאה
export function AdminCalls() {
  const [calls, setCalls] = useState(null)
  const [error, setError] = useState('')
  const [openId, setOpenId] = useState(null)
  const [busyId, setBusyId] = useState(null)
  const [notice, setNotice] = useState('')

  async function resend(call) {
    if (!window.confirm(`לשלוח שוב שיחת היכרות אל ${call.toName} (${call.toPhone})?`)) return
    setBusyId(call.id)
    try {
      const r = await adminApi.resendCall(call.id)
      setNotice(
        r?.scheduledForLocal
          ? `✓ השיחה אל ${call.toName} תצא ב-${formatDateTime(r.scheduledForLocal)}`
          : `✓ השיחה אל ${call.toName} תצא בדקות הקרובות`,
      )
      await load()
    } catch (err) {
      setNotice(err.message)
    } finally {
      setBusyId(null)
    }
  }

  async function resendAvailability(call, viaCall, viaEmail) {
    const what = viaCall && viaEmail ? 'שיחה ומייל' : viaCall ? 'שיחה' : 'מייל'
    if (!window.confirm(`לשלוח שוב ${what} אל ${call.toName} על ${call.itemName}?`)) return
    setBusyId(call.id)
    try {
      const r = await adminApi.resendAvailability(call.id, viaCall, viaEmail)
      const parts = []
      if (viaCall) parts.push(`השיחה תצא ב-${formatDateTime(r.callAtLocal)}`)
      if (viaEmail) parts.push(r.emailed ? 'המייל נשלח' : 'המייל לא נשלח (אין כתובת מייל?)')
      setNotice(`✓ ${call.toName}: ${parts.join(' · ')}`)
      await load()
    } catch (err) {
      setNotice(err.message)
    } finally {
      setBusyId(null)
    }
  }

  async function cancel(call) {
    if (!window.confirm(`לבטל את השיחה אל ${call.toName}?`)) return
    setBusyId(call.id)
    try {
      await adminApi.cancelCall(call.id)
      setNotice(`✓ השיחה אל ${call.toName} בוטלה`)
      await load()
    } catch (err) {
      setNotice(err.message)
    } finally {
      setBusyId(null)
    }
  }

  const load = useCallback(async () => {
    try {
      setCalls(await adminApi.calls())
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  if (error) return <p className="state-message state-error">{error}</p>
  if (!calls) return <Loader label="טוען שיחות" />

  return (
    <section className="admin-calls">
      <div className="admin-calls-head">
        <p className="state-message">
          יומן השיחות האוטומטיות (עד 300 האחרונות). שיחות שנופלות על שבת או חג נדחות אוטומטית למוצאי השבת/החג.
        </p>
        <button type="button" className="btn btn-ghost btn-small" onClick={load}>
          רענון
        </button>
      </div>

      {notice && <p className="form-success">{notice}</p>}

      {calls.length === 0 ? (
        <p className="state-message">עדיין לא יצאו שיחות.</p>
      ) : (
        <div className="table-wrap admin-table-wrap">
          <table>
            <thead>
              <tr>
                <th>מתי</th>
                <th>סוג</th>
                <th>אל</th>
                <th>מתעניינת</th>
                <th>פריט</th>
                <th>תוצאה</th>
                <th>פרטים</th>
                <th>
                  <span className="visually-hidden">פעולות</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {calls.map((call) => (
                <tr key={call.id}>
                  <td>
                    {formatDateTime(call.createdAtUtc)}
                    {call.scheduledForLocal && call.status === 'Scheduled' && (
                      <span className="admin-call-note"> · תצא ב-{formatDateTime(call.scheduledForLocal)}</span>
                    )}
                  </td>
                  <td>{KIND_LABELS[call.kind] ?? call.kind}</td>
                  <td>
                    {call.toName}
                    <span className="admin-call-note"> · {call.toPhone}</span>
                  </td>
                  <td dir="ltr" className="admin-ltr">
                    {call.interestedEmail ? <a href={`mailto:${call.interestedEmail}`}>{call.interestedEmail}</a> : '—'}
                  </td>
                  <td>{call.itemName || '—'}</td>
                  <td>
                    <span className={`admin-call-status admin-call-${STATUS_TONE[call.status] ?? 'muted'}`}>
                      {STATUS_LABELS[call.status] ?? call.status}
                    </span>
                  </td>
                  <td>
                    {call.detail && <span className="admin-call-note">{call.detail} </span>}
                    {call.message && (
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => setOpenId(openId === call.id ? null : call.id)}
                      >
                        {openId === call.id ? 'הסתרת הנוסח' : 'הנוסח שהוקרא'}
                      </button>
                    )}
                    {openId === call.id && call.message && <p className="admin-call-message">{call.message}</p>}
                  </td>
                  <td>
                    {call.status === 'Scheduled' || call.status === 'RetryScheduled' ? (
                      <button type="button" className="btn btn-ghost btn-small" disabled={busyId === call.id} onClick={() => cancel(call)}>
                        ביטול
                      </button>
                    ) : call.kind === 'Availability' || call.kind === 'AvailabilityRetry' ? (
                      <span className="admin-resend-actions">
                        <button type="button" className="btn btn-ghost btn-small" disabled={busyId === call.id} onClick={() => resendAvailability(call, true, false)}>
                          שיחה שוב
                        </button>
                        <button type="button" className="btn btn-ghost btn-small" disabled={busyId === call.id} onClick={() => resendAvailability(call, false, true)}>
                          מייל למשכירה
                        </button>
                      </span>
                    ) : (
                      call.kind === 'Welcome' && (
                        <button type="button" className="btn btn-ghost btn-small" disabled={busyId === call.id} onClick={() => resend(call)}>
                          שליחה שוב
                        </button>
                      )
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
