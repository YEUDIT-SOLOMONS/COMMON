const listeners = new Set()

export function subscribeRegisteredNotice(listener) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function showRegisteredNotice(title = 'נרשמת בהצלחה! 📞') {
  listeners.forEach((listener) => listener(title))
}
