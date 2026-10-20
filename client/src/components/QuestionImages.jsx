import { useState } from 'react'
import { EXPLICIT_API_BASE_URL } from '../config.js'
import { resizeImageToDataUrl } from '../utils/image.js'

const MAX_IMAGES = 3

// כתובת תמונה בהתכתבות: טעינת תמונה רגילה עם הקישור האישי, ועם bypass=netfree כמו בשאר המערכות
export function questionImageUrl(threadId, imageId, token) {
  const base = (EXPLICIT_API_BASE_URL || '/api').replace(/\/$/, '')
  return `${base}/questions/${threadId}/images/${imageId}?t=${encodeURIComponent(token || '')}&bypass=netfree`
}

// בחירת תמונות לצירוף להודעה (עד 3), עם תצוגה מקדימה
export function ImageAttach({ images, onChange, disabled }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function pick(e) {
    const files = [...(e.target.files ?? [])].slice(0, MAX_IMAGES - images.length)
    e.target.value = ''
    if (files.length === 0) return
    setBusy(true)
    setError('')
    try {
      const added = []
      for (const f of files) added.push(await resizeImageToDataUrl(f, { maxDimension: 1280, quality: 0.78 }))
      onChange([...images, ...added])
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="question-attach">
      {images.length > 0 && (
        <div className="question-attach-previews">
          {images.map((src, i) => (
            <span key={i} className="question-attach-thumb">
              <img src={src} alt="" />
              <button type="button" aria-label="הסרת התמונה" onClick={() => onChange(images.filter((_, j) => j !== i))}>
                ×
              </button>
            </span>
          ))}
        </div>
      )}
      {images.length < MAX_IMAGES && (
        <label className={`question-attach-btn${disabled || busy ? ' is-disabled' : ''}`}>
          📷 {busy ? 'מכינה תמונה...' : 'צירוף תמונות'}
          <input type="file" accept="image/*" multiple onChange={pick} disabled={disabled || busy} className="visually-hidden" />
        </label>
      )}
      <p className="question-attach-note">תמונות שמועלות כאן נשלחות אוטומטית כמצורפות למייל כך הן לא נחסמות</p>
      {error && <p className="form-error">{error}</p>}
    </div>
  )
}

// התמונות שבהודעה - לחיצה פותחת בגודל מלא
export function MessageImages({ threadId, imageIds, token }) {
  if (!imageIds?.length) return null
  return (
    <span className="question-msg-images">
      {imageIds.map((imgId) => {
        const url = questionImageUrl(threadId, imgId, token)
        return (
          <a key={imgId} href={url} target="_blank" rel="noreferrer">
            <img src={url} alt="תמונה מצורפת" loading="lazy" />
          </a>
        )
      })}
    </span>
  )
}
