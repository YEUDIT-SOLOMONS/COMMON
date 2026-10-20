import { useState } from 'react'
import { usersApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { isValidIsraeliPhone } from '../utils/phone.js'
import { showRegisteredNotice } from '../utils/registeredNotice.js'
import { Modal } from './Modal.jsx'
import { QuietHoursField } from './QuietHoursField.jsx'
import { quietFromUser, quietPayload, quietUserFields } from '../utils/quietHours.js'

export function PhoneUpdateModal({ onClose, onSaved }) {
  const { user, updateUser } = useAuth()
  const [phone, setPhone] = useState(user?.phone ?? '')
  const [quiet, setQuiet] = useState(() => quietFromUser(user))
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    if (!isValidIsraeliPhone(phone)) {
      setError('יש להזין מספר טלפון תקין')
      return
    }
    const phoneKey = (value) => (value ?? '').replace(/\D/g, '').slice(-9)
    const changed = phoneKey(phone) !== phoneKey(user.phone)
    const before = quietFromUser(user)
    const quietChanged = before.enabled !== quiet.enabled || before.from !== quiet.from || before.to !== quiet.to

    setSubmitting(true)
    try {
      if (changed || quietChanged) {
        const updated = await usersApi.updateProfile(user.id, { name: user.name, phone: phone.trim(), ...quietPayload(quiet) })
        updateUser({ name: updated.name, phone: updated.phone, ...quietUserFields(updated) })
        if (changed) showRegisteredNotice('המספר עודכן בהצלחה! 📞')
      }
      onSaved?.()
      onClose()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal title="עדכון מספר הטלפון" onClose={onClose}>
      <form className="modal-form" onSubmit={handleSubmit}>
        <label htmlFor="updatePhone">מספר טלפון</label>
        <input
          id="updatePhone"
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          dir="ltr"
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
          required
          autoFocus
        />
        <QuietHoursField value={quiet} onChange={setQuiet} idPrefix="updateQuiet" />
        {error && <p className="form-error">{error}</p>}
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'שומר...' : 'שמירה'}
        </button>
      </form>
    </Modal>
  )
}
