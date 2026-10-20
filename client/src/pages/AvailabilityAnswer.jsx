import { useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { availabilityAnswersApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'
import { formatDate } from '../utils/format.js'

const OUTCOME_TEXT = {
  canceled: 'השוכרת כבר ביטלה את הבקשה. תודה!',
  irrelevant: 'הבקשה כבר לא רלוונטית. תודה!',
  booked: 'בינתיים הפריט הוזמן לתאריכים האלה. תודה!',
}

export function AvailabilityAnswer() {
  const { requestId } = useParams()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('t') ?? ''

  const [view, setView] = useState(null)
  const [error, setError] = useState('')
  const [invalid, setInvalid] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    availabilityAnswersApi
      .get(requestId, token)
      .then(setView)
      .catch((err) => {
        setInvalid(true)
        setError(err.message)
      })
  }, [requestId, token])

  async function answer(a) {
    setBusy(true)
    setError('')
    try {
      setView(await availabilityAnswersApi.answer(requestId, token, a))
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
        <p className="state-message">{error}</p>
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

  const dates =
    view.startDate === view.returnDate
      ? formatDate(view.startDate)
      : `${formatDate(view.startDate)} עד ${formatDate(view.returnDate)}`
  const answeredText = view.answer === 'Available' ? 'הפריט פנוי' : 'הפריט תפוס'
  const justAnswered = view.outcome === 'ok'

  let content
  if (view.outcome && OUTCOME_TEXT[view.outcome]) {
    content = <p className="loan-action-done">{OUTCOME_TEXT[view.outcome]}</p>
  } else if (view.answer) {
    content = (
      <p className="loan-action-done">
        {justAnswered
          ? `✓ תודה! עדכנו את השוכרת ש${answeredText}.`
          : `✓ כבר התקבלה תשובה: ${answeredText}. תודה!`}
      </p>
    )
  } else if (view.canceled) {
    content = <p className="loan-action-done">{OUTCOME_TEXT.canceled}</p>
  } else {
    content = (
      <div className="loan-action-buttons">
        <button type="button" className="btn btn-primary" disabled={busy} onClick={() => answer(1)}>
          ✓ פנוי
        </button>
        <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => answer(2)}>
          ✗ תפוס
        </button>
      </div>
    )
  }

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      <h1>האם {view.itemName} פנוי?</h1>
      <p className="loan-action-sub">
        צלמת מהקהילה מבקשת לשכור · {dates}
      </p>
      {error && <p className="form-error">{error}</p>}
      <section className="loan-action-card">{content}</section>
    </div>
  )
}
