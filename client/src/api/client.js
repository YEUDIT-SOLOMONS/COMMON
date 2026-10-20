import { API_BASE_URL_CANDIDATES, EXPLICIT_API_BASE_URL } from '../config.js'
import { DEMO_SALE, demoRequest } from './demoSale.js'

const TOKEN_STORAGE_KEY = 'photorental.token'
const RESOLVED_BASE_STORAGE_KEY = 'photorental.apiBaseUrl'

export function getStoredToken() {
  return localStorage.getItem(TOKEN_STORAGE_KEY)
}

export function setStoredToken(token) {
  if (token) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token)
  } else {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
  }
}

// מזהה אוטומטית איזו כתובת מבין המועמדות (config.js) הוא זה שבו PhotoRental.API באמת רץ כרגע,
// כדי שלא יהיה תלוי בניחוש איזה פרופיל הרצה (http/https) המשתמש בחר ב-Visual Studio.
// התוצאה נשמרת בזיכרון וב-localStorage כדי לא לבדוק מחדש בכל בקשה.
const FRIENDLY_ERRORS = {
  400: 'הבקשה אינה תקינה - ייתכן שהקישור או הפרטים שגויים',
  401: 'יש להתחבר כדי להמשיך',
  403: 'אין הרשאה לפעולה הזו',
  404: 'המידע המבוקש לא נמצא',
  429: 'יותר מדי ניסיונות. נסו שוב בעוד כמה דקות.',
}

// התחברות שפגה: השרת מחזיר 401 בלי הודעה (שגיאות 401 של הקוד עצמו תמיד כוללות הודעה)
let sessionExpiredHandler = null

export function onSessionExpired(handler) {
  sessionExpiredHandler = handler
}

let resolvedBaseUrl = EXPLICIT_API_BASE_URL || localStorage.getItem(RESOLVED_BASE_STORAGE_KEY) || null
let resolvingPromise = null

async function resolveBaseUrl({ forceRecheck = false } = {}) {
  if (EXPLICIT_API_BASE_URL) {
    return EXPLICIT_API_BASE_URL
  }

  if (resolvedBaseUrl && !forceRecheck) {
    return resolvedBaseUrl
  }

  if (resolvingPromise) {
    return resolvingPromise
  }

  resolvingPromise = (async () => {
    for (const candidate of API_BASE_URL_CANDIDATES) {
      try {
        // /categories היא נקודת קצה קלה, פתוחה וללא תלות בהרשאות - טובה כ"בדיקת דופק"
        // לזיהוי אם השרת בכתובת הזו בכלל מגיב (גם תשובת שגיאה נחשבת "מגיב" - העיקר שההתחברות הצליחה).
        await fetch(`${candidate}/categories`, { method: 'GET' })
        resolvedBaseUrl = candidate
        localStorage.setItem(RESOLVED_BASE_STORAGE_KEY, candidate)
        return candidate
      } catch {
        // הכתובת הזו לא מגיבה - ננסה את הבאה בתור
      }
    }

    resolvedBaseUrl = null
    localStorage.removeItem(RESOLVED_BASE_STORAGE_KEY)

    const triedList = API_BASE_URL_CANDIDATES.join(' , ')
    throw new Error(
      `לא ניתן להתחבר לשרת. נוסו הכתובות: ${triedList} - אף אחת מהן לא הגיבה. ` +
        'ודאו ש-PhotoRental.API רץ (בדקו שה-Swagger נפתח, והעתיקו את הכתובת המדויקת שמופיעה שם). ' +
        'אם הכתובת שרואים ב-Swagger שונה מהרשימה כאן, הוסיפו אותה ל-API_BASE_URL_CANDIDATES ב- src/config.js. ' +
        'אם מדובר בכתובת https - ודאו שתעודת הפיתוח מאושרת (dotnet dev-certs https --trust) ושהדפדפן לא חוסם אותה.',
    )
  })()

  try {
    return await resolvingPromise
  } finally {
    resolvingPromise = null
  }
}

// עטיפת fetch אחידה: מוסיפה כותרות JSON, מצרפת טוקן התחברות (אם קיים), פונה לכתובת השרת
// שזוהתה אוטומטית, והופכת תשובות שגיאה (לפי הפורמט של GlobalExceptionMiddleware בשרת:
// {statusCode, message}) לחריגת JavaScript עם הודעה קריאה - כדי שכל מסך יוכל להציג אותה ישירות למשתמש.
async function request(path, { method = 'GET', body, auth = true, _retried = false } = {}) {
  if (DEMO_SALE) return demoRequest(path, { method, body })
  const baseUrl = await resolveBaseUrl()

  const headers = { 'Content-Type': 'application/json' }

  if (auth) {
    const token = getStoredToken()
    if (token) {
      headers.Authorization = `Bearer ${token}`
    }
  }

  let response
  try {
    response = await fetch(`${baseUrl}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    // הכתובת שהייתה תקינה בעבר הפסיקה להגיב (למשל השרת הופעל מחדש בפורט אחר) -
    // ננסה פעם אחת לזהות מחדש איזו כתובת עובדת, לפני שמוותרים ומציגים שגיאה.
    if (!_retried) {
      resolvedBaseUrl = null
      await resolveBaseUrl({ forceRecheck: true })
      return request(path, { method, body, auth, _retried: true })
    }

    throw new Error(
      'לא ניתן להתחבר לשרת. ודאו ש-PhotoRental.API רץ (בדקו ב-Swagger), ונסו לרענן את הדף.',
    )
  }

  if (response.status === 204) {
    return null
  }

  const text = await response.text()
  const data = text ? safeJsonParse(text) : null

  if (response.status === 401 && headers.Authorization && !(data && (data.message || data.Message))) {
    sessionExpiredHandler?.()
    throw new Error('ההתחברות פגה. יש להתחבר מחדש.')
  }

  if (!response.ok) {
    const message = (data && (data.message || data.Message)) || FRIENDLY_ERRORS[response.status] || 'אירעה תקלה. נסו שוב בעוד רגע.'
    throw new Error(message)
  }

  return data
}

function safeJsonParse(text) {
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

export const api = {
  get: (path) => request(path),
  post: (path, body, options) => request(path, { method: 'POST', body, ...options }),
  put: (path, body) => request(path, { method: 'PUT', body }),
  patch: (path, body) => request(path, { method: 'PATCH', body }),
  del: (path) => request(path, { method: 'DELETE' }),
}
