import { useEffect, useRef } from 'react'

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

// חלונות פתוחים (האחרון למעלה) - רק החלון העליון מגיב למקלדת, כדי שחלון אישור מעל חלון אחר לא יתנגש בו
const openDialogs = []

// נגישות לחלונות קופצים: מעביר את הפוקוס לחלון בפתיחה, שומר את המקלדת (Tab) בתוכו,
// סוגר ב-Escape ומחזיר את הפוקוס לרכיב שפתח אותו כשהחלון נסגר.
export function useDialogFocus(onClose) {
  const dialogRef = useRef(null)
  const onCloseRef = useRef(onClose)

  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    const opener = document.activeElement
    const dialog = dialogRef.current
    openDialogs.push(dialog)
    const first = dialog?.querySelector(FOCUSABLE)
    if (first) first.focus()
    else dialog?.focus()

    function handleKeyDown(e) {
      if (openDialogs[openDialogs.length - 1] !== dialog) return
      if (e.key === 'Escape') {
        onCloseRef.current?.()
        return
      }
      if (e.key !== 'Tab' || !dialog) return

      const items = [...dialog.querySelectorAll(FOCUSABLE)].filter((el) => el.offsetParent !== null)
      if (items.length === 0) {
        e.preventDefault()
        return
      }
      const firstItem = items[0]
      const lastItem = items[items.length - 1]
      if (e.shiftKey && (document.activeElement === firstItem || !dialog.contains(document.activeElement))) {
        e.preventDefault()
        lastItem.focus()
      } else if (!e.shiftKey && (document.activeElement === lastItem || !dialog.contains(document.activeElement))) {
        e.preventDefault()
        firstItem.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      openDialogs.splice(openDialogs.indexOf(dialog), 1)
      if (opener instanceof HTMLElement && document.contains(opener)) opener.focus()
    }
  }, [])

  return dialogRef
}
