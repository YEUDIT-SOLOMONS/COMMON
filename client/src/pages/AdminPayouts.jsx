import { useCallback, useEffect, useState } from 'react'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { ProofButton } from '../components/ProofButton.jsx'
import { readProofFile } from '../utils/proof.js'
import { formatDate, formatMoney } from '../utils/format.js'


export function AdminPayouts() {
  const [owners, setOwners] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')
  const [proofs, setProofs] = useState({})

  const load = useCallback(async () => {
    try {
      setOwners(await adminApi.payouts())
      setError('')
    } catch (err) {
      setError(err.message)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function markPaid(owner) {
    const due = owner.rentals.filter((r) => r.released && !r.paid)
    if (due.length === 0) return
    const total = owner.transferNow ?? due.reduce((sum, r) => sum + r.net, 0)
    const proof = proofs[owner.ownerId]
    if (!proof && !window.confirm('לא צורפה אסמכתת העברה. לסמן בכל זאת?')) return
    if (!window.confirm(`לסמן שהועברו ${formatMoney(total)} ל${owner.ownerName}?`)) return
    setBusy(owner.ownerId)
    try {
      await adminApi.markPayoutPaid(owner.ownerId, due.map((r) => r.loanId), proof)
      setProofs((prev) => ({ ...prev, [owner.ownerId]: null }))
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy('')
    }
  }

  if (error) return <p className="state-message state-error">{error}</p>
  if (!owners) return <Loader label="טוען תשלומים" />
  if (owners.length === 0) return <p className="state-message">אין עדיין השכרות ששולמו באשראי והוחזרו.</p>

  return (
    <section className="admin-payouts">
      <p className="field-hint">
        כספים מהשכרות ששולמו באשראי דרך האתר. התשלום למשכירה משתחרר מיד כשסימנה שהפריט הוחזר, בניכוי העמלה.
      </p>
      {owners.map((owner) => {
        const due = owner.rentals.filter((r) => r.released && !r.paid)
        return (
          <article className="admin-payout-card" key={owner.ownerId}>
            <header>
              <h3>{owner.ownerName}</h3>
              <span className="payment-status-note">
                {owner.ownerPhone} · {owner.ownerEmail}
              </span>
            </header>

            {owner.bankConfigured ? (
              <p className="admin-payout-bank">
                {owner.bankHolder} · {owner.bankName} · סניף {owner.bankBranch} · חשבון {owner.bankAccount}
              </p>
            ) : (
              <p className="form-error">למשכירה לא הוגדרו פרטי חשבון בנק - אי אפשר להעביר לה.</p>
            )}

            <p>
              מגיע לה עכשיו: <strong>{formatMoney(owner.dueNow)}</strong> · ממתין לשחרור: {formatMoney(owner.pending)}
            </p>
            {owner.offsets?.length > 0 && (
              <div className="payout-offsets">
                {owner.offsets.map((o) => (
                  <p key={o.month}>
                    − קיזוז עמלה חודשית ({o.month}): {formatMoney(o.amount)}
                  </p>
                ))}
                <p>
                  <strong>להעברה בפועל: {formatMoney(owner.transferNow)}</strong>
                </p>
              </div>
            )}

            <table className="admin-table">
              <thead>
                <tr>
                  <th>פריט</th>
                  <th>הוחזר</th>
                  <th>סכום</th>
                  <th>עמלה</th>
                  <th>למשכירה</th>
                  <th>מצב</th>
                </tr>
              </thead>
              <tbody>
                {owner.rentals.map((r) => (
                  <tr key={r.loanId}>
                    <td>
                      {r.itemName}
                      {r.lateFeeUncollected > 0 && (
                        <span className="late-note"> · איחור שלא נגבה: {formatMoney(r.lateFeeUncollected)}</span>
                      )}
                    </td>
                    <td>{formatDate(r.returnedAt)}</td>
                    <td>{formatMoney(r.gross)}</td>
                    <td>{formatMoney(r.commission)}</td>
                    <td>{formatMoney(r.net)}</td>
                    <td>
                      {r.paid ? `הועבר ${formatDate(r.paidAt)}` : r.released ? 'מוכן להעברה' : 'ממתין'}
                      {r.paid && r.hasProof && <ProofButton loanId={r.loanId} fetchProof={adminApi.payoutProof} />}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {due.length > 0 && (
              <label className="payout-proof-field">
                <span>אסמכתת העברה (תמונה או PDF):</span>
                <input
                  type="file"
                  accept="image/*,application/pdf"
                  onChange={async (e) => {
                    const file = e.target.files?.[0]
                    if (!file) return
                    try {
                      const proof = await readProofFile(file)
                      setProofs((prev) => ({ ...prev, [owner.ownerId]: proof }))
                    } catch (err) {
                      setError(err.message)
                    }
                  }}
                />
                {proofs[owner.ownerId] && <span className="payment-status-note">✓ {proofs[owner.ownerId].fileName}</span>}
              </label>
            )}
            {due.length > 0 && (
              <button
                type="button"
                className="btn btn-primary"
                disabled={busy === owner.ownerId || !owner.bankConfigured}
                onClick={() => markPaid(owner)}
              >
                {busy === owner.ownerId ? 'מסמן...' : `סימון כהועבר (${formatMoney(owner.transferNow ?? due.reduce((sum, r) => sum + r.net, 0))})`}
              </button>
            )}
          </article>
        )
      })}
    </section>
  )
}
