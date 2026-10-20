import { Link, Navigate, Route, Routes } from 'react-router-dom'
import { Wordmark } from './components/Wordmark.jsx'
import { Navbar } from './components/Navbar.jsx'
import { PageTitle } from './components/PageTitle.jsx'
import { WelcomeTransition } from './components/WelcomeTransition.jsx'
import { ProtectedRoute } from './components/ProtectedRoute.jsx'
import { PhoneRequiredGate } from './components/PhoneRequiredGate.jsx'
import { WaitingRequests } from './components/WaitingRequests.jsx'
import { UnreadQuestions } from './components/UnreadQuestions.jsx'
import { DEMO_SALE } from './api/demoSale.js'
import { RegisteredNotice } from './components/RegisteredNotice.jsx'
import { useAuth } from './context/AuthContext.jsx'
import { AdminPanel } from './pages/AdminPanel.jsx'
import { Contact } from './pages/Contact.jsx'
import { ForgotPassword } from './pages/ForgotPassword.jsx'
import { Home } from './pages/Home.jsx'
import { CommissionPaid } from './pages/CommissionPaid.jsx'
import { CommissionPay } from './pages/CommissionPay.jsx'
import { CommissionProof } from './pages/CommissionProof.jsx'
import { ItemAction } from './pages/ItemAction.jsx'
import { LoanAction } from './pages/LoanAction.jsx'
import { RenterPickup } from './pages/RenterPickup.jsx'
import { CallBack } from './pages/CallBack.jsx'
import { AvailabilityAnswer } from './pages/AvailabilityAnswer.jsx'
import { Login } from './pages/Login.jsx'
import { NotFound } from './pages/NotFound.jsx'
import { OrderPlaced } from './pages/OrderPlaced.jsx'
import { ReturnOptions } from './pages/ReturnOptions.jsx'
import { ShippingPaid } from './pages/ShippingPaid.jsx'
import { Profile } from './pages/Profile.jsx'
import { Register } from './pages/Register.jsx'
import { ResetPassword } from './pages/ResetPassword.jsx'
import { Terms } from './pages/Terms.jsx'
import { OrderPaid } from './pages/OrderPaid.jsx'
import { QuestionThread } from './pages/QuestionThread.jsx'
import { Purchase } from './pages/Purchase.jsx'

function App() {
  const { isAuthenticated, user } = useAuth()
  const needsPhone = isAuthenticated && user?.role !== 'Admin' && !user?.phone?.trim()

  return (
    <>
      <a href="#main-content" className="skip-link">
        דילוג לתוכן הראשי
      </a>
      <WelcomeTransition />
      {needsPhone && <PhoneRequiredGate />}
      {DEMO_SALE && (
        <div className="demo-banner">
          מצב הדגמה מקומי - שום דבר לא נשמר ואין תשלום אמיתי · <a href="/?demo=off">יציאה מההדגמה</a>
        </div>
      )}
      {(isAuthenticated || new URLSearchParams(window.location.search).get('demo') === 'waiting') && <WaitingRequests />}
      {isAuthenticated && <UnreadQuestions />}
      <RegisteredNotice />
      <PageTitle />
      <Navbar />
      <main id="main-content" tabIndex={-1}>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/yad2" element={<Home />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />
          <Route path="/reset-password" element={<ResetPassword />} />
          <Route
            path="/shipping-paid"
            element={
              <ProtectedRoute>
                <ShippingPaid />
              </ProtectedRoute>
            }
          />
          <Route path="/order/demo" element={<OrderPlaced />} />
          <Route
            path="/order/:loanId/return"
            element={
              <ProtectedRoute>
                <ReturnOptions />
              </ProtectedRoute>
            }
          />
          <Route
            path="/order/:loanId"
            element={
              <ProtectedRoute>
                <OrderPlaced />
              </ProtectedRoute>
            }
          />
          <Route
            path="/profile"
            element={
              <ProtectedRoute>
                <Profile />
              </ProtectedRoute>
            }
          />
          <Route
            path="/order-paid"
            element={
              <ProtectedRoute>
                <OrderPaid />
              </ProtectedRoute>
            }
          />
          <Route
            path="/admin"
            element={
              <ProtectedRoute>
                <AdminPanel />
              </ProtectedRoute>
            }
          />
          <Route path="/admin/categories" element={<Navigate to="/admin" replace />} />
          <Route path="/loan-action/:loanId" element={<LoanAction />} />
          <Route path="/questions/:id" element={<QuestionThread />} />
          <Route path="/purchase/:id" element={<Purchase />} />
          <Route path="/pickup/:loanId" element={<RenterPickup />} />
          <Route path="/call-back/:logId" element={<CallBack />} />
          <Route path="/availability-answer/:requestId" element={<AvailabilityAnswer />} />
          <Route path="/item-action/:itemId" element={<ItemAction />} />
          <Route path="/commission-proof" element={<CommissionProof />} />
          <Route path="/commission-pay" element={<CommissionPay />} />
          <Route path="/commission-paid" element={<CommissionPaid />} />
          <Route path="/terms" element={<Terms />} />
          <Route path="/contact" element={<Contact />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </main>
      <footer className="site-footer">
        <div className="site-footer-grid">
          <div className="site-footer-brand">
            <Wordmark className="site-footer-wordmark" />
            <p>קהילת צלמות להשכרה, קנייה ומכירה של ציוד צילום בין צלמות בציבור החרדי.</p>
            <p>מתאים. נוח. גמיש.</p>
          </div>

          <div className="site-footer-col">
            <h2>ניווט</h2>
            <Link to="/">קטלוג ההשכרה</Link>
            <Link to="/yad2">לוח יד 2</Link>
            {isAuthenticated ? <Link to="/profile">החשבון שלי</Link> : <Link to="/register">הצטרפות לקהילה</Link>}
            <Link to="/contact">יצירת קשר</Link>
          </div>

          <div className="site-footer-col">
            <h2>מידע</h2>
            <Link to="/terms">תנאי שימוש</Link>
            {!isAuthenticated && <Link to="/login">התחברות</Link>}
          </div>

          <div className="site-footer-col">
            <h2>יצירת קשר</h2>
            <a href="mailto:common@onyxapp.co.il">common@onyxapp.co.il</a>
          </div>
        </div>

        <div className="site-footer-bottom">
          <p>
            תכנון, פיתוח טכנולוגי וניהול: יהודית סלומונס {new Date().getFullYear()} · עיצוב: גיטי שורץ{' '}
            <span className="site-footer-credit-mail">gitish330@gmail.com</span>
          </p>
          <p>COMMON ©{new Date().getFullYear()} · קהילת הצלמות להשכרת ציוד ויד 2</p>
        </div>
      </footer>
    </>
  )
}

export default App
