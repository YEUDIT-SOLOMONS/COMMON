import { api } from '../api/client.js'

const COUNT_KEY = 'common_visit_count'
const SESSION_KEY = 'common_visit_counted'

// כל כניסה לאתר (לשונית/חלון חדש) נספרת פעם אחת
export function countVisit() {
  try {
    if (sessionStorage.getItem(SESSION_KEY)) return
    sessionStorage.setItem(SESSION_KEY, '1')
    const count = Number(localStorage.getItem(COUNT_KEY)) || 0
    localStorage.setItem(COUNT_KEY, String(count + 1))
    api.post('/visits', { isNew: count === 0 }, { auth: false }).catch(() => {})
  } catch {
    // בלי אחסון בדפדפן - לא סופרים
  }
}

export function isReturningVisitor() {
  try {
    return (Number(localStorage.getItem(COUNT_KEY)) || 0) >= 2
  } catch {
    return false
  }
}
