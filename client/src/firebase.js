import { initializeApp, getApps } from 'firebase/app'
import { GoogleAuthProvider, getAuth, signInWithPopup } from 'firebase/auth'
import { FIREBASE_CONFIG, isFirebaseAuthConfigured } from './config.js'

// אתחול חד-פעמי של Firebase (רק להתחברות עם Google כרגע - לא בשימוש עבור Firestore, שממשיך
// להתבצע דרך ה-REST API הרגיל של השרת). לא מאתחלים כלל אם לא הוגדרו פרטי החיבור בצד הלקוח
// (ראו isFirebaseAuthConfigured ב-config.js), כדי שהאתר ימשיך לעבוד כרגיל גם בלי Firebase Auth.
const auth = isFirebaseAuthConfigured
  ? getAuth(getApps().length > 0 ? getApps()[0] : initializeApp(FIREBASE_CONFIG))
  : null

const googleProvider = new GoogleAuthProvider()

// פותח את חלון הפופ-אפ הרשמי של Google להתחברות, ומחזיר את אסימון הזהות (ID token) של Firebase
// שיש לשלוח לשרת (ראו authApi.google ו-AuthService.LoginWithGoogleAsync) לאימות ולהתחברות בפועל -
// Firebase עצמו לא "מחובר" מבחינת common עד שהשרת מאמת את האסימון ומנפיק טוקן משלו.
export async function signInWithGoogle() {
  if (!auth) {
    throw new Error('התחברות עם Google לא הוגדרה באתר זה')
  }
  try {
    const result = await signInWithPopup(auth, googleProvider)
    return await result.user.getIdToken()
  } catch (err) {
    throw new Error(describeGoogleError(err))
  }
}

// הודעות ברורות בעברית לכשלי ההתחברות עם גוגל. חשוב במיוחד: ברשתות מסוננות (נטפרי וכד') הבקשות לגוגל נחסמות
// ואז מתקבלת שגיאת רשת - המשתמשת צריכה לדעת שאפשר להתחבר גם עם אימייל וסיסמה.
function describeGoogleError(err) {
  switch (err?.code) {
    case 'auth/network-request-failed':
      return 'לא הצלחנו להתחבר לגוגל - ייתכן שהרשת מסוננת וחוסמת אותו. אפשר להתחבר עם אימייל וסיסמה.'
    case 'auth/popup-blocked':
      return 'הדפדפן חסם את חלון ההתחברות של גוגל. יש לאפשר חלונות קופצים לאתר ולנסות שוב.'
    case 'auth/popup-closed-by-user':
    case 'auth/cancelled-popup-request':
      return 'חלון ההתחברות של גוגל נסגר לפני שהסתיים. אפשר לנסות שוב.'
    case 'auth/unauthorized-domain':
      return 'התחברות עם גוגל עדיין לא הופעלה עבור כתובת האתר הזו. אפשר להתחבר עם אימייל וסיסמה.'
    default:
      return err?.message || 'ההתחברות עם גוגל נכשלה. אפשר להתחבר עם אימייל וסיסמה.'
  }
}
