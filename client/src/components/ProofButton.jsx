import { useState } from 'react'
import { loansApi } from '../api/endpoints.js'
import { openDataUrl } from '../utils/proof.js'

// כפתור "אסמכתא": מוריד מהשרת את אסמכתת ההעברה הבנקאית של השכרה ופותח אותה בלשונית חדשה
export function ProofButton({ loanId, fetchProof, label = 'אסמכתא' }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function handleClick() {
    setBusy(true)
    setError('')
    try {
      const proof = await (fetchProof ?? loansApi.getPaymentProof)(loanId)
      openDataUrl(proof.dataUrl, proof.fileName)
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <button type="button" className="btn btn-ghost btn-small" onClick={handleClick} disabled={busy}>
        {busy ? 'טוען...' : label}
      </button>
      {error && <span className="payment-status-note">{error}</span>}
    </>
  )
}
