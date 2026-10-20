import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { categoriesApi, itemsApi, loansApi } from '../api/endpoints.js'
import { CameraIcon } from '../components/CameraIcon.jsx'
import { ItemStatusBadge } from '../components/StatusBadge.jsx'
import { useAuth } from '../context/AuthContext.jsx'
import { Loader } from '../components/Loader.jsx'
import { Price } from '../components/Price.jsx'

function defaultReturnDate() {
  const d = new Date()
  d.setDate(d.getDate() + 7)
  return d.toISOString().slice(0, 10)
}

export function ItemDetails() {
  const { id } = useParams()
  const { user, isAuthenticated } = useAuth()
  const navigate = useNavigate()

  const [item, setItem] = useState(null)
  const [categoryName, setCategoryName] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [returnDate, setReturnDate] = useState(defaultReturnDate)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState('')
  const [successMessage, setSuccessMessage] = useState('')

  useEffect(() => {
    let cancelled = false

    async function load() {
      setLoading(true)
      setError('')
      try {
        const fetchedItem = await itemsApi.getById(id)
        if (cancelled) return
        setItem(fetchedItem)

        if (fetchedItem.categoryId) {
          categoriesApi
            .getById(fetchedItem.categoryId)
            .then((c) => !cancelled && setCategoryName(c.name))
            .catch(() => {})
        }
      } catch (err) {
        if (!cancelled) setError(err.message)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [id])

  async function handleRent(e) {
    e.preventDefault()
    setFormError('')
    setSuccessMessage('')

    if (!returnDate) {
      setFormError('יש לבחור תאריך החזרה משוער')
      return
    }

    setSubmitting(true)
    try {
      await loansApi.create({
        itemId: item.id,
        borrowerId: user.id,
        expectedReturnDate: new Date(`${returnDate}T12:00:00`).toISOString(),
      })
      setSuccessMessage('ההשכרה נפתחה בהצלחה! עקבו אחריה בכל עת מהחשבון שלכם.')
      setItem((prev) => ({ ...prev, status: 'Rented' }))
    } catch (err) {
      setFormError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) return <div className="page"><Loader label="טוען פרטי פריט" /></div>
  if (error) return <div className="page state-message state-error">{error}</div>
  if (!item) return null

  const isOwnItem = user && item.ownerId === user.id

  return (
    <div className="page item-details">
      <Link to="/" className="back-link">
        ← חזרה לקטלוג הציוד
      </Link>

      <div className="item-details-grid">
        <div className="item-details-media">
          <CameraIcon className="item-details-icon" />
        </div>

        <div className="item-details-info">
          <div className="item-details-top">
            <h1>{item.name}</h1>
            <ItemStatusBadge status={item.status} />
          </div>

          {categoryName && <p className="item-details-category">קטגוריה: {categoryName}</p>}
          <p className="item-details-desc">{item.description}</p>

          <dl className="item-meta">
            <div>
              <dt>מספר סידורי</dt>
              <dd>{item.serialNumber}</dd>
            </div>
          </dl>

          <div className="item-price-big">
            <Price amount={item.dailyPrice} vat={item.addVat} />
          </div>

          {isOwnItem ? (
            <p className="state-message">זהו פריט שבבעלותכם.</p>
          ) : item.status !== 'Available' ? (
            <p className="state-message">הפריט אינו זמין להשכרה כרגע.</p>
          ) : isAuthenticated ? (
            <form className="rent-form" onSubmit={handleRent}>
              <label htmlFor="returnDate">תאריך החזרה משוער</label>
              <input
                id="returnDate"
                type="date"
                value={returnDate}
                onChange={(e) => setReturnDate(e.target.value)}
                min={new Date().toISOString().slice(0, 10)}
                required
              />
              {formError && <p className="form-error">{formError}</p>}
              {successMessage && <p className="form-success">{successMessage}</p>}
              <button type="submit" className="btn btn-primary" disabled={submitting}>
                {submitting ? 'שולח...' : 'השכרת הפריט'}
              </button>
            </form>
          ) : (
            <button type="button" className="btn btn-primary" onClick={() => navigate('/login')}>
              התחברו כדי לשכור
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
