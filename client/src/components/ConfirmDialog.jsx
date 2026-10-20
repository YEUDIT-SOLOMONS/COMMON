import { useId } from 'react'
import { useDialogFocus } from '../utils/useDialogFocus.js'

// חלון אישור גנרי לפעולות הרסניות (כגון מחיקה) - חוסם את הפעולה עד לאישור מפורש מהמשתמש
export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'אישור',
  cancelLabel = 'ביטול',
  danger = true,
  busy = false,
  onConfirm,
  onCancel,
}) {
  const dialogRef = useDialogFocus(onCancel)
  const titleId = useId()
  const messageId = useId()

  return (
    <div className="modal-backdrop" onClick={onCancel}>
      <div
        ref={dialogRef}
        className="modal-dialog confirm-dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-body">
          <h2 id={titleId} className="confirm-title">{title}</h2>
          <p id={messageId} className="confirm-message">{message}</p>
          <div className="confirm-actions">
            <button type="button" className="btn btn-ghost" onClick={onCancel} disabled={busy}>
              {cancelLabel}
            </button>
            <button
              type="button"
              className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`}
              onClick={onConfirm}
              disabled={busy}
            >
              {busy ? 'מוחק...' : confirmLabel}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
