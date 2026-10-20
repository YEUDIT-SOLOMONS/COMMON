import { useEffect, useMemo, useRef, useState } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { itemsApi } from '../api/endpoints.js'
import { useAuth } from '../context/AuthContext.jsx'
import { BrandMark } from './BrandMark.jsx'
import { CameraIcon } from './CameraIcon.jsx'
import { NavSearchGlyph } from './glyphs.jsx'
import { Wordmark } from './Wordmark.jsx'

const MAX_SUGGESTIONS = 6

export function Navbar() {
  const { user, isAuthenticated, logout } = useAuth()
  const navigate = useNavigate()
  const [headerSearch, setHeaderSearch] = useState('')
  const [suggestionsOpen, setSuggestionsOpen] = useState(false)
  const [allItems, setAllItems] = useState([])
  const searchWrapRef = useRef(null)
  const headerRef = useRef(null)
  const [scrolled, setScrolled] = useState(false)

  // בגלילה: ההידר מצטמצם ומקבל זכוכית מטושטשת, ופס התקדמות בצבעי המותג מתמלא מתחתיו
  useEffect(() => {
    let frame = 0

    function update() {
      frame = 0
      const el = headerRef.current
      if (!el) return
      const max = document.documentElement.scrollHeight - window.innerHeight
      const progress = max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 0
      el.style.setProperty('--progress', progress.toFixed(4))
      setScrolled((was) => (was ? window.scrollY > 8 : window.scrollY > 24))
    }

    function onScroll() {
      if (!frame) frame = requestAnimationFrame(update)
    }

    update()
    window.addEventListener('scroll', onScroll, { passive: true })
    window.addEventListener('resize', onScroll)
    return () => {
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('resize', onScroll)
      if (frame) cancelAnimationFrame(frame)
    }
  }, [])

  // חושף את הגובה בפועל של ההידר כמשתנה CSS גלובלי (--navbar-height) - כך שורת המיון/סינון/זום
  // בדף הבית יכולה להידבק בדיוק מתחתיו ללא "קפיצה", גם כשגובה ההידר משתנה (למשל כשתיבת החיפוש
  // עוברת לשורה נפרדת ברוחב מסך צר)
  useEffect(() => {
    const el = headerRef.current
    if (!el) return

    function updateHeight() {
      document.documentElement.style.setProperty('--navbar-height', `${el.offsetHeight}px`)
    }

    updateHeight()
    const observer = new ResizeObserver(updateHeight)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  // רשימת כל הציוד נטענת פעם אחת ברקע (בלי לחסום כלום) כדי שתפריט ההצעות בחיפוש יוכל
  // להיפתח מיידית תוך כדי הקלדה, בלי לפנות לשרת בכל תו שמוקלד
  useEffect(() => {
    itemsApi
      .getAll()
      .then((result) => setAllItems(result ?? []))
      .catch(() => setAllItems([]))
  }, [])

  // סוגרים את תפריט ההצעות בלחיצה מחוץ לתיבת החיפוש
  useEffect(() => {
    function handleClickOutside(e) {
      if (searchWrapRef.current && !searchWrapRef.current.contains(e.target)) {
        setSuggestionsOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const suggestions = useMemo(() => {
    const term = headerSearch.trim().toLowerCase()
    if (!term) return []
    return allItems
      .filter(
        (item) =>
          item.name.toLowerCase().includes(term) || (item.description ?? '').toLowerCase().includes(term),
      )
      .slice(0, MAX_SUGGESTIONS)
  }, [allItems, headerSearch])

  function handleLogout() {
    logout()
    navigate('/')
  }

  function goToSearch(term) {
    setSuggestionsOpen(false)
    navigate(term ? `/?q=${encodeURIComponent(term)}` : '/')
  }

  function handleSearchSubmit(e) {
    e.preventDefault()
    goToSearch(headerSearch.trim())
  }

  function handleSuggestionClick(item) {
    setHeaderSearch(item.name)
    goToSearch(item.name)
  }

  return (
    <>
    <header className={`navbar ${scrolled ? 'navbar-scrolled' : ''}`} ref={headerRef}>
      <div className="navbar-inner">
        <NavLink to="/" className="brand">
          <Wordmark className="brand-wordmark" />
          <BrandMark className="brand-icon" />
        </NavLink>

        <nav className="nav-links">
          <NavLink to="/" end className={({ isActive }) => `nav-pill ${isActive ? 'nav-pill-active' : ''}`}>
            השכרת ציוד
          </NavLink>
          <NavLink to="/yad2" className={({ isActive }) => `nav-pill ${isActive ? 'nav-pill-active' : ''}`}>
            יד 2
          </NavLink>
          <NavLink to="/contact" className={({ isActive }) => `nav-pill ${isActive ? 'nav-pill-active' : ''}`}>
            יצירת קשר
          </NavLink>
          {isAuthenticated && (
            <NavLink
              to="/profile"
              className={({ isActive }) => `nav-pill nav-pill-account ${isActive ? 'nav-pill-active' : ''}`}
            >
              החשבון שלי
            </NavLink>
          )}
        </nav>

        <div className="navbar-search-wrap" ref={searchWrapRef}>
          <form className="navbar-search" onSubmit={handleSearchSubmit} role="search" autoComplete="off">
            <NavSearchGlyph className="navbar-search-icon" />
            <input
              type="search"
              value={headerSearch}
              onChange={(e) => {
                setHeaderSearch(e.target.value)
                setSuggestionsOpen(true)
              }}
              onFocus={() => setSuggestionsOpen(true)}
              placeholder="חיפוש בקטלוג הציוד..."
              aria-label="חיפוש בקטלוג הציוד"
            />
          </form>

          {suggestionsOpen && suggestions.length > 0 && (
            <div className="navbar-search-suggestions">
              {suggestions.map((item) => (
                <button
                  type="button"
                  key={item.id}
                  className="navbar-search-suggestion"
                  onClick={() => handleSuggestionClick(item)}
                >
                  <span className="navbar-search-suggestion-media">
                    {item.imageDataUrl ? (
                      <img src={item.imageDataUrl} alt={item.name} loading="lazy" decoding="async" />
                    ) : (
                      <CameraIcon />
                    )}
                  </span>
                  <span className="navbar-search-suggestion-text">
                    <span className="navbar-search-suggestion-name">{item.name}</span>
                    <span className="navbar-search-suggestion-price">
                      {item.forSale ? `₪${item.salePrice} · למכירה` : `₪${item.dailyPrice} / יום${item.addVat ? ' כולל מע״מ' : ''}`}
                    </span>
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="nav-auth">
          {isAuthenticated ? (
            <>
              <span className="nav-greeting">שלום, {user.name}</span>
              <button type="button" className="btn btn-ghost" onClick={handleLogout}>
                התנתקות
              </button>
            </>
          ) : (
            <>
              <NavLink to="/login" className="btn btn-ghost">
                התחברות
              </NavLink>
              <NavLink to="/register" className="btn btn-primary">
                הצטרפות
              </NavLink>
            </>
          )}
        </div>
      </div>
    </header>
    </>
  )
}
