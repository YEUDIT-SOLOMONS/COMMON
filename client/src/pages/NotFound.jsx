import { Link } from 'react-router-dom'

export function NotFound() {
  return (
    <div className="page state-message not-found">
      <h1 className="not-found-title">נראה שהעמוד הזה יצא להשכרה ולא חזר עדיין...</h1>
      <Link to="/" className="btn btn-primary">
        חזרה לקטלוג הציוד
      </Link>
    </div>
  )
}
