// אם מוגדרת כתובת מפורשת (VITE_API_BASE_URL) - נשתמש רק בה. אחרת, הלקוח יבדוק אוטומטית
// (ב-src/api/client.js) אילו מהכתובות הבאות באמת מגיבה, כדי לא להיתקע על ניחוש שגוי
// של פרופיל ההרצה (http/https) שבו PhotoRental.API עולה בפועל.
export const EXPLICIT_API_BASE_URL = import.meta.env.VITE_API_BASE_URL || null

// שתי הכתובות המוגדרות ב-launchSettings.json של PhotoRental.API (פרופיל http ופרופיל https),
// לפי סדר עדיפות. אם השרת שלכם רץ על כתובת אחרת (למשל IIS/פורט מותאם אישית), הוסיפו אותה כאן.
export const API_BASE_URL_CANDIDATES = [
  'http://localhost:5116/api',
  'https://localhost:7244/api',
]

// פרטי חיבור Firebase Authentication (משמש כרגע רק להתחברות עם Google - ראו firebase.js).
// אלו ערכי תצורה ציבוריים (לא סודיים - זו ההתנהגות הרגילה של Firebase בצד לקוח; ההגנה בפועל היא
// אימות האסימון בשרת, ראו AuthService.LoginWithGoogleAsync), שנלקחים מ-Project Settings > General
// > "Your apps" באזור האישי של הפרויקט בקונסולת Firebase (https://console.firebase.google.com).
// כל עוד לא הוגדרו, כפתור "התחברות עם Google" לא יוצג כלל (ראו GoogleSignInButton.jsx).
export const FIREBASE_CONFIG = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY || '',
  authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN || '',
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID || '',
  appId: import.meta.env.VITE_FIREBASE_APP_ID || '',
}

export const isFirebaseAuthConfigured = Boolean(
  FIREBASE_CONFIG.apiKey && FIREBASE_CONFIG.authDomain && FIREBASE_CONFIG.projectId && FIREBASE_CONFIG.appId,
)

// המספר שממנו יוצאות השיחות האוטומטיות (Twilio:FromPhoneNumber בשרת)
export const SITE_CALLER_NUMBER = '055-4026802'
