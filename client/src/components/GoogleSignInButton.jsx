import { useState } from 'react'
import { isFirebaseAuthConfigured } from '../config.js'
import { signInWithGoogle } from '../firebase.js'

// כפתור "התחברות עם Google", מבוסס Firebase Authentication - מוצג רק כאשר הוגדרו פרטי חיבור
// Firebase אמיתיים בצד הלקוח (ראו isFirebaseAuthConfigured/FIREBASE_CONFIG ב-config.js). הלחיצה
// פותחת את חלון הפופ-אפ הרשמי של Google (מנוהל ע"י Firebase, ראו firebase.js); בהצלחה מתקבל
// אסימון הזהות של Firebase ומועבר ל-onSuccess - האימות בפועל שהאסימון תקין מתבצע בשרת
// (ראו AuthService.LoginWithGoogleAsync), ולא כאן בלקוח.
export function GoogleSignInButton({ onSuccess, onError }) {
  const [busy, setBusy] = useState(false)

  if (!isFirebaseAuthConfigured) return null

  async function handleClick() {
    setBusy(true)
    try {
      const idToken = await signInWithGoogle()
      await onSuccess(idToken)
    } catch (err) {
      onError?.(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="google-signin-wrap">
      <div className="auth-divider">
        <span>או -</span>
      </div>
      <button type="button" className="google-signin-button" onClick={handleClick} disabled={busy}>
        <GoogleLogo />
        {busy ? 'מתחבר...' : 'המשך עם google'}
      </button>
    </div>
  )
}

function GoogleLogo() {
  return (
    <svg width="18" height="18" viewBox="0 0 18 18" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <path
        fill="#4285F4"
        d="M17.64 9.2c0-.64-.06-1.25-.16-1.84H9v3.48h4.84c-.21 1.13-.84 2.09-1.8 2.73v2.27h2.92c1.71-1.57 2.68-3.88 2.68-6.64z"
      />
      <path
        fill="#34A853"
        d="M9 18c2.43 0 4.47-.8 5.96-2.18l-2.92-2.27c-.81.54-1.84.86-3.04.86-2.34 0-4.32-1.58-5.03-3.7H.96v2.34C2.44 15.98 5.48 18 9 18z"
      />
      <path
        fill="#FBBC05"
        d="M3.97 10.71c-.18-.54-.28-1.11-.28-1.71s.1-1.17.28-1.71V4.95H.96C.35 6.17 0 7.55 0 9s.35 2.83.96 4.05l3.01-2.34z"
      />
      <path
        fill="#EA4335"
        d="M9 3.58c1.32 0 2.5.45 3.44 1.35l2.58-2.58C13.46.89 11.43 0 9 0 5.48 0 2.44 2.02.96 4.95l3.01 2.34C4.68 5.16 6.66 3.58 9 3.58z"
      />
    </svg>
  )
}
