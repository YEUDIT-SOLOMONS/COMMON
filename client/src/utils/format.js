const HEBREW_MONTHS = ['ינואר', 'פברואר', 'מרץ', 'אפריל', 'מאי', 'יוני', 'יולי', 'אוגוסט', 'ספטמבר', 'אוקטובר', 'נובמבר', 'דצמבר']

// "2026-10" -> "אוקטובר 2026"
export function formatMonth(month) {
  if (!month || !/^\d{4}-\d{2}$/.test(month)) return month ?? ''
  const [year, number] = month.split('-')
  return `${HEBREW_MONTHS[Number(number) - 1]} ${year}`
}

export function formatMoney(value) {
  return `₪${Number(value ?? 0).toLocaleString('he-IL', { minimumFractionDigits: 0, maximumFractionDigits: 2 })}`
}

// ===== שעון ישראל =====
// השרת שומר מועדי השכרה (תחילה/החזרה) כ"שעון קיר" של ישראל, ושולח אותם בלי סימון אזור זמן (למשל 2026-10-07T12:00:00) -
// כאלה מוצגים כמו שהם. מועדים אמיתיים (נשלחים עם Z, למשל מתי נאסף הפריט) מומרים לשעון ישראל, בלי קשר לאזור הזמן של המכשיר.
const ISRAEL_TZ = 'Asia/Jerusalem'
const HAS_ZONE = /(Z|[+-]\d{2}:?\d{2})$/i

function zoneOptions(value) {
  return typeof value === 'string' && HAS_ZONE.test(value) ? { timeZone: ISRAEL_TZ } : {}
}

// "עכשיו" בישראל כשעון קיר: YYYY-MM-DDTHH:mm:ss
export function israelNowWall() {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-GB', {
      timeZone: ISRAEL_TZ,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
      .formatToParts(new Date())
      .map((part) => [part.type, part.value]),
  )
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}`
}

// התאריך של היום בישראל (YYYY-MM-DD)
export function todayIsraelIso() {
  return israelNowWall().slice(0, 10)
}

// שעון קיר -> מספר (אפשר להפחית שני כאלה כדי לקבל הפרש אמיתי בין שני מועדים של ישראל)
export function wallMs(wall) {
  return Date.parse(String(wall).replace(/Z$/i, '').split('.')[0] + 'Z')
}

export function formatDate(value) {
  if (!value) return '—'
  return new Date(value).toLocaleDateString('he-IL', zoneOptions(value))
}

export function formatDateTime(value) {
  if (!value) return '—'
  return new Date(value).toLocaleString('he-IL', { dateStyle: 'short', timeStyle: 'short', ...zoneOptions(value) })
}

// אחוז עמלת השירות המוצג בתיבת האישור (השרת מחשב לפי Commission:Percent, ברירת מחדל 8)
export const COMMISSION_PERCENT = 8
