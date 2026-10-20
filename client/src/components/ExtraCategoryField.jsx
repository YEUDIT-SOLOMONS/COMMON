import { useState } from 'react'

// קטגוריה נוספת שהפריט מתאים לה (מוצג בקטלוג גם שם): קטגוריה אחרת, או תת-קטגוריה נוספת באותה קטגוריה.
// value: מזהה הקטגוריה / תת-הקטגוריה הנוספת
export function ExtraCategoryField({ categories, value, onChange, idPrefix, mainParentId, mainSubId }) {
  const current = categories.find((c) => c.id === value)
  const sameParent = Boolean(current?.parentId && current.parentId === mainParentId)
  const [mode, setMode] = useState(!value ? '' : sameParent ? 'sub' : 'category')
  const [parentId, setParentId] = useState(sameParent ? '' : current?.parentId || value || '')
  // סטודיו/לוקיישן הוא רק להשכרת מקום - לא מוצע כקטגוריה נוספת
  const topCategories = categories.filter((c) => !c.parentId && c.id !== mainParentId && !c.name?.includes('לוקיישן'))
  const otherParentSubs = categories.filter((c) => c.parentId && c.parentId === parentId)
  const siblingSubs = categories.filter((c) => c.parentId && c.parentId === mainParentId && c.id !== mainSubId)

  function choose(next) {
    setMode(mode === next ? '' : next)
    setParentId('')
    onChange(null)
  }

  return (
    <div className="extra-category">
      <label className="checkbox-row">
        <input type="checkbox" checked={mode === 'category'} onChange={() => choose('category')} />
        <span>הפריט שלי מתאים לעוד קטגוריה</span>
      </label>
      {siblingSubs.length > 0 && (
        <label className="checkbox-row">
          <input type="checkbox" checked={mode === 'sub'} onChange={() => choose('sub')} />
          <span>הפריט שלי מתאים לעוד תת-קטגוריה</span>
        </label>
      )}

      {mode === 'category' && (
        <>
          <label htmlFor={`${idPrefix}ExtraCategory`}>קטגוריה נוספת</label>
          <select
            id={`${idPrefix}ExtraCategory`}
            value={parentId}
            onChange={(e) => {
              setParentId(e.target.value)
              onChange(e.target.value || null)
            }}
          >
            <option value="">בחרי קטגוריה</option>
            {topCategories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          {otherParentSubs.length > 0 && (
            <>
              <label htmlFor={`${idPrefix}ExtraSubCategory`}>תת-קטגוריה (אופציונלי)</label>
              <select
                id={`${idPrefix}ExtraSubCategory`}
                value={current?.parentId === parentId ? value : ''}
                onChange={(e) => onChange(e.target.value || parentId || null)}
              >
                <option value="">ללא תת-קטגוריה</option>
                {otherParentSubs.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </>
          )}
        </>
      )}

      {mode === 'sub' && (
        <>
          <label htmlFor={`${idPrefix}ExtraSibling`}>תת-קטגוריה נוספת</label>
          <select id={`${idPrefix}ExtraSibling`} value={sameParent ? value : ''} onChange={(e) => onChange(e.target.value || null)}>
            <option value="">בחרי תת-קטגוריה</option>
            {siblingSubs.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </>
      )}
    </div>
  )
}
