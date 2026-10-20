import { useLayoutEffect, useRef, useState } from 'react'

function readCollapsed(storageKey, defaultCollapsed) {
  try {
    const stored = localStorage.getItem(`photorental.collapsed.${storageKey}`)
    return stored === null ? defaultCollapsed : stored === '1'
  } catch {
    return defaultCollapsed
  }
}

// כותרת באזור האישי שאפשר למזער עם חץ: מסתירה את כל שאר תוכן האזור (section) שבו היא נמצאת.
// הבחירה נשמרת בדפדפן לפי storageKey; defaultCollapsed - ממוזער עד שפותחים בפעם הראשונה.
export function SectionTitle({ storageKey, className = '', defaultCollapsed = false, children }) {
  const [collapsed, setCollapsed] = useState(() => readCollapsed(storageKey, defaultCollapsed))
  const ref = useRef(null)

  useLayoutEffect(() => {
    ref.current?.closest('section')?.classList.toggle('is-collapsed', collapsed)
  }, [collapsed])

  function toggle() {
    const next = !collapsed
    setCollapsed(next)
    try {
      localStorage.setItem(`photorental.collapsed.${storageKey}`, next ? '1' : '0')
    } catch {
      // בלי שמירה - הבחירה תחזור למצב פתוח בטעינה הבאה
    }
  }

  return (
    <h2 ref={ref} className={`section-title ${className}`}>
      <button type="button" className="section-toggle" onClick={toggle} aria-expanded={!collapsed}>
        <span>{children}</span>
        <span className={`section-toggle-arrow ${collapsed ? '' : 'is-open'}`} aria-hidden="true">
          ‹
        </span>
      </button>
    </h2>
  )
}
