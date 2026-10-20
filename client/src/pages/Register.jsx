import { PasswordInput } from '../components/PasswordInput.jsx'
import { useCallback, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark.jsx'
import { HandshakeGlyph, LongArrowGlyph } from '../components/glyphs.jsx'
import { GoogleSignInButton } from '../components/GoogleSignInButton.jsx'
import { useAuth } from '../context/AuthContext.jsx'
import { showWelcome } from '../utils/welcomeTransition.js'
import { showRegisteredNotice } from '../utils/registeredNotice.js'
import { isValidIsraeliPhone } from '../utils/phone.js'
import { QuietHoursField } from '../components/QuietHoursField.jsx'
import { DEFAULT_QUIET, quietPayload } from '../utils/quietHours.js'
import { passwordProblem } from '../utils/password.js'
import { SITE_CALLER_NUMBER } from '../config.js'

export function Register() {
  const { register, loginWithGoogle } = useAuth()
  const navigate = useNavigate()

  const [form, setForm] = useState({
    name: '',
    email: '',
    phone: '',
    password: '',
  })
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [quiet, setQuiet] = useState(DEFAULT_QUIET)
  const phoneValid = isValidIsraeliPhone(form.phone)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  function update(field) {
    return (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    if (!isValidIsraeliPhone(form.phone)) {
      setError('יש להזין מספר טלפון תקין')
      return
    }
    const weakPassword = passwordProblem(form.password)
    if (weakPassword) {
      setError(weakPassword)
      return
    }
    setSubmitting(true)
    try {
      const created = await register({ ...form, termsAccepted, ...quietPayload(quiet) })
      await showWelcome(created?.name)
      navigate('/profile', { replace: true })
      showRegisteredNotice()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  const handleGoogleToken = useCallback(
    async (idToken) => {
      setError('')
      try {
        const loggedIn = await loginWithGoogle(idToken)
        await showWelcome(loggedIn?.name)
        navigate('/profile', { replace: true })
      } catch (err) {
        setError(err.message)
      }
    },
    [loginWithGoogle, navigate],
  )

  return (
    <div className="page auth-page auth-split">
      <aside className="auth-aside">
        <svg className="auth-arc" viewBox="0 0 232.6 116.8" aria-hidden="true" focusable="false">
          <path d="M 0.5 116.8 A 115.8 115.8 0 0 1 232.1 116.8" />
        </svg>
        <BrandMark className="auth-mark">
          <HandshakeGlyph className="gl gl-handshake" />
        </BrandMark>
        <h1>הצטרפות לקהילה</h1>
        <p className="auth-subtitle">
          הצטרפי לקהילת הצלמות של COMMON. שתפי ושכרי ציוד צילום אחת מהשנייה
          <LongArrowGlyph className="auth-arrow" />
        </p>
      </aside>

      <form className="auth-card" onSubmit={handleSubmit}>

        <label htmlFor="name">שם מלא</label>
        <input id="name" value={form.name} onChange={update('name')} required />

        <label htmlFor="email">אימייל</label>
        <input id="email" type="email" value={form.email} onChange={update('email')} required />

        <label htmlFor="phone">טלפון</label>
        <input id="phone" type="tel" inputMode="tel" autoComplete="tel" dir="ltr" value={form.phone} onChange={update('phone')} required aria-describedby={phoneValid ? 'phoneHint' : undefined} />
        {phoneValid && (
        <p id="phoneHint" className="field-hint" role="status">
          מיד בסיום ההרשמה תקבלי מאיתנו שיחה אוטומטית קצרה מהמספר <bdi className="nowrap">{SITE_CALLER_NUMBER}</bdi> כדי שתוכלי לשמור אותו באנשי הקשר שלך. השמירה חשובה מאוד, כי מהמספר הזה תקבלי שיחות בעניינים חשובים של ההשכרות שלך.
        </p>
        )}
        {phoneValid && <QuietHoursField value={quiet} onChange={setQuiet} idPrefix="registerQuiet" />}

        <label htmlFor="password">סיסמה</label>
        <PasswordInput
          id="password"
          value={form.password}
          onChange={update('password')}
          minLength={6}
          required
        />

        <label className="checkbox-row">
          <input type="checkbox" checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} required />
          <span>
            קראתי ואני מסכימה ל
            <Link to="/terms" target="_blank" rel="noopener noreferrer">
              תנאי השימוש
            </Link>
          </span>
        </label>

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting || !termsAccepted}>
          {submitting ? 'מצטרפים...' : 'הצטרפות'}
        </button>

        <GoogleSignInButton onSuccess={handleGoogleToken} onError={setError} />
        <p className="payment-status-note">
          בהצטרפות עם Google את מאשרת את <Link to="/terms" target="_blank" rel="noopener noreferrer">תנאי השימוש</Link>.
        </p>

        <p className="auth-switch">
          כבר יש לכם חשבון? <Link to="/login">התחברות</Link>
        </p>
      </form>
    </div>
  )
}
