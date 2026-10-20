// עזרים ללוח הזמנים של פריט. הטווחים מהשרת הם שעון קיר של ישראל: "2026-11-10T10:00:00"

export function isoDay(date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

// יום תפוס אם טווח כלשהו נוגע בו
export function isDayBusy(day, ranges) {
  return (ranges ?? []).some((r) => r.from.slice(0, 10) <= day && r.to.slice(0, 10) >= day)
}

// האם הטווח המבוקש (תאריך+שעה) חופף טווח תפוס
export function rangeOverlaps(startDay, startTime, endDay, endTime, ranges) {
  const start = `${startDay}T${startTime || '00:00'}`
  const end = `${endDay}T${endTime || '23:59'}`
  return (ranges ?? []).some((r) => start < r.to.slice(0, 16) && end > r.from.slice(0, 16))
}

export function daysInRange(fromDay, toDay) {
  const out = []
  const d = new Date(`${fromDay}T12:00:00`)
  const end = new Date(`${toDay}T12:00:00`)
  while (d <= end) {
    out.push(isoDay(d))
    d.setDate(d.getDate() + 1)
  }
  return out
}

const at = (v) => String(v ?? '').slice(0, 16)

function peakUnits(bookings, start, end) {
  const events = []
  for (const b of bookings ?? []) {
    const from = at(b.from)
    const to = at(b.to)
    if (from >= end || to <= start) continue
    events.push([from < start ? start : from, b.units || 1])
    events.push([to > end ? end : to, -(b.units || 1)])
  }
  events.sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : a[1] - b[1]))
  let current = 0
  let peak = 0
  for (const [, delta] of events) {
    current += delta
    peak = Math.max(peak, current)
  }
  return peak
}

// כמה יחידות פנויות בטווח שנבחר (פריט עם כמה עותקים)
export function freeUnits(schedule, startDay, startTime, endDay, endTime) {
  if (!schedule) return null
  const start = `${startDay}T${startTime || '00:00'}`
  const end = `${endDay}T${endTime || '23:59'}`
  if ((schedule.blocked ?? []).some((r) => start < at(r.to) && end > at(r.from))) return 0
  return Math.max(0, (schedule.quantity || 1) - peakUnits(schedule.bookings, start, end))
}

// טווחים שבהם אין מספיק יחידות פנויות לכמות המבוקשת
export function busyRangesFor(schedule, units) {
  if (!schedule) return []
  const quantity = schedule.quantity || 1
  if (quantity <= 1 || units <= 1) return schedule.busy ?? []
  const limit = quantity - units
  const events = (schedule.bookings ?? [])
    .flatMap((b) => [
      [at(b.from), b.units || 1],
      [at(b.to), -(b.units || 1)],
    ])
    .sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : a[1] - b[1]))
  const ranges = []
  let current = 0
  let openedAt = null
  for (const [time, delta] of events) {
    current += delta
    if (current > limit && openedAt === null) openedAt = time
    else if (current <= limit && openedAt !== null) {
      if (time > openedAt) ranges.push({ from: openedAt, to: time })
      openedAt = null
    }
  }
  return [...ranges, ...(schedule.blocked ?? [])]
}
