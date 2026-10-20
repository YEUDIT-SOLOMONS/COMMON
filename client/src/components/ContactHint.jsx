const CONTACT = /[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+|(?<!\d)(?:\+?\D{0,2}9\D{0,2}7\D{0,2}2|0)\D{0,3}[2-9](?:\D{0,3}\d){7,8}(?!\D{0,3}\d)|(?:(?:אפס|אחת|אחד|שתיים|שתים|שניים|שלוש|ארבע|חמש|שש|שבע|שמונה|תשע)[\s,.\-]*){7,}|wa\.me\/|chat\.whatsapp\.com/

const MAIL_WORD = /שטרודל|שטרודעל|ג['׳]?י?מייל|g-?mail|walla|וואלה|yahoo|יאהו|hotmail|outlook|icloud|instagram|אינסטגרם|אינסטה|facebook|פייסבוק|telegram|טלגרם|\.com|דוט קום|נקודה קום/i
const MAIL_NAME = /מייל[^A-Za-z]{0,30}[A-Za-z][A-Za-z0-9._-]{3,}|[A-Za-z][A-Za-z0-9._-]{3,}[^A-Za-z]{0,30}מייל/

// מופיע רק כשמקלידים פרטי קשר
export function ContactHint({ text }) {
  if (!text || !(CONTACT.test(text) || MAIL_WORD.test(text) || MAIL_NAME.test(text))) return null
  return <p className="contact-hint">פרטי קשר מוסתרים אוטומטית - ההזמנה והתשלום מתבצעים דרך האתר בלבד</p>
}
