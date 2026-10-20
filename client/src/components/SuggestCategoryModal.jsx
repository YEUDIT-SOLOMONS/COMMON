import { useState } from 'react'
import { contactApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { Modal } from './Modal.jsx'

// "אני רוצה להוסיף קטגוריה" - ההצעה נשלחת במייל להנהלת האתר
export function SuggestCategoryModal({ topCategories, onClose }) {
  const { user } = useAuth()
  const [kind, setKind] = useState('category')
  const [parentId, setParentId] = useState('')
  const [name, setName] = useState('')
  const [notes, setNotes] = useState('')
  const [senderName, setSenderName] = useState(user?.name ?? '')
  const [senderEmail, setSenderEmail] = useState(user?.email ?? '')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const [sent, setSent] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    if (!name.trim()) {
      setError('יש לכתוב את שם הקטגוריה')
      return
    }
    if (kind === 'sub' && !parentId) {
      setError('יש לבחור לאיזו קטגוריה שייכת ההצעה')
      return
    }
    const parentName = topCategories.find((c) => c.id === parentId)?.name
    const message = [
      'הצעה לקטגוריה חדשה באתר',
      kind === 'sub' ? `סוג: תת-קטגוריה חדשה בתוך "${parentName}"` : 'סוג: קטגוריה חדשה',
      `שם: ${name.trim()}`,
      notes.trim() ? `עוד: ${notes.trim()}` : '',
    ]
      .filter(Boolean)
      .join('\n')
    setSending(true)
    try {
      await contactApi.send({ name: senderName.trim(), email: senderEmail.trim(), message })
      setSent(true)
    } catch (err) {
      setError(err.message)
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal title="אני רוצה להוסיף קטגוריה" onClose={onClose}>
      {sent ? (
        <div className="suggest-sent">
          <p className="suggest-sent-check" aria-hidden="true">
            ✓
          </p>
          <p>
            <strong>הרעיון שלך נשלח בהצלחה!</strong>
          </p>
          <p>אם יימצא צורך, בס"ד תתווסף הקטגוריה החדשה. תודה שאת איתנו!</p>
          {senderEmail.trim() && (
            <p className="field-hint">
              נעדכן אותך אישית במייל <bdi dir="ltr">{senderEmail.trim()}</bdi> אם הקטגוריה תתווסף.
            </p>
          )}
          <button type="button" className="btn btn-primary" onClick={onClose}>
            סגירה
          </button>
        </div>
      ) : (
        <form className="modal-form" onSubmit={handleSubmit}>
          <fieldset className="delivery-choice-block">
            <legend>ההצעה שלי היא</legend>
            <div className="delivery-choice">
              <label>
                <input type="radio" name="suggestKind" checked={kind === 'category'} onChange={() => setKind('category')} />
                קטגוריה חדשה
              </label>
              <label>
                <input type="radio" name="suggestKind" checked={kind === 'sub'} onChange={() => setKind('sub')} />
                תת-קטגוריה חדשה
              </label>
            </div>
          </fieldset>

          {kind === 'sub' && (
            <>
              <label htmlFor="suggestParent">לאיזו קטגוריה שייכת ההצעה שלך?</label>
              <select id="suggestParent" value={parentId} onChange={(e) => setParentId(e.target.value)} required>
                <option value="">בחרי קטגוריה</option>
                {topCategories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </>
          )}

          <label htmlFor="suggestName">שם הקטגוריה</label>
          <input id="suggestName" value={name} onChange={(e) => setName(e.target.value)} maxLength={60} required />

          <label htmlFor="suggestNotes">עוד משהו לתאר?</label>
          <textarea id="suggestNotes" rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} />

          {!user && (
            <>
              <label htmlFor="suggestSender">השם שלך</label>
              <input id="suggestSender" value={senderName} onChange={(e) => setSenderName(e.target.value)} required />
              <label htmlFor="suggestEmail">המייל שלך</label>
              <input
                id="suggestEmail"
                type="email"
                dir="ltr"
                value={senderEmail}
                onChange={(e) => setSenderEmail(e.target.value)}
                required
              />
            </>
          )}

          {!user && <p className="field-hint">המייל ישמש רק כדי לעדכן אותך על ההצעה.</p>}

          {error && <p className="form-error">{error}</p>}
          <button type="submit" className="btn btn-primary" disabled={sending}>
            {sending ? 'שולחת...' : 'שליחה'}
          </button>
        </form>
      )}
    </Modal>
  )
}
