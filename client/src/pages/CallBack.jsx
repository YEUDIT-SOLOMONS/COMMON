import { useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { callsApi } from '../api/endpoints.js'
import { BrandMark } from '../components/BrandMark.jsx'
import { Loader } from '../components/Loader.jsx'

export function CallBack() {
  const { logId } = useParams()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('t') ?? ''
  const [state, setState] = useState('calling')
  const [error, setError] = useState('')
  const started = useRef(false)

  function call() {
    setState('calling')
    setError('')
    callsApi
      .callbackLink(logId, token)
      .then(() => setState('done'))
      .catch((err) => {
        setError(err.message)
        setState('error')
      })
  }

  useEffect(() => {
    if (started.current) return
    started.current = true
    call()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="page loan-action">
      <BrandMark className="loan-action-mark" />
      {state === 'calling' && <Loader label="מחייגים אלייך" />}
      {state === 'done' && (
        <>
          <h1>השיחה בדרך אלייך</h1>
          <section className="loan-action-card">
            <p className="loan-action-done">✓ עוד רגע הטלפון שלך יצלצל מ-common. כשתעני, נחבר אותך אליה.</p>
          </section>
        </>
      )}
      {state === 'error' && (
        <>
          <h1>לא הצלחנו לחייג</h1>
          <p className="form-error">{error}</p>
          <button type="button" className="btn btn-primary" onClick={call}>
            לנסות שוב
          </button>
        </>
      )}
      <p className="field-hint">
        <Link to="/profile">לאזור האישי</Link>
      </p>
    </div>
  )
}
