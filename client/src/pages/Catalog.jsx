import { useEffect, useState } from 'react'
import { itemsApi } from '../api/endpoints.js'
import { ItemCard } from '../components/ItemCard.jsx'
import { RentModal } from '../components/RentModal.jsx'

const PAGE_SIZE = 8

// קטלוג עם דפדוף אמיתי מול השרת: כל עמוד נטען בנפרד (page / pageSize)
export function Catalog() {
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState('newest')
  const [loaded, setLoaded] = useState({ key: null, result: null, error: '' })
  const [rentItem, setRentItem] = useState(null)
  const key = `${page}|${sort}`

  useEffect(() => {
    let cancelled = false
    itemsApi
      .getPage({ page, pageSize: PAGE_SIZE, sort })
      .then((data) => !cancelled && setLoaded({ key, result: data, error: '' }))
      .catch((err) => !cancelled && setLoaded({ key, result: null, error: err.message }))
    return () => {
      cancelled = true
    }
  }, [key, page, sort])

  const loading = loaded.key !== key
  const { result, error } = loaded
  const totalPages = result?.totalPages ?? 0

  return (
    <div className="page">
      <h1>קטלוג</h1>
      <label>
        מיון:{' '}
        <select value={sort} onChange={(e) => { setSort(e.target.value); setPage(1) }}>
          <option value="newest">החדשים ביותר</option>
          <option value="price">מחיר</option>
          <option value="name">שם</option>
        </select>
      </label>

      {loading && <p className="state-message">טוען…</p>}
      {!loading && error && <p className="state-message state-error">{error}</p>}

      {!loading && result && (
        <>
          <p className="state-message">{result.totalCount} פריטים · עמוד {result.page} מתוך {Math.max(totalPages, 1)}</p>
          <div className="items-grid">
            {result.items.map((item) => (
              <ItemCard key={item.id} item={item} onClick={() => setRentItem(item)} />
            ))}
          </div>
        </>
      )}

      <nav className="pagination" aria-label="דפדוף">
        <button type="button" disabled={page <= 1 || loading} onClick={() => setPage((p) => p - 1)}>הקודם</button>
        <span> {page} </span>
        <button type="button" disabled={page >= totalPages || loading} onClick={() => setPage((p) => p + 1)}>הבא</button>
      </nav>

      {rentItem && <RentModal item={rentItem} onClose={() => setRentItem(null)} />}
    </div>
  )
}
