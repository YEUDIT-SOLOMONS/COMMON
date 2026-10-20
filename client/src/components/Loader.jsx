// מחוון טעינה של common: שלושה עיגולים (ירוק וכחול מקיפים את הוורוד שפועם במרכז) במקום הטקסט "טוען..."
// compact = גרסה קטנה לשימוש בתוך חלונות/קטעים קטנים
export function Loader({ label = 'טוען', compact = false }) {
  return (
    <div className={`loader ${compact ? 'loader-compact' : ''}`} role="status" aria-live="polite">
      <span className="loader-circles" aria-hidden="true">
        <span className="loader-orbit">
          <i className="loader-dot loader-green" />
          <i className="loader-dot loader-blue" />
        </span>
        <i className="loader-dot loader-pink" />
      </span>
      <span className="loader-label">{label.replace(/\.+$/, '')}</span>
    </div>
  )
}
