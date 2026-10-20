import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { reviewsApi } from '../api/endpoints.js'
import { formatDate } from '../utils/format.js'
import { Modal } from './Modal.jsx'
import { StarPicker } from './Stars.jsx'
import { siteShareText, whatsappLink } from '../utils/share.js'

// באזור האישי: השכרות שהסתיימו ומחכות לדירוג. קישור מהמייל: /profile?review=<loanId>
export function PendingReviews() {
  const [pending, setPending] = useState([])
  const [target, setTarget] = useState(null)
  const [thanks, setThanks] = useState('')
  const [sharePrompt, setSharePrompt] = useState(false)

  // "תודה על הדירוג!" מוצג כמה שניות ונעלם בהדרגה
  useEffect(() => {
    if (!thanks) return undefined
    const timer = window.setTimeout(() => setThanks(''), 4000)
    return () => window.clearTimeout(timer)
  }, [thanks])
  const [expanded, setExpanded] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()
  const linkedLoanId = searchParams.get('review')

  const reload = useCallback(() => {
    reviewsApi
      .pending()
      .then((r) => setPending(r ?? []))
      .catch(() => {})
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  async function dismiss(p) {
    try {
      await reviewsApi.dismiss(p.loanId)
      setPending((prev) => prev.filter((x) => !(x.loanId === p.loanId && x.role === p.role)))
    } catch {
      reload()
    }
  }

  const linked = linkedLoanId ? pending.find((p) => p.loanId === linkedLoanId) : null
  const open = target ?? linked

  function close() {
    setTarget(null)
    if (linkedLoanId) setSearchParams({}, { replace: true })
  }

  if (pending.length === 0 && !thanks && !sharePrompt) return null

  return (
    <section className="profile-section pending-reviews">
      {pending.length > 0 && (
        <button
          type="button"
          className="pending-reviews-toggle"
          onClick={() => setExpanded((v) => !v)}
          aria-expanded={expanded}
          aria-controls="pendingReviewsList"
        >
          <span>
            דירוגים שמחכים לך <span className="pending-reviews-count">{pending.length}</span>
          </span>
          <span className={`pending-reviews-arrow ${expanded ? 'is-open' : ''}`} aria-hidden="true">
            ‹
          </span>
        </button>
      )}
      {thanks && (
        <p className="form-success thanks-toast" role="status">
          {thanks}
        </p>
      )}
      {sharePrompt && (
        <div className="share-prompt" role="status">
          <p>
            <strong>שמחות שנהנית! 💛</strong> מכירה צלמת שתאהב את קומון?
          </p>
          <a className="btn btn-primary btn-small" href={whatsappLink(siteShareText())} target="_blank" rel="noopener noreferrer">
            שיתוף בוואטסאפ
          </a>
          <button type="button" className="modal-close share-prompt-close" onClick={() => setSharePrompt(false)} aria-label="סגירה">
            ✕
          </button>
        </div>
      )}
      {expanded && (
        <ul className="favorites-list" id="pendingReviewsList">
          {pending.map((p) => (
            <li key={`${p.loanId}-${p.role}`} className="favorite-row">
              <span className="favorite-row-text">
                <strong>{p.role === 'Renter' ? `איך היה עם ${p.itemName}?` : `איך הייתה השוכרת של ${p.itemName}?`}</strong>
                {p.returnedAt && <span>הוחזר {formatDate(p.returnedAt)}</span>}
              </span>
              <span className="pending-review-actions">
                <button type="button" className="btn btn-primary btn-small" onClick={() => setTarget(p)}>
                  לדירוג
                </button>
                <button type="button" className="link-button pending-review-dismiss" onClick={() => dismiss(p)}>
                  הסרה בלי לדרג
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}

      {open && (
        <ReviewModal
          pending={open}
          onClose={close}
          onDone={(stars) => {
            close()
            setThanks('תודה על הדירוג!')
            if (open.role === 'Renter' && stars >= 4) setSharePrompt(true)
            reload()
          }}
        />
      )}
    </section>
  )
}

function ReviewModal({ pending, onClose, onDone }) {
  const [stars, setStars] = useState(0)
  const [text, setText] = useState('')
  const [thankYouNote, setThankYouNote] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const isRenter = pending.role === 'Renter'

  async function handleSubmit(e) {
    e.preventDefault()
    if (!stars) {
      setError('יש לבחור מספר כוכבים')
      return
    }
    setSubmitting(true)
    setError('')
    try {
      await reviewsApi.submit(pending.loanId, { stars, text, thankYouNote: isRenter ? thankYouNote : null })
      onDone(stars)
    } catch (err) {
      setError(err.message)
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title={isRenter ? `דירוג: ${pending.itemName}` : `דירוג השוכרת של ${pending.itemName}`}
      subtitle={isRenter ? undefined : 'הדירוג גלוי להנהלת האתר בלבד'}
      onClose={onClose}
    >
      <form className="modal-form" onSubmit={handleSubmit}>
        <StarPicker value={stars} onChange={setStars} />
        <label htmlFor="reviewText">{isRenter ? 'כמה מילים על ההשכרה (לא חובה)' : 'הערות על השוכרת (לא חובה)'}</label>
        <textarea id="reviewText" rows={3} maxLength={500} value={text} onChange={(e) => setText(e.target.value)} />
        {isRenter && (
          <>
            <label htmlFor="thankYouNote">מילה טובה לצלמת (לא חובה, תישלח אליה בלבד)</label>
            <textarea
              id="thankYouNote"
              rows={2}
              maxLength={300}
              value={thankYouNote}
              onChange={(e) => setThankYouNote(e.target.value)}
            />
          </>
        )}
        {error && <p className="form-error">{error}</p>}
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'שולחת...' : 'שליחת הדירוג'}
        </button>
      </form>
    </Modal>
  )
}
