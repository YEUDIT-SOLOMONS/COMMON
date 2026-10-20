import { useCallback, useEffect, useState } from 'react'
import { itemsApi } from '../api/endpoints.js'
import { formatDate, formatDateTime } from '../utils/format.js'
import { isDayBusy } from '../utils/schedule.js'
import { Modal } from './Modal.jsx'
import { MonthCalendar } from './MonthCalendar.jsx'

function inRanges(day, list) {
  return list.some((r) => r.from.slice(0, 10) <= day && r.to.slice(0, 10) >= day)
}

// לוח הזמנים של פריט - למשכירה: ההשכרות (עם שם השוכרת), ימים שחסמה, וחסימת ימים חדשים בלחיצה על הלוח
export function ItemScheduleModal({ item, onClose }) {
  const [schedule, setSchedule] = useState(null)
  const [error, setError] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)

  const reload = useCallback(() => {
    itemsApi
      .schedule(item.id)
      .then(setSchedule)
      .catch((err) => setError(err.message))
  }, [item.id])

  useEffect(() => {
    reload()
  }, [reload])

  const rentals = schedule?.rentals ?? []
  const blocks = schedule?.blocks ?? []
  const rentalRanges = rentals.map((r) => ({ from: r.from, to: r.to }))
  const blockRanges = blocks.map((b) => ({ from: b.from, to: b.to }))

  function pickDay(day) {
    setError('')
    if (!from || (from && to) || day < from) {
      setFrom(day)
      setTo('')
    } else {
      setTo(day)
    }
  }

  async function addBlock(e) {
    e.preventDefault()
    if (!from) {
      setError('בחרי בלוח את היום הראשון לחסימה')
      return
    }
    setSaving(true)
    setError('')
    try {
      await itemsApi.addBlock(item.id, { from, to: to || from, note })
      setFrom('')
      setTo('')
      setNote('')
      reload()
    } catch (err) {
      setError(err.message)
    } finally {
      setSaving(false)
    }
  }

  async function removeBlock(blockId) {
    setError('')
    try {
      await itemsApi.deleteBlock(item.id, blockId)
      reload()
    } catch (err) {
      setError(err.message)
    }
  }

  const selectedEnd = to || from

  return (
    <Modal title={`לוח זמנים: ${item.name}`} onClose={onClose} wide>
      {!schedule ? (
        <p className="state-message">{error || 'טוען...'}</p>
      ) : (
        <div className="item-schedule-owner">
          <MonthCalendar
            dayClass={(day) =>
              [
                inRanges(day, rentalRanges) ? 'is-rented' : '',
                inRanges(day, blockRanges) ? 'is-blocked' : '',
                from && day >= from && day <= selectedEnd ? 'is-selected' : '',
              ].join(' ')
            }
            isDisabled={(day) => isDayBusy(day, [...rentalRanges, ...blockRanges])}
            onDayClick={pickDay}
            dayTitle={(day) => (inRanges(day, rentalRanges) ? 'מושכר' : inRanges(day, blockRanges) ? 'חסום' : undefined)}
          />
          <p className="calendar-legend">
            <span className="legend-swatch is-rented" /> מושכר
            <span className="legend-swatch is-blocked" /> חסום על ידך
          </p>

          <form className="modal-form block-form" onSubmit={addBlock}>
            <p className="field-hint">
              {from
                ? `נבחר: ${formatDate(from)}${to ? ` עד ${formatDate(to)}` : ''}`
                : 'כדי לחסום ימים שהפריט לא זמין - לחצי בלוח על היום הראשון ואז על האחרון'}
            </p>
            <label htmlFor="blockNote">הערה לעצמך (לא חובה)</label>
            <input id="blockNote" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
            {error && <p className="form-error">{error}</p>}
            <button type="submit" className="btn btn-primary btn-small" disabled={saving || !from}>
              {saving ? 'שומרת...' : 'חסימת הימים'}
            </button>
          </form>

          {rentals.length > 0 && (
            <>
              <h3>השכרות קרובות</h3>
              <ul className="schedule-list">
                {rentals.map((r) => (
                  <li key={r.loanId}>
                    {formatDateTime(r.from)} - {formatDateTime(r.to)}
                    {schedule.quantity > 1 && ` · ${r.units} ${r.units === 1 ? 'יחידה' : 'יחידות'}`}
                  </li>
                ))}
              </ul>
            </>
          )}

          {blocks.length > 0 && (
            <>
              <h3>ימים שחסמת</h3>
              <ul className="schedule-list">
                {blocks.map((b) => (
                  <li key={b.id}>
                    {formatDate(b.from)}
                    {b.to !== b.from ? ` - ${formatDate(b.to)}` : ''}
                    {b.note ? ` · ${b.note}` : ''}
                    <button type="button" className="link-button schedule-unblock" onClick={() => removeBlock(b.id)}>
                      ביטול החסימה
                    </button>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
    </Modal>
  )
}
