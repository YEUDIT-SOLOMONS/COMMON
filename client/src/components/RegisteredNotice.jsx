import { useEffect, useState } from 'react'
import { SITE_CALLER_NUMBER } from '../config.js'
import { subscribeRegisteredNotice } from '../utils/registeredNotice.js'
import { Modal } from './Modal.jsx'

export function RegisteredNotice() {
  const [title, setTitle] = useState(null)

  useEffect(() => subscribeRegisteredNotice(setTitle), [])

  if (!title) return null

  const close = () => setTitle(null)
  return (
    <Modal title={title} onClose={close}>
      <div className="modal-form">
        <p>
          בעוד רגע תקבלי שיחה <span className="nowrap">מ-<bdi>{SITE_CALLER_NUMBER}</bdi></span>. שמרי את המספר באנשי הקשר שלך.
        </p>
        <p>
          <strong>ברוכה הבאה ל-COMMON!</strong>
        </p>
        <button type="button" className="btn btn-primary" onClick={close} autoFocus>
          הבנתי
        </button>
      </div>
    </Modal>
  )
}
