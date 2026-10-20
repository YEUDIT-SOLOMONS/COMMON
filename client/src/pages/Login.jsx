import { PasswordInput } from '../components/PasswordInput.jsx'
import { useCallback, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark.jsx'
import { HandshakeGlyph } from '../components/glyphs.jsx'
import { GoogleSignInButton } from '../components/GoogleSignInButton.jsx'
import { SESSION_EXPIRED_KEY, useAuth } from '../context/AuthContext.jsx'
import { showWelcome } from '../utils/welcomeTransition.js'

export function Login() {
  const { login, loginWithGoogle } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [sessionExpired] = useState(() => {
    try {
      const expired = sessionStorage.getItem(SESSION_EXPIRED_KEY) === '1'
      sessionStorage.removeItem(SESSION_EXPIRED_KEY)
      return expired
    } catch {
      return false
    }
  })
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      const loggedIn = await login(email, password)
      await showWelcome(loggedIn?.name)
      navigate(location.state?.from ?? '/profile', { replace: true })
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
        navigate(location.state?.from ?? '/profile', { replace: true })
      } catch (err) {
        setError(err.message)
      }
    },
    [loginWithGoogle, navigate, location.state],
  )

  return (
    <div className="page auth-page auth-split">
      <aside className="auth-aside auth-aside-centered">
        <svg className="auth-arc" viewBox="0 0 232.6 116.8" aria-hidden="true" focusable="false">
          <path d="M 0.5 116.8 A 115.8 115.8 0 0 1 232.1 116.8" />
        </svg>
        <BrandMark className="auth-mark">
          <HandshakeGlyph className="gl gl-handshake" />
        </BrandMark>
        <h1>התחברות</h1>
        <p className="auth-subtitle">
          שמחות לראות אותך שוב!
        </p>
      </aside>

      <form className="auth-card" onSubmit={handleSubmit}>
        {sessionExpired && (
          <p className="form-error" role="status">
            ההתחברות פגה. יש להתחבר מחדש.
          </p>
        )}

        <label htmlFor="email">אימייל</label>
        <input
          id="email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />

        <div className="auth-label-row">
          <label htmlFor="password">סיסמה</label>
          <Link to="/forgot-password" className="auth-forgot-link">
            שכחתי סיסמה
          </Link>
        </div>
        <PasswordInput
          id="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'מתחבר...' : 'התחברות'}
        </button>

        <GoogleSignInButton onSuccess={handleGoogleToken} onError={setError} />

        <p className="auth-switch">
          עדיין אין לכם חשבון? <Link to="/register">הצטרפות לקהילה</Link>
        </p>
      </form>
    </div>
  )
}
