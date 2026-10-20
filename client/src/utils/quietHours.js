export const DEFAULT_QUIET = { enabled: false, from: '00:30', to: '07:00' }

export function quietFromUser(user) {
  return {
    enabled: Boolean(user?.quietHoursEnabled),
    from: user?.quietFrom || DEFAULT_QUIET.from,
    to: user?.quietTo || DEFAULT_QUIET.to,
  }
}

export function quietPayload(quiet) {
  return { quietHoursEnabled: quiet.enabled, quietFrom: quiet.from, quietTo: quiet.to }
}

export function quietUserFields(updated) {
  return {
    quietHoursEnabled: updated.quietHoursEnabled,
    quietFrom: updated.quietFrom,
    quietTo: updated.quietTo,
  }
}
