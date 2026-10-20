import { useEffect, useRef, useState } from 'react'
import { BrandMark } from './BrandMark.jsx'
import { CornerArrowGlyph } from './glyphs.jsx'
import { ByOnyx, Wordmark } from './Wordmark.jsx'

const SLOGAN = ['קהילת', 'הצלמות', 'להשכרת', 'ומכירת', 'ציוד', 'צילום']
const EXIT_MS = 750

// המסך מוצג פעם אחת בכל ביקור (לשונית): נזכר בזיכרון הדף וב-sessionStorage, כך שלחיצה על "קטלוג הציוד"
// (גם בלי להתחבר, וגם אחרי כניסה ישירה לעמוד אחר כמו התחברות) תמיד מציגה את הדף הראשי ולא את מסך הפתיחה.
const SEEN_KEY = 'photorental.splashSeen'
let seenThisLoad = window.location.pathname !== '/' // נכנסו דרך עמוד אחר - אין "כניסה" ראשונית להציג

function readSeen() {
  try {
    return sessionStorage.getItem(SEEN_KEY) === '1'
  } catch {
    return false
  }
}

function markSeen() {
  seenThisLoad = true
  try {
    sessionStorage.setItem(SEEN_KEY, '1')
  } catch {
    // אחסון חסום - נשארים עם הזיכרון של הדף בלבד
  }
}

function wasSeen() {
  return seenThisLoad || readSeen() || new URLSearchParams(window.location.search).has('nosplash')
}

// מסך הפתיחה (עמוד 1 בעיצוב): קשת בצורת U שנשרטת מעצמה, הלוגו נחשף, שלושת העיגולים נפגשים
// ו"נושמים", והסלוגן עולה מילה אחרי מילה. העיגולים זזים קלות אחרי העכבר, וחץ הכניסה פועם
// כהזמנה. בלחיצה (על החץ / על המסך / Enter / Esc) העיגול הוורוד מתרחב וממלא את המסך ופותח את האתר.
// מוצג פעם אחת בכל ביקור (לא בניווט בין עמודים ולא בכניסה דרך עמוד אחר); ?nosplash בכתובת מדלג עליו.
export function Splash() {
  const [open, setOpen] = useState(() => !wasSeen())
  const [leaving, setLeaving] = useState(false)
  const stageRef = useRef(null)

  function close() {
    if (leaving) return
    setLeaving(true)
    window.setTimeout(() => {
      markSeen()
      setOpen(false)
    }, EXIT_MS)
  }

  useEffect(() => {
    if (!open) return
    function onKey(e) {
      if (e.key === 'Enter' || e.key === 'Escape') close()
    }
    document.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, leaving])

  // פרלקסה עדינה: העיגולים זזים בכיוונים שונים לפי מיקום העכבר/האצבע
  function handlePointerMove(e) {
    const el = stageRef.current
    if (!el) return
    const mx = (e.clientX / window.innerWidth - 0.5) * 2
    const my = (e.clientY / window.innerHeight - 0.5) * 2
    el.style.setProperty('--mx', mx.toFixed(3))
    el.style.setProperty('--my', my.toFixed(3))
  }

  if (!open) return null

  return (
    <div
      className={`splash ${leaving ? 'splash-leaving' : ''}`}
      onClick={close}
      onPointerMove={handlePointerMove}
      role="dialog"
      aria-label="common - מסך פתיחה"
    >
      <div className="splash-stage" ref={stageRef}>
        <svg className="splash-u" viewBox="0 0 854.5 1594.1" aria-hidden="true" focusable="false">
          <path
            pathLength="1"
            d="M 0.5 0 L 0.5 1166.85 A 426.75 426.75 0 0 0 854 1166.85 L 854 0"
          />
        </svg>

        <div className="splash-logo" aria-label="COMMON BY ONYX">
          <Wordmark className="splash-wordmark" />
          <ByOnyx className="splash-by" />
        </div>

        <p className="splash-slogan">
          {SLOGAN.map((word, i) => (
            <span key={word} style={{ '--i': i }}>
              {word}
            </span>
          ))}
        </p>

        <BrandMark className="splash-mark" />

        <p className="splash-hint">לחצו להיכנס</p>

        <button type="button" className="splash-enter" onClick={close} aria-label="כניסה לאתר">
          <span className="splash-enter-ring" aria-hidden="true" />
          <CornerArrowGlyph />
        </button>
      </div>
    </div>
  )
}
