import { useId, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { usersApi } from '../api/endpoints.js'
import { SITE_CALLER_NUMBER } from '../config.js'
import { useAuth } from '../context/AuthContext.jsx'
import { isValidIsraeliPhone } from '../utils/phone.js'
import { QuietHoursField } from './QuietHoursField.jsx'
import { quietFromUser, quietPayload, quietUserFields } from '../utils/quietHours.js'
import { showRegisteredNotice } from '../utils/registeredNotice.js'
import { useDialogFocus } from '../utils/useDialogFocus.js'

// חשבון בלי טלפון (בעיקר הרשמה עם גוגל) - אי אפשר להמשיך באתר עד שמוסיפים מספר
export function PhoneRequiredGate() {
  const { user, updateUser, logout } = useAuth()
  const navigate = useNavigate()
  const dialogRef = useDialogFocus(() => {})
  const titleId = useId()
  const [phone, setPhone] = useState('')
  const [quiet, setQuiet] = useState(() => quietFromUser(user))
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    if (!isValidIsraeliPhone(phone)) {
      setError('יש להזין מספר טלפון תקין')
      return
    }

    setSubmitting(true)
    try {
      const updated = await usersApi.updateProfile(user.id, { name: user.name, phone: phone.trim(), ...quietPayload(quiet) })
      updateUser({ name: updated.name, phone: updated.phone, ...quietUserFields(updated) })
      showRegisteredNotice()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="modal-backdrop">
      <div ref={dialogRef} className="modal-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
        <div className="modal-header">
          <div className="modal-heading">
            <h2 id={titleId}>עוד צעד אחד וסיימנו</h2>
            <p className="modal-subtitle">כדי להשתמש באתר צריך מספר טלפון</p>
          </div>
          <button
            type="button"
            className="modal-close"
            onClick={() => {
              logout()
              navigate('/', { replace: true })
            }}
            aria-label="סגירה והתנתקות"
          >
            ✕
          </button>
        </div>
        <div className="modal-body">
          <form className="modal-form" onSubmit={handleSubmit}>
            <p>
              מיד אחרי השמירה תקבלי מאיתנו שיחה אוטומטית קצרה מהמספר <bdi className="nowrap">{SITE_CALLER_NUMBER}</bdi> כדי
              שתוכלי לשמור אותו באנשי הקשר שלך. השמירה חשובה מאוד, כי מהמספר הזה תקבלי שיחות בעניינים
              חשובים של ההשכרות שלך.
            </p>

            <label htmlFor="requiredPhone">מספר טלפון</label>
            <input
              id="requiredPhone"
              type="tel"
              inputMode="tel"
              autoComplete="tel"
              dir="ltr"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              required
              autoFocus
            />
            {isValidIsraeliPhone(phone) && <QuietHoursField value={quiet} onChange={setQuiet} idPrefix="gateQuiet" />}

            {error && <p className="form-error">{error}</p>}

            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? 'שומר...' : 'שמירה והמשך'}
            </button>
          </form>
        </div>
      </div>
    </div>
  )
}
