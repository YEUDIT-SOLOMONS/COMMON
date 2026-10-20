import { useCallback, useEffect, useMemo, useState } from 'react'
import { categoriesApi } from '../api/endpoints.js'
import { ConfirmDialog } from '../components/ConfirmDialog.jsx'
import { Loader } from '../components/Loader.jsx'

function sortByOrder(list) {
  return [...list].sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name, 'he'))
}

// לשונית "קטגוריות" במסך הניהול: קטגוריות ראשיות ותתי-קטגוריות (למשל מצלמות > לפי חברה): הוספה, שינוי
// שם, מחיקה, ושינוי הסדר שבו הן מוצגות בקטלוג (גרירה או חצים) - בנפרד בתוך כל קבוצת אחים.
// השרת אוכף את ההרשאה (תפקיד Admin) בנפרד - ראו CategoriesController.
export function AdminCategories() {
  const [categories, setCategories] = useState([])
  const [names, setNames] = useState({}) // id -> שם בעריכה
  const [newName, setNewName] = useState('')
  const [newSubNames, setNewSubNames] = useState({}) // parentId -> שם תת-קטגוריה חדשה
  const [expanded, setExpanded] = useState({}) // parentId -> האם תתי-הקטגוריות פתוחות
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState(null)
  const [deleteTarget, setDeleteTarget] = useState(null)
  const [drag, setDrag] = useState(null) // { group, index }

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const result = await categoriesApi.getAll()
      const sorted = sortByOrder(result ?? [])
      setCategories(sorted)
      setNames(Object.fromEntries(sorted.map((c) => [c.id, c.name])))
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const tops = useMemo(() => sortByOrder(categories.filter((c) => !c.parentId)), [categories])
  const subsOf = useCallback(
    (parentId) => sortByOrder(categories.filter((c) => c.parentId === parentId)),
    [categories],
  )

  // שמירת סדר חדש לקבוצת אחים (מזהים לפי הסדר הרצוי)
  async function saveOrder(groupIds) {
    setCategories((prev) => prev.map((c) => (groupIds.includes(c.id) ? { ...c, order: groupIds.indexOf(c.id) } : c)))
    setError('')
    try {
      await categoriesApi.reorder({ ids: groupIds })
    } catch (err) {
      setError(err.message)
      await load()
    }
  }

  function move(group, index, delta) {
    const target = index + delta
    if (target < 0 || target >= group.length) return
    const ids = group.map((c) => c.id)
    ;[ids[index], ids[target]] = [ids[target], ids[index]]
    saveOrder(ids)
  }

  function handleDrop(groupKey, group, targetIndex) {
    if (!drag || drag.group !== groupKey || drag.index === targetIndex) return
    const ids = group.map((c) => c.id)
    const [moved] = ids.splice(drag.index, 1)
    ids.splice(targetIndex, 0, moved)
    setDrag(null)
    saveOrder(ids)
  }

  async function handleAdd(e, parentId) {
    e.preventDefault()
    const name = (parentId ? newSubNames[parentId] : newName) ?? ''
    if (!name.trim()) return
    setError('')
    setBusyId(parentId ?? 'new')
    try {
      await categoriesApi.create({ name: name.trim(), parentId: parentId ?? null })
      if (parentId) {
        setNewSubNames((prev) => ({ ...prev, [parentId]: '' }))
        setExpanded((prev) => ({ ...prev, [parentId]: true }))
      } else {
        setNewName('')
      }
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleRename(category) {
    const name = (names[category.id] ?? '').trim()
    if (!name || name === category.name) return
    setError('')
    setBusyId(category.id)
    try {
      await categoriesApi.update(category.id, { name })
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleConfirmDelete() {
    if (!deleteTarget) return
    setError('')
    setBusyId(deleteTarget.id)
    try {
      await categoriesApi.remove(deleteTarget.id)
      setDeleteTarget(null)
      await load()
    } catch (err) {
      setDeleteTarget(null)
      setError(err.message)
    } finally {
      setBusyId(null)
    }
  }

  function renderRow(category, group, groupKey, index, extra = null) {
    const edited = (names[category.id] ?? '').trim()
    const changed = edited && edited !== category.name
    return (
      <li
        key={category.id}
        className={`admin-categories-row ${drag?.group === groupKey && drag.index === index ? 'is-dragging' : ''}`}
        draggable
        onDragStart={(e) => {
          e.stopPropagation()
          setDrag({ group: groupKey, index })
        }}
        onDragOver={(e) => e.preventDefault()}
        onDrop={(e) => {
          e.stopPropagation()
          handleDrop(groupKey, group, index)
        }}
        onDragEnd={() => setDrag(null)}
      >
        <span className="admin-order-handle" title="גררו כדי לשנות סדר" aria-hidden="true">
          ⠿
        </span>
        <span className="admin-order-number">{index + 1}</span>
        <div className="admin-order-arrows">
          <button
            type="button"
            onClick={() => move(group, index, -1)}
            disabled={index === 0}
            aria-label={`העלאת ${category.name}`}
          >
            ▲
          </button>
          <button
            type="button"
            onClick={() => move(group, index, 1)}
            disabled={index === group.length - 1}
            aria-label={`הורדת ${category.name}`}
          >
            ▼
          </button>
        </div>
        <input
          value={names[category.id] ?? ''}
          onChange={(e) => setNames((prev) => ({ ...prev, [category.id]: e.target.value }))}
          aria-label={`שם הקטגוריה ${category.name}`}
        />
        {extra}
        <button
          type="button"
          className="btn profile-card-action"
          onClick={() => handleRename(category)}
          disabled={!changed || busyId === category.id}
        >
          שמירת שם
        </button>
        <button
          type="button"
          className="btn btn-ghost btn-small"
          onClick={() => setDeleteTarget(category)}
          disabled={busyId === category.id}
        >
          מחיקה
        </button>
      </li>
    )
  }

  return (
    <div className="admin-categories">
      <p className="admin-categories-intro">
        מוסיפים קטגוריה חדשה, משנים שם, מוחקים קטגוריה ריקה - וגוררים (או משתמשים בחצים) כדי לקבוע את
        הסדר שבו הן מוצגות בקטלוג. לכל קטגוריה אפשר להוסיף תתי-קטגוריות (למשל במצלמות: לפי חברה).
        השינויים מופיעים מיד.
      </p>

      <form className="admin-categories-add" onSubmit={(e) => handleAdd(e, null)}>
        <input
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          placeholder="שם קטגוריה חדשה"
          aria-label="שם קטגוריה חדשה"
        />
        <button type="submit" className="btn btn-primary" disabled={busyId === 'new' || !newName.trim()}>
          + הוספת קטגוריה
        </button>
      </form>

      {error && <p className="form-error">{error}</p>}
      {loading && <Loader label="טוען קטגוריות" compact />}

      <ul className="admin-categories-list">
        {tops.map((category, index) => {
          const subs = subsOf(category.id)
          const isOpen = Boolean(expanded[category.id])
          const toggle = (
            <button
              type="button"
              className={`admin-subs-toggle ${isOpen ? 'is-open' : ''}`}
              onClick={() => setExpanded((prev) => ({ ...prev, [category.id]: !prev[category.id] }))}
              aria-expanded={isOpen}
            >
              תתי-קטגוריות ({subs.length}) {isOpen ? '▴' : '▾'}
            </button>
          )

          return (
            <li key={category.id} className="admin-category-block">
              <ul className="admin-categories-list admin-categories-top">
                {renderRow(category, tops, 'top', index, toggle)}
              </ul>

              {isOpen && (
                <div className="admin-subs">
                  <ul className="admin-categories-list">
                    {subs.map((sub, subIndex) => renderRow(sub, subs, category.id, subIndex))}
                  </ul>
                  <form className="admin-categories-add admin-subs-add" onSubmit={(e) => handleAdd(e, category.id)}>
                    <input
                      value={newSubNames[category.id] ?? ''}
                      onChange={(e) => setNewSubNames((prev) => ({ ...prev, [category.id]: e.target.value }))}
                      placeholder={`תת-קטגוריה חדשה ב${category.name}`}
                      aria-label={`תת-קטגוריה חדשה ב${category.name}`}
                    />
                    <button
                      type="submit"
                      className="btn btn-primary btn-small"
                      disabled={busyId === category.id || !(newSubNames[category.id] ?? '').trim()}
                    >
                      + הוספה
                    </button>
                  </form>
                </div>
              )}
            </li>
          )
        })}
      </ul>

      {deleteTarget && (
        <ConfirmDialog
          title="מחיקת קטגוריה"
          message={`למחוק את "${deleteTarget.name}"? אפשר למחוק רק קטגוריה שאין בה פריטים ותתי-קטגוריות.`}
          confirmLabel="מחיקה"
          busy={busyId === deleteTarget.id}
          onConfirm={handleConfirmDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
