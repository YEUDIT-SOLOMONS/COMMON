import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { adminApi, categoriesApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'

// העברת פריטים לקטגוריה הנכונה
export function AdminItems() {
  const [items, setItems] = useState(null)
  const [categories, setCategories] = useState([])
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [search, setSearch] = useState('')
  const [filterCat, setFilterCat] = useState('')
  const [busyId, setBusyId] = useState(null)

  useEffect(() => {
    Promise.all([adminApi.items(), categoriesApi.getAll()])
      .then(([list, cats]) => {
        setItems(list ?? [])
        setCategories(cats ?? [])
      })
      .catch((err) => setError(err.message))
  }, [])

  const byId = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories])
  const tops = useMemo(() => categories.filter((c) => !c.parentId), [categories])
  const topOf = (id) => {
    const c = byId.get(id)
    return c?.parentId || c?.id || ''
  }

  const shown = useMemo(() => {
    if (!items) return []
    const term = search.trim().toLowerCase()
    return items.filter(
      (i) =>
        (!term || i.name.toLowerCase().includes(term) || (i.ownerName ?? '').toLowerCase().includes(term)) &&
        (!filterCat || topOf(i.categoryId) === filterCat || i.categoryId === filterCat),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items, search, filterCat, byId])

  async function move(item, categoryId) {
    if (!categoryId || categoryId === item.categoryId) return
    setBusyId(item.id)
    setError('')
    try {
      const extra = item.extraCategoryId === categoryId ? null : item.extraCategoryId
      await adminApi.setItemCategory(item.id, categoryId, extra)
      setItems((prev) => prev.map((i) => (i.id === item.id ? { ...i, categoryId, extraCategoryId: extra } : i)))
      setNotice(`✓ "${item.name}" הועבר ל${trail(categoryId)}`)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  function trail(id) {
    const c = byId.get(id)
    if (!c) return '—'
    const parent = c.parentId ? byId.get(c.parentId) : null
    return parent ? `${parent.name} › ${c.name}` : c.name
  }

  if (!items) return error ? <p className="state-message state-error">{error}</p> : <Loader label="טוען פריטים" />

  return (
    <section>
      <div className="admin-users-controls">
        <input type="search" placeholder="חיפוש לפי שם פריט או משכירה" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select value={filterCat} onChange={(e) => setFilterCat(e.target.value)}>
          <option value="">כל הקטגוריות</option>
          {tops.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <span className="payment-status-note">{shown.length} פריטים</span>
      </div>
      <p className="field-hint">בחרי קטגוריה חדשה לפריט, והוא עובר אליה מיד (גם בקטלוג).</p>
      {error && <p className="form-error">{error}</p>}
      {notice && <p className="form-success">{notice}</p>}

      <div className="table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>פריט</th>
              <th>משכירה</th>
              <th>קטגוריה נוכחית</th>
              <th>העברה ל...</th>
            </tr>
          </thead>
          <tbody>
            {shown.map((i) => (
              <tr key={i.id}>
                <td>
                  <span className="admin-item-cell">
                    {i.image && <img src={i.image} alt="" className="admin-item-thumb" loading="lazy" />}
                    <Link to={`/?item=${encodeURIComponent(i.id)}`}>{i.name}</Link>
                  </span>
                </td>
                <td>{i.ownerName || '—'}</td>
                <td>
                  {trail(i.categoryId)}
                  {i.extraCategoryId && <span className="admin-call-note"> · גם: {trail(i.extraCategoryId)}</span>}
                </td>
                <td>
                  <select
                    className="neighborhood-input"
                    value={i.categoryId ?? ''}
                    disabled={busyId === i.id}
                    onChange={(e) => move(i, e.target.value)}
                  >
                    {!byId.has(i.categoryId) && <option value="">בחרי קטגוריה</option>}
                    {tops.map((t) => [
                      <option key={t.id} value={t.id}>
                        {t.name}
                      </option>,
                      ...categories
                        .filter((c) => c.parentId === t.id)
                        .map((c) => (
                          <option key={c.id} value={c.id}>
                            {'\u00a0\u00a0'}› {c.name}
                          </option>
                        )),
                    ])}
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
