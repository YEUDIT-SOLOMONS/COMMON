import { useState } from 'react'
import { BrandMark } from '../components/BrandMark.jsx'
import { contactApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'

const emptyForm = { name: '', email: '', message: '' }

export function Contact() {
  const { user } = useAuth()

  const [form, setForm] = useState({
    name: user?.name ?? '',
    email: user?.email ?? '',
    message: '',
  })
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [sent, setSent] = useState(false)

  function update(field) {
    return (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await contactApi.send(form)
      setSent(true)
      setForm(emptyForm)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="page contact-page">
      <div className="contact-grid">
        <section className="contact-aside">
          <BrandMark className="contact-mark" />
          <p className="eyebrow">נשמח לשמוע מכם</p>
          <h1>יצירת קשר</h1>
          <p className="contact-intro">
            אם יש לכם שאלה על השכרה, הצעה לשיפור, או סתם רציתם להגיד שלום-
          </p>
          <p className="contact-intro contact-intro-2">אנחנו כאן. השאירו הודעה ונחזור אליכם בהקדם.</p>
        </section>

        <form className="contact-card" onSubmit={handleSubmit}>
          {sent ? (
            <div className="contact-success">
              <h2>ההודעה נשלחה!</h2>
              <p>תודה שפניתם אלינו - נחזור אליכם בהקדם האפשרי.</p>
              <button type="button" className="btn btn-ghost btn-small" onClick={() => setSent(false)}>
                שליחת הודעה נוספת
              </button>
            </div>
          ) : (
            <>
              <label htmlFor="contactName">שם מלא</label>
              <input id="contactName" value={form.name} onChange={update('name')} required />

              <label htmlFor="contactEmail">אימייל</label>
              <input
                id="contactEmail"
                type="email"
                value={form.email}
                onChange={update('email')}
                required
              />

              <label htmlFor="contactMessage">הודעה</label>
              <textarea
                id="contactMessage"
                rows={5}
                value={form.message}
                onChange={update('message')}
                required
              />

              {error && <p className="form-error">{error}</p>}

              <button type="submit" className="btn btn-primary" disabled={submitting}>
                {submitting ? 'שולח...' : 'שליחת הודעה'}
              </button>
            </>
          )}
        </form>
      </div>

      <section className="contact-direct" aria-labelledby="contactDirectTitle">
        <h2 id="contactDirectTitle">פרטי התקשרות</h2>
        <dl>
          <div>
            <dt>אימייל</dt>
            <dd>
              <a href="mailto:common@onyxapp.co.il">common@onyxapp.co.il</a>
            </dd>
          </div>
          <div>
            <dt>טלפון</dt>
            <dd>
              <a href="tel:0554026802" dir="ltr">
                055-4026802
              </a>
            </dd>
          </div>
          <div className="contact-emergency">
            <dt>למקרי חירום</dt>
            <dd>
              <a href="tel:0534111525" dir="ltr">
                053-4111525
              </a>
            </dd>
          </div>
        </dl>
      </section>
    </div>
  )
}
