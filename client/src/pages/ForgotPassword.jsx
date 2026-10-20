import { useState } from 'react'
import { Link } from 'react-router-dom'
import { authApi } from '../api/endpoints.js'

export function ForgotPassword() {
  const [email, setEmail] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState(null)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      const response = await authApi.forgotPassword(email)
      setResult(response)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="page auth-page">
      <div className="auth-card">
        {result ? (
          <>
            <h1>בדקו את תיבת הדואר</h1>
            <p className="auth-subtitle">{result.message}</p>

            {result.resetToken ? (
              <div className="demo-reset-panel">
                <p className="demo-reset-label">מצב הדגמה - אין באתר שירות דוא"ל אמיתי</p>
                <p>
                  באתר אמיתי הקישור הזה היה נשלח לתיבת המייל שלכם. לצורך ההדגמה, הנה קישור
                  האיפוס ישירות:
                </p>
                <Link to={`/reset-password?token=${result.resetToken}`} className="btn btn-primary">
                  מעבר לאיפוס הסיסמה
                </Link>
              </div>
            ) : (
              <p className="state-message">
                אם קיים חשבון עם כתובת המייל הזו, קיבלתם הודעה עם קישור לאיפוס הסיסמה.
              </p>
            )}

            <p className="auth-switch">
              נזכרתם בסיסמה? <Link to="/login">התחברות</Link>
            </p>
          </>
        ) : (
          <form onSubmit={handleSubmit}>
            <h1>שחזור סיסמה</h1>
            <p className="auth-subtitle">הזינו את כתובת המייל ונשלח לכם קישור לאיפוס הסיסמה</p>

            <label htmlFor="forgotEmail">אימייל</label>
            <input
              id="forgotEmail"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoFocus
            />

            {error && <p className="form-error">{error}</p>}

            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? 'שולח...' : 'שליחת קישור לאיפוס'}
            </button>

            <p className="auth-switch">
              נזכרתם בסיסמה? <Link to="/login">התחברות</Link>
            </p>
          </form>
        )}
      </div>
    </div>
  )
}
