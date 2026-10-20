import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { commissionProofApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { formatMoney } from '../utils/format.js'
import { readProofFile } from '../utils/proof.js'

// חלון "העלאת אסמכתא" - נפתח מהקישור "כאן" במייל דוח העמלה החודשי (בלי התחברות, מוגן בטוקן סודי בכתובת).
// המשכירה מעלה אסמכתת העברה (תמונה, PDF או אקסל), והיא נשלחת לתיבת האתר ונשמרת לצפייה במסך הניהול.
export function CommissionProof() {
  const [searchParams] = useSearchParams()
  const ownerId = searchParams.get('o') ?? ''
  const month = searchParams.get('m') ?? ''
  const token = searchParams.get('t') ?? ''

  const [view, setView] = useState(null)
  const [loadError, setInvalid] = useState('')
  const linkMissing = !ownerId || !month || !token
  const invalid = linkMissing ? 'הקישור חסר או שגוי' : loadError
  const [file, setFile] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  useEffect(() => {
    if (linkMissing) return undefined
    let cancelled = false
    commissionProofApi
      .get(ownerId, month, token)
      .then((result) => {
        if (!cancelled) setView(result)
      })
      .catch((err) => {
        if (!cancelled) setInvalid(err.message || 'הקישור אינו תקף')
      })
    return () => {
      cancelled = true
    }
  }, [ownerId, month, token, linkMissing])

  async function handleFileChange(e) {
    const chosen = e.target.files?.[0]
    setError('')
    setFile(null)
    if (!chosen) return
    try {
      setFile(await readProofFile(chosen))
    } catch (err) {
      setError(err.message)
    }
  }

  async function handleSubmit() {
    if (!file) return
    setBusy(true)
    setError('')
    try {
      await commissionProofApi.submit(ownerId, month, token, { dataUrl: file.dataUrl, fileName: file.fileName })
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
        <p className="state-message">{invalid}. אפשר להשיב למייל הדוח ולצרף את האסמכתא שם.</p>
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
      <h1>העלאת אסמכתת העברה</h1>
      <p className="loan-action-sub">
        דוח עמלה לחודש {view.monthLabel} · {formatMoney(view.amount)}
      </p>

      {error && <p className="form-error">{error}</p>}

      <section className="loan-action-card">
        {done ? (
          <p className="loan-action-done">✓ האסמכתא התקבלה, תודה!</p>
        ) : view.paid ? (
          <p className="loan-action-done">✓ התשלום לדוח הזה כבר סומן כשולם</p>
        ) : (
          <>
            {view.alreadyUploaded && <p className="field-hint">כבר הועלתה אסמכתא לדוח הזה. אפשר להעלות אחרת במקומה.</p>}
            <h2>בחרי קובץ אסמכתא (תמונה, PDF או אקסל)</h2>
            <input
              type="file"
              accept="image/*,application/pdf,.xlsx,.xls,.csv"
              onChange={handleFileChange}
              aria-label="קובץ אסמכתא"
            />
            {file && <p className="field-hint">נבחר: {file.fileName}</p>}
            <button type="button" className="btn btn-primary" disabled={!file || busy} onClick={handleSubmit}>
              {busy ? 'שולחת...' : 'שליחת האסמכתא'}
            </button>
          </>
        )}
      </section>
    </div>
  )
}
