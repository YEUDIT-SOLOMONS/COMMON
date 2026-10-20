import { useId } from 'react'
import { useDialogFocus } from '../utils/useDialogFocus.js'

export function Modal({ title, eyebrow, subtitle, wide = false, className = '', onClose, children }) {
  const dialogRef = useDialogFocus(onClose)
  const titleId = useId()

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div
        ref={dialogRef}
        className={`modal-dialog ${wide ? 'modal-dialog-wide' : ''} ${className}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <div className="modal-heading">
            {eyebrow && <p className="modal-eyebrow">{eyebrow}</p>}
            <h2 id={titleId}>{title}</h2>
            {subtitle && <p className="modal-subtitle">{subtitle}</p>}
          </div>
          <button type="button" className="modal-close" onClick={onClose} aria-label="סגירה">
            ✕
          </button>
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  )
}
