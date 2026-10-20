import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { formatDateTime } from '../utils/format.js'
import { MessageImages } from '../components/QuestionImages.jsx'

// כל ההתכתבויות בין שוכרות למשכירות - לפיקוח הנהלת האתר
export function AdminQuestions() {
  const [rows, setRows] = useState(null)
  const [error, setError] = useState('')
  const [openId, setOpenId] = useState(null)
  const [editing, setEditing] = useState(null) // { id, index, text }
  const [saving, setSaving] = useState(false)
  const [systemText, setSystemText] = useState({})

  async function sendSystem(id) {
    const text = (systemText[id] ?? '').trim()
    if (!text) return
    setSaving(true)
    try {
      await adminApi.adminQuestionMessage(id, text)
      setRows((prev) =>
        prev.map((q) =>
          q.id === id ? { ...q, messages: [...q.messages, { from: 'Admin', text, atUtc: new Date().toISOString(), imageIds: [] }] } : q,
        ),
      )
      setSystemText({ ...systemText, [id]: '' })
    } catch (err) {
      setError(err.message)
    } finally {
      setSaving(false)
    }
  }

  async function saveEdit(remove = false) {
    if (remove && !window.confirm('למחוק את ההודעה?')) return
    setSaving(true)
    try {
      const text = remove ? '' : editing.text
      await adminApi.editQuestionMessage(editing.id, editing.index, text)
      setRows((prev) =>
        prev.map((q) =>
          q.id !== editing.id
            ? q
            : {
                ...q,
                messages: remove
                  ? q.messages.filter((_, i) => i !== editing.index)
                  : q.messages.map((m, i) => (i === editing.index ? { ...m, text } : m)),
              },
        ),
      )
      setEditing(null)
    } catch (err) {
      setError(err.message)
    } finally {
      setSaving(false)
    }
  }

  useEffect(() => {
    adminApi
      .questions()
      .then((r) => setRows(r ?? []))
      .catch((err) => setError(err.message))
  }, [])

  if (error) return <p className="state-message state-error">{error}</p>
  if (!rows) return <Loader label="טוען התכתבויות" />
  if (rows.length === 0) return <p className="state-message">עדיין אין שאלות.</p>

  return (
    <section>
      <p className="field-hint">החדשות ראשונות. טלפונים ומיילים בתוך ההודעות מוסתרים אוטומטית.</p>
      <div className="table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>עדכון אחרון</th>
              <th>פריט</th>
              <th>שואלת</th>
              <th>משכירה</th>
              <th>הודעות</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((q) => (
              <tr key={q.id}>
                <td>{formatDateTime(q.lastMessageAtUtc)}</td>
                <td>
                  <Link to={`/?item=${encodeURIComponent(q.itemId)}`}>{q.itemName}</Link>
                </td>
                <td>
                  {q.renterName}
                  {q.renterEmail && <span className="admin-call-note" dir="ltr"> · {q.renterEmail}</span>}
                </td>
                <td>
                  {q.ownerName}
                  {q.ownerEmail && <span className="admin-call-note" dir="ltr"> · {q.ownerEmail}</span>}
                </td>
                <td>
                  <button type="button" className="btn btn-ghost btn-small" onClick={() => setOpenId(openId === q.id ? null : q.id)}>
                    {openId === q.id ? 'הסתרה' : `${q.messages.length} הודעות`}
                  </button>
                  {openId === q.id && (
                    <ul className="question-messages admin-question-messages">
                      {q.messages.map((m, i) => (
                        <li key={i} className={`question-msg ${m.from === 'Admin' ? 'is-admin' : m.from === 'Owner' ? 'is-mine' : ''}`}>
                          <span className="question-msg-who">
                            {m.from === 'Admin' ? '📢 הנהלה' : m.from === 'Owner' ? 'משכירה' : 'שואלת'} · {formatDateTime(m.atUtc)}
                          </span>
                          {editing?.id === q.id && editing.index === i ? (
                            <span className="admin-edit-msg">
                              <textarea
                                rows={3}
                                value={editing.text}
                                onChange={(e) => setEditing({ ...editing, text: e.target.value })}
                              />
                              <span className="ask-owner-actions">
                                <button type="button" className="btn btn-primary btn-small" disabled={saving || !editing.text.trim()} onClick={() => saveEdit()}>
                                  שמירה
                                </button>
                                <button type="button" className="btn btn-ghost btn-small" disabled={saving} onClick={() => saveEdit(true)}>
                                  מחיקת ההודעה
                                </button>
                                <button type="button" className="btn btn-ghost btn-small" onClick={() => setEditing(null)}>
                                  ביטול
                                </button>
                              </span>
                            </span>
                          ) : (
                            <>
                              {m.text && <p>{m.text}</p>}
                              <MessageImages threadId={q.id} imageIds={m.imageIds} token={q.ownerToken} />
                              <button type="button" className="link-button admin-edit-link" onClick={() => setEditing({ id: q.id, index: i, text: m.text })}>
                                עריכה
                              </button>
                            </>
                          )}
                        </li>
                      ))}
                    </ul>
                  )}
                  {openId === q.id && (
                    <span className="admin-edit-msg">
                      <textarea
                        rows={2}
                        placeholder="הודעת מערכת מהנהלת האתר (תישלח במייל לשתיהן)"
                        value={systemText[q.id] ?? ''}
                        onChange={(e) => setSystemText({ ...systemText, [q.id]: e.target.value })}
                      />
                      <button
                        type="button"
                        className="btn btn-primary btn-small"
                        disabled={saving || !(systemText[q.id] ?? '').trim()}
                        onClick={() => sendSystem(q.id)}
                      >
                        📢 שליחת הודעת מערכת
                      </button>
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
