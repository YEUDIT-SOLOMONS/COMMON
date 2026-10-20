import { PasswordInput } from '../components/PasswordInput.jsx'
import { PASSWORD_RULE, passwordProblem } from '../utils/password.js'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { authApi } from '../api/endpoints.js'

export function ResetPassword() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const token = searchParams.get('token') ?? ''

  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [done, setDone] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')

    if (passwordProblem(password)) {
      setError(passwordProblem(password))
      return
    }
    if (password !== confirmPassword) {
      setError('הסיסמאות אינן תואמות')
      return
    }

    setSubmitting(true)
    try {
      await authApi.resetPassword({ token, newPassword: password })
      setDone(true)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  if (!token) {
    return (
      <div className="page auth-page">
        <div className="auth-card">
          <h1>קישור לא תקין</h1>
          <p className="state-message">קישור איפוס הסיסמה חסר או שגוי.</p>
          <Link to="/forgot-password" className="btn btn-primary">
            בקשת קישור חדש
          </Link>
        </div>
      </div>
    )
  }

  if (done) {
    return (
      <div className="page auth-page">
        <div className="auth-card">
          <h1>הסיסמה עודכנה</h1>
          <p className="state-message">הסיסמה שלכם עודכנה בהצלחה. אפשר להתחבר עכשיו.</p>
          <button type="button" className="btn btn-primary" onClick={() => navigate('/login')}>
            מעבר להתחברות
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="page auth-page">
      <form className="auth-card" onSubmit={handleSubmit}>
        <h1>הגדרת סיסמה חדשה</h1>

        <label htmlFor="newPassword">סיסמה חדשה</label>
        <PasswordInput
          id="newPassword"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          minLength={6}
          required
          autoFocus
          aria-describedby="newPasswordHint"
        />
        <p id="newPasswordHint" className="field-hint">
          {PASSWORD_RULE}
        </p>

        <label htmlFor="confirmPassword">אימות סיסמה</label>
        <PasswordInput
          id="confirmPassword"
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          minLength={6}
          required
        />

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'מעדכן...' : 'עדכון סיסמה'}
        </button>
      </form>
    </div>
  )
}
