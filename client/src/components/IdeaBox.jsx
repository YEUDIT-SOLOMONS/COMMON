import { useState } from 'react'
import { contactApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { Modal } from './Modal.jsx'

const HIDE_KEY = 'common_idea_hidden_until'
const HIDE_FOR_MS = 14 * 24 * 60 * 60 * 1000

// "יש לך רעיון?" באזור האישי - נשלח למייל האתר דרך טופס יצירת הקשר
export function IdeaBox() {
  const { user } = useAuth()
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)
  const [peekOpen, setPeekOpen] = useState(false)
  const [hidden, setHidden] = useState(() => {
    try {
      return Number(localStorage.getItem(HIDE_KEY) || 0) > Date.now()
    } catch {
      return false
    }
  })

  function hideForNow() {
    try {
      localStorage.setItem(HIDE_KEY, String(Date.now() + HIDE_FOR_MS))
    } catch {
      // בלי אחסון - מסתיר רק עד רענון
    }
    setHidden(true)
  }

  async function handleSubmit(e) {
    e.preventDefault()
    if (!text.trim()) {
      setError('כתבי את הרעיון שלך')
      return
    }
    setSending(true)
    setError('')
    try {
      await contactApi.send({ name: user.name, email: user.email, message: `💡 רעיון לשיפור: ${text.trim()}` })
      setSent(true)
      setText('')
    } catch (err) {
      setError(err.message)
    } finally {
      setSending(false)
    }
  }

  return (
    <>
    {!hidden && (
      <aside
        className={`idea-peek${peekOpen ? ' is-open' : ''}`}
        aria-label="הציעי רעיון לשיפור"
        onMouseEnter={() => setPeekOpen(true)}
        onMouseLeave={() => setPeekOpen(false)}
      >
        <button
          type="button"
          className="idea-peek-tab"
          aria-expanded={peekOpen}
          aria-label="יש לך רעיון לשיפור?"
          onClick={() => setPeekOpen((v) => !v)}
        >
          💡
        </button>
        <div className="idea-peek-body">
          <span className="idea-note-tape" aria-hidden="true" />
          <button type="button" className="idea-peek-close" aria-label="הסתרה לשבועיים" onClick={hideForNow}>
            ×
          </button>
          <p className="idea-note-title">יש לך רעיון לשיפור?</p>
          <p className="idea-note-text">האתר נבנה בשבילכן, ונשמח לשמוע מה יעשה אותו טוב עוד יותר.</p>
          <button
            type="button"
            className="idea-note-button"
            onClick={() => {
              setSent(false)
              setOpen(true)
              setPeekOpen(false)
            }}
          >
            הציעי רעיון ←
          </button>
        </div>
      </aside>
    )}

      {open && (
        <Modal title="הציעי רעיון" onClose={() => setOpen(false)}>
          {sent ? (
            <div className="modal-form">
              <p className="form-success">✓ תודה! הרעיון שלך הגיע אלינו ונקרא בתשומת לב.</p>
              <button type="button" className="btn btn-primary" onClick={() => setOpen(false)}>
                סגירה
              </button>
            </div>
          ) : (
            <form className="modal-form" onSubmit={handleSubmit}>
              <label htmlFor="ideaText">מה היית רוצה שיהיה באתר?</label>
              <textarea id="ideaText" rows={4} maxLength={1000} value={text} onChange={(e) => setText(e.target.value)} autoFocus />
              {error && <p className="form-error">{error}</p>}
              <button type="submit" className="btn btn-primary" disabled={sending}>
                {sending ? 'שולחת...' : 'שליחה'}
              </button>
            </form>
          )}
        </Modal>
      )}
    </>
  )
}
