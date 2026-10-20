import { useCallback, useEffect, useState } from 'react'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'

// שיוך כל רחוב שיש בו פריטים לשכונה. בחלון הפריט מוצגת השכונה, ואם לא נקבעה - הרחוב
export function AdminNeighborhoods() {
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [saved, setSaved] = useState('')

  const load = useCallback(async () => {
    try {
      setData(await adminApi.neighborhoods())
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function save(row, value) {
    const neighborhood = value.trim() || null
    if ((row.neighborhood ?? null) === neighborhood) return
    try {
      await adminApi.setNeighborhood(row.city, row.street, neighborhood)
      setData((prev) => ({
        ...prev,
        rows: prev.rows.map((r) => (r.city === row.city && r.street === row.street ? { ...r, neighborhood } : r)),
      }))
      setSaved(`${row.street}, ${row.city}`)
    } catch (err) {
      setError(err.message)
    }
  }

  if (!data) return error ? <p className="state-message state-error">{error}</p> : <Loader label="טוען רחובות" />

  const missing = data.rows.filter((r) => !r.neighborhood).length
  const options = data.options ?? {}

  return (
    <section>
      <p className="field-hint">
        בחלון הפריט מוצגת השכונה של הרחוב. רחוב בלי שכונה מוצג כרחוב. בחרי שכונה מהרשימה (או "אחר" כדי להקליד
        שם), והיא נשמרת מיד. השינוי מופיע באתר תוך פחות מדקה.
        {missing > 0 && ` ${missing} רחובות עדיין בלי שכונה.`}
      </p>
      {error && <p className="form-error">{error}</p>}
      {saved && <p className="form-success">✓ נשמר: {saved}</p>}

      <div className="table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>עיר</th>
              <th>רחוב</th>
              <th>פריטים</th>
              <th>שכונה</th>
            </tr>
          </thead>
          <tbody>
            {data.rows.map((r) => {
              return (
                <tr key={`${r.city}|${r.street}`}>
                  <td>{r.city}</td>
                  <td>{r.street}</td>
                  <td>{r.items}</td>
                  <td>
                    <NeighborhoodPicker row={r} names={options[r.city] ?? []} onSave={(v) => save(r, v)} />
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}

const OTHER = '__other__'

function NeighborhoodPicker({ row, names, onSave }) {
  const current = row.neighborhood ?? ''
  const list = current && !names.includes(current) ? [...names, current] : names
  const [typing, setTyping] = useState(false)
  const [text, setText] = useState('')

  if (typing) {
    return (
      <span className="neighborhood-other">
        <input
          className="neighborhood-input"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="שם השכונה"
          autoFocus
        />
        <button
          type="button"
          className="btn btn-primary btn-small"
          disabled={!text.trim()}
          onClick={() => {
            onSave(text)
            setTyping(false)
          }}
        >
          שמירה
        </button>
        <button type="button" className="btn btn-ghost btn-small" onClick={() => setTyping(false)}>
          ביטול
        </button>
      </span>
    )
  }

  return (
    <select
      className={`neighborhood-input${current ? '' : ' is-empty'}`}
      value={current}
      onChange={(e) => {
        if (e.target.value === OTHER) {
          setText('')
          setTyping(true)
        } else onSave(e.target.value)
      }}
    >
      <option value="">— בלי שכונה (מוצג הרחוב) —</option>
      {list.map((n) => (
        <option key={n} value={n}>
          {n}
        </option>
      ))}
      <option value={OTHER}>אחר (להקליד)...</option>
    </select>
  )
}
