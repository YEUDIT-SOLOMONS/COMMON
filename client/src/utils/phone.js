export function isValidIsraeliPhone(value) {
  const digits = (value ?? '').replace(/\D/g, '')
  const local = digits.startsWith('972') ? `0${digits.slice(3)}` : digits
  return /^0\d{8,9}$/.test(local)
}
