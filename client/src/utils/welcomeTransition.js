// מעבר אחרי התחברות/הצטרפות (כמו במסך הפתיחה): מסך ברוכה הבאה, ואז העיגול הוורוד מתרחב וממלא את כל המסך,
// בזמן שהאתר עובר מתחתיו לאזור האישי, ולבסוף המסך נמוג וחושף אותו. המסך עצמו מוצג ע"י WelcomeTransition ב-App.
const listeners = new Set()

export const WELCOME_GREETING_MS = 600 // כמה זמן מוצגת הברכה לפני שהעיגול הוורוד מתחיל להתרחב
export const WELCOME_GROW_MS = 480 // משך התרחבות העיגול הוורוד
export const WELCOME_FADE_MS = 350 // משך היעלמות המסך אחרי שמילא את המסך
// הניווט מתבצע כשהמסך כבר מכוסה כולו בוורוד
export const WELCOME_NAVIGATE_MS = WELCOME_GREETING_MS + WELCOME_GROW_MS + 30

export function subscribeWelcome(listener) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

// מציג את המסך ומחזיר Promise שמסתיים כשמותר לנווט (המסך כבר מכסה הכול)
export function showWelcome(name) {
  listeners.forEach((listener) => listener(name))
  return new Promise((resolve) => window.setTimeout(resolve, WELCOME_NAVIGATE_MS))
}
