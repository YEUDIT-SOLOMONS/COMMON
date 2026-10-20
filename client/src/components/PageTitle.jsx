import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'

const BASE = 'common | קהילת הצלמים להשכרת ציוד'

const TITLES = [
  [/^\/$/, BASE],
  [/^\/login$/, 'התחברות | common'],
  [/^\/register$/, 'הצטרפות לקהילה | common'],
  [/^\/forgot-password$/, 'שחזור סיסמה | common'],
  [/^\/reset-password$/, 'איפוס סיסמה | common'],
  [/^\/profile$/, 'האזור האישי | common'],
  [/^\/admin/, 'ניהול האתר | common'],
  [/^\/order\/demo$/, 'דוגמה להזמנה | common'],
  [/^\/order\/[^/]+\/return$/, 'שינוי אופן ההחזרה | common'],
  [/^\/order\/[^/]+$/, 'ההזמנה שלך | common'],
  [/^\/order-paid$/, 'אישור תשלום להזמנה | common'],
  [/^\/shipping-paid$/, 'אישור תשלום דמי משלוח | common'],
  [/^\/loan-action\//, 'חלון סימון | common'],
  [/^\/item-action\//, 'עדכון זמינות פריט | common'],
  [/^\/questions\//, 'התכתבות על פריט | common'],
  [/^\/yad2$/, 'לוח יד 2 | common'],
  [/^\/purchase\//, 'רכישת יד 2 | common'],
  [/^\/pickup\//, 'קבלת הפריט | common'],
  [/^\/availability-answer\//, 'אימות זמינות | common'],
  [/^\/commission-proof$/, 'אסמכתת תשלום עמלה | common'],
  [/^\/commission-pay$/, 'תשלום עמלה | common'],
  [/^\/commission-paid$/, 'אישור תשלום עמלה | common'],
  [/^\/terms$/, 'תנאי שימוש | common'],
  [/^\/contact$/, 'יצירת קשר | common'],
]

// כותרת לשונית נפרדת לכל דף, לפי הכתובת
export function PageTitle() {
  const { pathname } = useLocation()

  useEffect(() => {
    const match = TITLES.find(([pattern]) => pattern.test(pathname))
    document.title = match ? match[1] : 'העמוד לא נמצא | common'
  }, [pathname])

  return null
}
