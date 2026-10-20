import { useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { itemActionsApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'

// חלון "זמין / להסרה מהאתר" - נפתח מהקישור במייל "הפריט לא הושכר ב-6 השבועות האחרונים" (בלי התחברות, מוגן בטוקן
// סודי בכתובת). a=available: מאשרים שהפריט עדיין זמין. a=remove: מסירים את הפריט מהאתר. כל פעולה דורשת לחיצה על
// כפתור (ולא מתבצעת בעצם פתיחת הקישור), כדי שסורקי קישורים אוטומטיים במייל לא יסירו פריט בטעות.
export function ItemAction() {
  const { itemId } = useParams()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('t') ?? ''
  const action = searchParams.get('a') === 'remove' ? 'remove' : 'available'

  const [view, setView] = useState(null)
  const [invalid, setInvalid] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)

  useEffect(() => {
    let cancelled = false
    itemActionsApi
      .get(itemId, token)
      .then((result) => {
        if (!cancelled) setView(result)
      })
      .catch((err) => {
        if (!cancelled) setInvalid(err.message || 'הקישור אינו תקף')
      })
    return () => {
      cancelled = true
    }
  }, [itemId, token])

  async function confirm() {
    setBusy(true)
    setError('')
    try {
      await (action === 'remove' ? itemActionsApi.remove(itemId, token) : itemActionsApi.available(itemId, token))
      setDone(true)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  if (invalid) {
    return (
      <div className="page loan-action">
        <h1>הקישור אינו תקף</h1>
        <p className="state-message">{invalid}. אפשר להיכנס לאזור האישי כדי לנהל את הציוד.</p>
      </div>
    )
  }

  if (!view) {
    return (
      <div className="page loan-action">
        <Loader label="טוען" />
      </div>
    )
  }

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>{action === 'remove' ? 'הסרה מהאתר' : 'הפריט עדיין זמין?'}</h1>
      <p className="loan-action-sub">{view.itemName}</p>

      {error && <p className="form-error">{error}</p>}

      <section className="loan-action-card">
        {done ? (
          <p className="loan-action-done">
            {action === 'remove' ? '✓ הפריט הוסר מהאתר' : '✓ תודה! הפריט נשאר זמין להשכרה'}
          </p>
        ) : action === 'remove' ? (
          view.hasActiveLoan ? (
            <p className="loan-action-cash">הפריט מושכר כרגע - אפשר להסיר אותו אחרי שיוחזר.</p>
          ) : (
            <>
              <h2>להסיר את "{view.itemName}" מהאתר?</h2>
              <p className="field-hint">הפריט יוסר מהקטלוג. היסטוריית ההשכרות שלו תישמר.</p>
              <button type="button" className="btn btn-primary" disabled={busy} onClick={confirm}>
                {busy ? 'מסיר...' : 'כן, להסיר מהאתר'}
              </button>
            </>
          )
        ) : (
          <>
            <h2>הפריט תקין וזמין להשכרה?</h2>
            <button type="button" className="btn btn-primary" disabled={busy} onClick={confirm}>
              {busy ? 'שומר...' : 'כן, הפריט זמין'}
            </button>
          </>
        )}
      </section>
    </div>
  )
}
