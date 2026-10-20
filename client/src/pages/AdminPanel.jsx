import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../context/AuthContext.jsx'
import { AdminCategories } from './AdminCategories.jsx'
import { AdminCalls } from './AdminCalls.jsx'
import { AdminPayouts } from './AdminPayouts.jsx'
import { AdminVisits } from './AdminVisits.jsx'
import { AdminFavorites } from './AdminFavorites.jsx'
import { AdminLoans } from './AdminLoans.jsx'
import { AdminNeighborhoods } from './AdminNeighborhoods.jsx'
import { AdminItems } from './AdminItems.jsx'
import { AdminCommissions } from './AdminCommissions.jsx'
import { AdminUsers } from './AdminUsers.jsx'
import { AdminReviews } from './AdminReviews.jsx'
import { AdminQuestions } from './AdminQuestions.jsx'
import { AdminSales } from './AdminSales.jsx'

const TABS = [
  { id: 'visits', label: 'גולשים' },
  { id: 'categories', label: 'קטגוריות וסדר הצגה' },
  { id: 'users', label: 'משתמשים ונתונים' },
  { id: 'loans', label: 'השכרות' },
  { id: 'commissions', label: 'עמלות' },
  { id: 'calls', label: 'שיחות טלפון' },
  { id: 'payouts', label: 'תשלומים למשכירות' },
  { id: 'reviews', label: 'דירוגים' },
  { id: 'favorites', label: 'פריטים אהובים' },
  { id: 'neighborhoods', label: 'שכונות' },
  { id: 'items', label: 'פריטים וקטגוריות' },
  { id: 'questions', label: 'שאלות למשכירות' },
  { id: 'sales', label: 'מכירות יד 2' },
]

// מסך ניהול האתר (למנהלות בלבד - תפקיד Admin; השרת אוכף את ההרשאה בנפרד בכל נקודת קצה)
export function AdminPanel() {
  const { user } = useAuth()
  const [tab, setTab] = useState('visits')

  if (user?.role !== 'Admin') {
    return (
      <div className="page">
        <h1>ניהול האתר</h1>
        <p className="state-message">המסך הזה זמין למנהלות האתר בלבד.</p>
        <Link to="/" className="btn btn-primary">
          חזרה לקטלוג
        </Link>
      </div>
    )
  }

  return (
    <div className="page admin-panel">
      <h1>ניהול האתר</h1>

      <div className="admin-tabs" role="tablist">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            className={`admin-tab ${tab === t.id ? 'admin-tab-active' : ''}`}
            onClick={() => setTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'visits' && <AdminVisits />}
      {tab === 'categories' && <AdminCategories />}
      {tab === 'users' && <AdminUsers />}
      {tab === 'loans' && <AdminLoans />}
      {tab === 'neighborhoods' && <AdminNeighborhoods />}
      {tab === 'items' && <AdminItems />}
      {tab === 'questions' && <AdminQuestions />}
      {tab === 'sales' && <AdminSales />}
      {tab === 'commissions' && <AdminCommissions />}
      {tab === 'calls' && <AdminCalls />}
      {tab === 'payouts' && <AdminPayouts />}
      {tab === 'reviews' && <AdminReviews />}
      {tab === 'favorites' && <AdminFavorites />}
    </div>
  )
}
