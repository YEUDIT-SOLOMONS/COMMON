import { useState } from 'react'
import { Link } from 'react-router-dom'
import { questionsApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { ContactHint } from './ContactHint.jsx'

// "יש לי שאלה למשכירה..." - קישור צנוע בחלון הפריט, נפתח לתיבת שאלה
export function AskOwner({ item }) {
  const who = item.forSale ? 'למוכרת' : 'למשכירה'
  const { isAuthenticated } = useAuth()
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const [sent, setSent] = useState(null)

  async function send(e) {
    e.preventDefault()
    if (!text.trim()) return
    setSending(true)
    setError('')
    try {
      setSent(await questionsApi.ask(item.id, text.trim()))
      setText('')
    } catch (err) {
      setError(err.message)
    } finally {
      setSending(false)
    }
  }

  if (!open)
    return (
      <button type="button" className="ask-owner-link" onClick={() => setOpen(true)}>
        💬 יש לי שאלה {who}...
      </button>
    )

  if (!isAuthenticated)
    return (
      <p className="ask-owner-box field-hint">
        כדי לשאול את {item.forSale ? 'המוכרת' : 'המשכירה'} צריך <Link to="/login">להתחבר</Link>.
      </p>
    )

  if (sent)
    return (
      <p className="ask-owner-box form-success">
        ✓ השאלה נשלחה {who}. נעדכן אותך במייל כשתגיע תשובה.{' '}
        <Link to={`/questions/${sent.id}?t=${sent.token}`}>להתכתבות</Link>
      </p>
    )

  return (
    <form className="ask-owner-box" onSubmit={send}>
      <label htmlFor="askOwnerText">השאלה שלך {who}</label>
      <textarea
        id="askOwnerText"
        rows={3}
        maxLength={1000}
        value={text}
        onChange={(e) => setText(e.target.value)}
        autoFocus
      />
      <ContactHint text={text} />
      {error && <p className="form-error">{error}</p>}
      <div className="ask-owner-actions">
        <button type="submit" className="btn btn-primary btn-small" disabled={sending || !text.trim()}>
          {sending ? 'שולחת...' : 'שליחה'}
        </button>
        <button type="button" className="btn btn-ghost btn-small" onClick={() => setOpen(false)}>
          ביטול
        </button>
      </div>
    </form>
  )
}
