import { useEffect, useMemo, useState } from 'react'
import { adminApi } from '../api/endpoints.js'
import { Loader } from '../components/Loader.jsx'
import { formatDateTime } from '../utils/format.js'

const SORTS = {
  lastLogin: { label: 'התחברות אחרונה', get: (u) => u.lastLoginUtc ?? '' },
  created: { label: 'תאריך הצטרפות', get: (u) => u.createdAtUtc ?? '' },
  items: { label: 'מספר פריטים', get: (u) => u.itemsCount },
  earned: { label: 'הכנסות', get: (u) => u.totalEarned },
  spent: { label: 'הוצאות', get: (u) => u.totalSpent },
  rentals: { label: 'מספר השכרות', get: (u) => u.rentalsAsRenter + u.rentalsOnTheirItems },
  name: { label: 'שם', get: (u) => u.name },
}



function formatMoney(value) {
  return `₪${Number(value ?? 0).toLocaleString('he-IL', { maximumFractionDigits: 0 })}`
}

function csvCell(value) {
  return `"${String(value ?? '').replaceAll('"', '""')}"`
}

// לשונית "משתמשים ונתונים": מספרים כלליים על האתר + טבלת משתמשים עם מעקב (הצטרפות, התחברות אחרונה,
// פריטים, השכרות, הכנסות והוצאות), חיפוש, מיון וייצוא לאקסל (CSV).
const israelDay = (value) => new Date(value).toLocaleDateString('en-CA', { timeZone: 'Asia/Jerusalem' })
const isToday = (value) => Boolean(value) && israelDay(value) === israelDay(Date.now())

export function AdminUsers() {
  const [overview, setOverview] = useState(null)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [sortKey, setSortKey] = useState('lastLogin')

  useEffect(() => {
    let cancelled = false
    adminApi
      .overview()
      .then((result) => {
        if (!cancelled) setOverview(result)
      })
      .catch((err) => {
        if (!cancelled) setError(err.message)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const users = useMemo(() => {
    if (!overview) return []
    const term = search.trim().toLowerCase()
    const sort = SORTS[sortKey]
    const filtered = overview.users.filter(
      (u) =>
        !term ||
        [u.name, u.email, u.phone, u.city].some((field) => (field ?? '').toLowerCase().includes(term)),
    )
    return filtered.sort((a, b) => {
      const x = sort.get(a)
      const y = sort.get(b)
      if (typeof x === 'string') return sortKey === 'name' ? x.localeCompare(y, 'he') : y.localeCompare(x)
      return y - x
    })
  }, [overview, search, sortKey])

  function exportCsv() {
    const header = ['שם', 'אימייל', 'טלפון', 'עיר', 'הצטרפות', 'התחברות אחרונה', 'פריטים', 'השכרות כשוכרת', 'השכרות על הציוד', 'הוצאות', 'הכנסות']
    const rows = users.map((u) => [
      u.name,
      u.email,
      u.phone,
      u.city,
      formatDateTime(u.createdAtUtc),
      formatDateTime(u.lastLoginUtc),
      u.itemsCount,
      u.rentalsAsRenter,
      u.rentalsOnTheirItems,
      u.totalSpent,
      u.totalEarned,
    ])
    const csv = [header, ...rows].map((row) => row.map(csvCell).join(',')).join('\r\n')
    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' })
    const link = document.createElement('a')
    link.href = URL.createObjectURL(blob)
    link.download = 'common-users.csv'
    link.click()
    URL.revokeObjectURL(link.href)
  }

  if (error) return <p className="form-error">{error}</p>
  if (!overview) return <Loader label="טוען נתונים" compact />

  const stats = [
    ['משתמשים', overview.totalUsers],
    ['הצטרפו ב-7 ימים אחרונים', overview.newUsersLast7Days],
    ['התחברו ב-7 ימים אחרונים', overview.activeUsersLast7Days],
    ['פריטים בקטלוג', overview.totalItems],
    ['פריטים זמינים', overview.availableItems],
    ['השכרות סה"כ', overview.totalLoans],
    ['השכרות פעילות', overview.activeLoans],
    ['הכנסות (שולם)', formatMoney(overview.totalRevenue)],
  ]

  return (
    <div className="admin-users">
      <ul className="admin-stats">
        {stats.map(([label, value]) => (
          <li key={label} className="admin-stat">
            <strong>{value}</strong>
            <span>{label}</span>
          </li>
        ))}
      </ul>

      <div className="admin-users-controls">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="חיפוש לפי שם, אימייל, טלפון או עיר"
          aria-label="חיפוש משתמשים"
        />
        <select value={sortKey} onChange={(e) => setSortKey(e.target.value)} aria-label="מיון">
          {Object.entries(SORTS).map(([key, { label }]) => (
            <option key={key} value={key}>
              מיון: {label}
            </option>
          ))}
        </select>
        <button type="button" className="btn btn-ghost btn-small" onClick={exportCsv}>
          ייצוא ל-Excel
        </button>
      </div>

      <p className="admin-users-count">{users.length} משתמשים</p>

      <div className="table-wrap admin-table-wrap">
        <table>
          <thead>
            <tr>
              <th>שם</th>
              <th>אימייל</th>
              <th>טלפון</th>
              <th>עיר</th>
              <th>הצטרפות</th>
              <th>התחברות אחרונה</th>
              <th>פריטים</th>
              <th>שכרה</th>
              <th>הושכר ממנה</th>
              <th>הוצאות</th>
              <th>הכנסות</th>
            </tr>
          </thead>
          <tbody>
            {users.map((u) => (
              <tr key={u.id}>
                <td>
                  {u.name}
                  {u.role === 'Admin' && <span className="admin-badge">מנהלת</span>}
                  {isToday(u.createdAtUtc) && <span className="admin-badge new-today-badge">חדשה היום</span>}
                </td>
                <td dir="ltr" className="admin-ltr">
                  {u.email}
                </td>
                <td dir="ltr" className="admin-ltr">
                  {u.phone || '—'}
                </td>
                <td>{u.city || '—'}</td>
                <td>{formatDateTime(u.createdAtUtc)}</td>
                <td>{formatDateTime(u.lastLoginUtc)}</td>
                <td>{u.itemsCount}</td>
                <td>{u.rentalsAsRenter}</td>
                <td>{u.rentalsOnTheirItems}</td>
                <td>{formatMoney(u.totalSpent)}</td>
                <td>{formatMoney(u.totalEarned)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
