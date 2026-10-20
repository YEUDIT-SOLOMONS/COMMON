import { useState } from 'react'
import { resizeImageToDataUrl } from '../utils/image.js'

export const MAX_ITEM_IMAGES = 6
// מגבלת הגודל הכוללת של כל התמונות יחד - השרת שומר אותן במסמך אחד ב-Firestore (מגבלה: 1MB)
const MAX_TOTAL_CHARS = 850_000

// בחירת כמה תמונות לפריט: הראשונה היא התמונה הראשית (מוצגת בקטלוג), ואפשר להסיר תמונה או להפוך
// תמונה אחרת לראשית. כל תמונה מכווצת בדפדפן לפני השליחה (ראו resizeImageToDataUrl).
export function ImagePicker({ id, images, onChange, variant = 'plain' }) {
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [dragOver, setDragOver] = useState(false)

  async function handleFiles(e) {
    const files = Array.from(e.target.files ?? [])
    e.target.value = ''
    await addFiles(files)
  }

  async function addFiles(files) {
    if (files.length === 0) return
    setError('')

    const room = MAX_ITEM_IMAGES - images.length
    if (room <= 0) {
      setError(`ניתן להעלות עד ${MAX_ITEM_IMAGES} תמונות לפריט`)
      return
    }

    setBusy(true)
    try {
      const added = []
      for (const file of files.slice(0, room)) {
        added.push(await resizeImageToDataUrl(file, { maxDimension: 720, quality: 0.66 }))
      }
      const next = [...images, ...added]
      if (next.reduce((sum, image) => sum + image.length, 0) > MAX_TOTAL_CHARS) {
        setError('התמונות גדולות מדי יחד - נא להסיר תמונה או להעלות תמונות קטנות יותר')
      } else {
        onChange(next)
        if (files.length > room) setError(`ניתן להעלות עד ${MAX_ITEM_IMAGES} תמונות - הוספנו רק את הראשונות`)
      }
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  function remove(index) {
    setError('')
    onChange(images.filter((_, i) => i !== index))
  }

  function makeMain(index) {
    onChange([images[index], ...images.filter((_, i) => i !== index)])
  }

  return (
    <div className="image-picker">
      {variant === 'drop' ? (
        <label
          htmlFor={id}
          className={`image-drop${dragOver ? ' is-over' : ''}${busy ? ' is-busy' : ''}`}
          onDragOver={(e) => {
            e.preventDefault()
            setDragOver(true)
          }}
          onDragLeave={() => setDragOver(false)}
          onDrop={(e) => {
            e.preventDefault()
            setDragOver(false)
            addFiles(Array.from(e.dataTransfer.files ?? []).filter((f) => f.type.startsWith('image/')))
          }}
        >
          <span className="image-drop-icon" aria-hidden="true">
            📷
          </span>
          <strong>{images.length ? 'הוספת עוד תמונות' : 'הוסיפי תמונות של הפריט'}</strong>
          <span className="image-drop-hint">לחצי לבחירה או גררי לכאן · עד {MAX_ITEM_IMAGES} תמונות</span>
          <input id={id} type="file" accept="image/*" multiple onChange={handleFiles} disabled={busy} className="visually-hidden" />
        </label>
      ) : (
        <input id={id} type="file" accept="image/*" multiple onChange={handleFiles} disabled={busy} />
      )}
      <p className="field-hint">
        {variant === 'drop'
          ? 'התמונה הראשונה היא התמונה הראשית שמוצגת בקטלוג. תמונות ברורות ומוארות מושכות יותר שוכרות.'
          : `אפשר להעלות כמה תמונות (עד ${MAX_ITEM_IMAGES}). הראשונה היא התמונה הראשית שמוצגת בקטלוג.`}
      </p>
      {busy && <p className="field-hint">מעבד תמונות...</p>}
      {error && <p className="form-error">{error}</p>}

      {images.length > 0 && (
        <ul className="image-picker-grid">
          {images.map((image, index) => (
            <li key={index} className="image-picker-item">
              <img src={image} alt={`תמונה ${index + 1}`} />
              {index === 0 ? (
                <span className="image-picker-main">ראשית</span>
              ) : (
                <button type="button" className="image-picker-make-main" onClick={() => makeMain(index)}>
                  הפוך לראשית
                </button>
              )}
              <button
                type="button"
                className="image-picker-remove"
                onClick={() => remove(index)}
                aria-label={`הסרת תמונה ${index + 1}`}
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
