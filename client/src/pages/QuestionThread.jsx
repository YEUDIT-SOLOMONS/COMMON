import { useCallback, useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { questionsApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { formatDateTime } from '../utils/format.js'
import { ImageAttach, MessageImages } from '../components/QuestionImages.jsx'
import { ContactHint } from '../components/ContactHint.jsx'

// מרחב ההתכתבות האישי על פריט (קישור מהמייל)
export function QuestionThread() {
  const { id } = useParams()
  const [params] = useSearchParams()
  const token = params.get('t') || ''
  const [thread, setThread] = useState(null)
  const [error, setError] = useState('')
  const [text, setText] = useState('')
  const [images, setImages] = useState([])
  const [sending, setSending] = useState(false)

  const load = useCallback(async () => {
    try {
      setThread(await questionsApi.get(id, token))
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }, [id, token])

  useEffect(() => {
    load()
  }, [load])

  async function send(e) {
    e.preventDefault()
    if (!text.trim() && images.length === 0) return
    setSending(true)
    try {
      await questionsApi.reply(id, token, text.trim(), images)
      setText('')
      setImages([])
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setSending(false)
    }
  }

  if (!thread) return error ? <p className="state-message state-error">{error}</p> : <Loader label="טוען התכתבות" />

  const mine = thread.role
  return (
    <div className="page question-thread">
      <BrandMark className="loan-action-mark" />
      <h1>{mine === 'Owner' ? 'שאלה על פריט שלך' : 'השאלה שלך למשכירה'}</h1>

      {thread.item && (
        <Link to={`/?item=${encodeURIComponent(thread.item.id)}`} className="question-item">
          {thread.item.image && <img src={thread.item.image} alt="" />}
          <span>
            <strong>{thread.item.name}</strong>
            {thread.item.city && <span className="admin-call-note"> · {thread.item.city}</span>}
            <span className="question-item-open">לצפייה בפריט ›</span>
          </span>
        </Link>
      )}

      <ul className="question-messages">
        {thread.messages.map((m, i) => (
          <li key={i} className={`question-msg ${m.from === 'Admin' ? 'is-admin' : m.from === mine ? 'is-mine' : ''}`}>
            <span className="question-msg-who">
              {m.from === 'Admin' ? '📢 הודעה מהנהלת common' : m.from === mine ? 'את' : m.from === 'Owner' ? 'המשכירה' : 'הצלמת השואלת'} ·{' '}
              {formatDateTime(m.atUtc)}
            </span>
            {m.text && <p>{m.text}</p>}
            <MessageImages threadId={thread.id} imageIds={m.imageIds} token={thread.viewToken} />
          </li>
        ))}
      </ul>

      <form className="question-reply" onSubmit={send}>
        <label htmlFor="questionReply">{mine === 'Owner' ? 'התשובה שלך' : 'הודעה נוספת'}</label>
        <textarea id="questionReply" rows={3} maxLength={1000} value={text} onChange={(e) => setText(e.target.value)} />
        <ContactHint text={text} />
        <ImageAttach images={images} onChange={setImages} disabled={sending} />
        {error && <p className="form-error">{error}</p>}
        <button type="submit" className="btn btn-primary" disabled={sending || (!text.trim() && images.length === 0)}>
          {sending ? 'שולחת...' : 'שליחה'}
        </button>
      </form>
    </div>
  )
}
