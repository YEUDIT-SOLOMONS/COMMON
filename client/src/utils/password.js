export const PASSWORD_RULE = 'לפחות 6 תווים, משולבים אותיות ומספרים'

export function passwordProblem(password) {
  const value = password ?? ''
  if (value.length < 6 || !/\p{L}/u.test(value) || !/\d/.test(value)) {
    return `הסיסמה צריכה לכלול ${PASSWORD_RULE}`
  }
  return ''
}
