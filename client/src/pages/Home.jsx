import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { categoriesApi, itemsApi, loansApi, reviewsApi, usersApi } from '../api/endpoints.js'
import { AddItemModal } from '../components/AddItemModal.jsx'
import { BrandMark } from '../components/BrandMark.jsx'
import {
  BoxGlyph,
  CardGlyph,
  ExtraCameraGlyph,
  PhoneGlyph,
  SearchGlyph,
  ShieldGlyph,
  TruckGlyph,
  UsersGlyph,
} from '../components/glyphs.jsx'
import { FilterIcon, GridDenseIcon, GridSparseIcon, SearchIcon } from '../components/icons.jsx'
import { ItemCard } from '../components/ItemCard.jsx'
import { buildCategoryChains } from '../utils/categoryIcon.js'
import { RentModal } from '../components/RentModal.jsx'
import { BuyModal } from '../components/BuyModal.jsx'
import { todayIsraelIso } from '../utils/format.js'
import { SortDropdown } from '../components/SortDropdown.jsx'
import { Splash } from '../components/Splash.jsx'
import { useAuth } from '../context/AuthContext.jsx'
import { Loader } from '../components/Loader.jsx'
import { DEMO_SALE } from '../api/demoSale.js'
import { SuggestCategoryModal } from '../components/SuggestCategoryModal.jsx'

// סליידר-זום: 5 דרגות (0..4) = 7,6,5,4,3 כרטיסים בשורה. הכרטיסים תמיד ממלאים את השורה בדיוק, וכל
// תוכן הכרטיס (כתב, מסגרות, תמונה) מתכווץ/מתנפח באותו יחס, כך שהגודל משתנה בצורה מאוזנת.
const ZOOM_MIN = 0
const ZOOM_MAX = 4
const ZOOM_DEFAULT = 2
const ZOOM_LABELS = ['זעיר', 'קטן', 'בינוני', 'גדול', 'ענק']
const ZOOM_COLUMNS = [7, 6, 5, 4, 3]

const emptyFilters = { priceMin: '', priceMax: '', city: 'all', onlyAvailable: false, dateFrom: '', dateTo: '' }

// אפשרויות המיון המוצגות למשתמש. 'proximity' ו-'popularity' מחושבות מנתונים אמיתיים
// שכבר קיימים במערכת (עיר המשתמש/הפריט, ומספר ההשכרות בפועל של כל פריט) - לא ניחוש.
const MY_CITY_KEY = 'common_my_city'

const SORT_OPTIONS = [
  { value: 'random', label: 'אקראי' },
  { value: 'priceAsc', label: 'מחיר: מהנמוך לגבוה' },
  { value: 'priceDesc', label: 'מחיר: מהגבוה לנמוך' },
  { value: 'proximity', label: 'הכי קרוב אליי' },
  { value: 'popularity', label: 'המבוקשים בקהילה' },
]

// קו הגל ("החבל") של "איך זה עובד": חצאי עיגולים ברדיוס 106.67 (= חצי רוחב עמודה) שמתחלפים
// למעלה/למטה, בדיוק כמו בעיצוב. מערכת הקואורדינטות היא של עמודת הבמה (852 רוחב) - ארבע עמודות
// של 213.33, כשהקו חוצה את קו-הבסיס (y=140.6) בגבולות העמודות (שם יושבים הסימנים, ולכן הם
// נשארים במקום). הקו ממשיך הרבה מעבר לעמודה לשני הצדדים (overflow: visible).
// התנועה: "גל" נע לאורך החבל מימין לשמאל - כל קשת בתורה מתנפחת קלות (עד ~14%) וחוזרת, והבאה
// אחריה ממשיכה, וכך נראה כאילו החבל מתנועע. נקודות החצייה לא זזות.
const WAVE_COLUMN = 852 / 4
const WAVE_BASELINE = 140.6
const WAVE_FROM = -12
const WAVE_TO = 16
const WAVE_FRAMES = 48

function buildWavePath(heightFactor = () => 1) {
  const r = WAVE_COLUMN / 2
  const f = (n) => Math.round(n * 100) / 100
  let d = ''
  for (let k = WAVE_FROM; k < WAVE_TO; k++) {
    const x = k * WAVE_COLUMN
    if (k === WAVE_FROM) d += `M ${x} ${WAVE_BASELINE}`
    // k אי-זוגי = קשת עליונה (sweep 1), זוגי = תחתונה. שלב 1 (ימין) = העמודה האחרונה (k=3) = קשת עליונה
    d += ` A ${r} ${f(r * heightFactor(k))} 0 0 ${k % 2 ? 1 : 0} ${x + WAVE_COLUMN} ${WAVE_BASELINE}`
  }
  return d
}

const HOW_IT_WORKS_WAVE = buildWavePath()

// מרכז הגל c נע מקשת 8 (מימין למסך) עד קשת -5 (משמאל למסך); הלולאה חלקה כי בקצוות הגל מחוץ למסך
const HOW_IT_WORKS_WAVE_KEYFRAMES = Array.from({ length: WAVE_FRAMES + 1 }, (_, n) => {
  const c = 8 - (13 * n) / WAVE_FRAMES
  return buildWavePath((k) => 1 + 0.14 * Math.exp(-((k - c) ** 2) / (2 * 1.1 ** 2)))
}).join(';')

// טבעת פתוחה (קשת של כ-190 מעלות, פתוחה לצד הכותרת) עם סימן המותג והאייקון במרכזה
function PillarMark({ children }) {
  return (
    <span className="trust-pillar-mark" aria-hidden="true">
      <svg className="trust-pillar-arc" viewBox="0 0 100 100" focusable="false">
        <path d="M 56.3 0.8 A 49.5 49.5 0 1 0 39 99.2" />
      </svg>
      <BrandMark className="pillar-brandmark">{children}</BrandMark>
    </span>
  )
}

// הקטלוג האחרון נשמר בדפדפן ומוצג מיד בכניסה הבאה, ומתעדכן ברקע
const CATALOG_KEY = 'common.catalog.v1'

function readCatalog() {
  if (DEMO_SALE) return null
  try {
    const saved = JSON.parse(localStorage.getItem(CATALOG_KEY) ?? 'null')
    return Array.isArray(saved?.items) && saved.items.length ? saved : null
  } catch {
    return null
  }
}

function writeCatalog(categories, items) {
  if (DEMO_SALE) return
  try {
    localStorage.setItem(CATALOG_KEY, JSON.stringify({ categories, items }))
  } catch {
    // אין מקום או אחסון חסום - פשוט טוענים מהשרת בפעם הבאה
  }
}

export function Home() {
  const { user, isAuthenticated, updateUser } = useAuth()
  // SMIL לא מכבד את prefers-reduced-motion דרך CSS - לכן תנועת החבל פשוט לא מרונדרת במקרה כזה
  const prefersReducedMotion = useMemo(
    () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false,
    [],
  )
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  // לוח יד 2: אותו קטלוג ואותן קטגוריות, רק עם הפריטים שלמכירה
  const saleMode = useLocation().pathname === '/yad2'

  const [cachedCatalog] = useState(readCatalog)
  const [categories, setCategories] = useState(() => cachedCatalog?.categories ?? [])
  const [items, setItems] = useState(() => cachedCatalog?.items ?? [])
  const [loans, setLoans] = useState([])
  const [loading, setLoading] = useState(!cachedCatalog)
  const [loadingRest, setLoadingRest] = useState(false)
  const initialSaleMode = useRef(saleMode)
  const [error, setError] = useState('')
  const [activeCategoryId, setActiveCategoryId] = useState('all')
  // תת-קטגוריה נבחרת בתוך הקטגוריה הראשית ('' = כל התת-קטגוריות)
  const [activeSubId, setActiveSubId] = useState('')
  const [search, setSearch] = useState(() => searchParams.get('q') ?? '')
  // ברירת המחדל למיון היא "הכי קרוב אליי" (proximity) - כל עוד המשתמש לא בחר מיון אחר בעצמו
  const [sortBy, setSortBy] = useState('random')
  const [suggestOpen, setSuggestOpen] = useState(false)
  // הקטלוג נבנה בהדרגה: 24 פריטים ראשונים, ועוד 24 בכל פעם שמגיעים בגלילה לסוף
  const PAGE_SIZE = 24
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE)
  const moreRef = useRef(null)
  // סדר אקראי קבוע לאורך הביקור (לא מתערבב מחדש בכל סינון)
  const randomRank = useRef(new Map())

  // מצב הזום (ראו ZOOM_* למעלה): ערך נמוך = כרטיסים קטנים ויותר בשורה, ערך גבוה = כרטיסים גדולים
  const [zoomLevel, setZoomLevel] = useState(ZOOM_DEFAULT)
  const zoomLabel = ZOOM_LABELS[zoomLevel]
  const zoomColumns = ZOOM_COLUMNS[zoomLevel]
  // מיקום הידית באחוזים משמאל (המסלול הפוך ב-RTL: ערך קטן = ימין) - לצביעת המסלול ורוד/ירוק
  const zoomPos = 100 - ((zoomLevel - ZOOM_MIN) / (ZOOM_MAX - ZOOM_MIN)) * 100

  // שורת המיון/סינון/זום נדבקת מתחת להידר בגלילה (ראו .search-filter-row ב-CSS: position: sticky) -
  // ה-listener הזה רק מוסיף מחלקת is-stuck כשהיא בפועל "נדבקה" (הגיעה לגובה ההידר), כדי להעצים
  // מעט את הצללית ולתת תחושת עומק בזמן שהיא צפה מעל הפריטים. משווים ישירות את מיקום ה"עוקב"
  // הבלתי-נראה שמונח צמוד אליה מלמעלה מול גובה ההידר בפועל - פשוט וישיר יותר מ-IntersectionObserver
  // עבור מקרה כזה של סף שמשתנה עם גובה ההידר הדינמי.
  const [toolbarStuck, setToolbarStuck] = useState(false)
  const stickySentinelRef = useRef(null)
  const toolbarRef = useRef(null)

  useEffect(() => {
    function handleScroll() {
      const sentinel = stickySentinelRef.current
      if (!sentinel) return
      const navbarHeight =
        parseInt(getComputedStyle(document.documentElement).getPropertyValue('--navbar-height'), 10) || 76
      setToolbarStuck(sentinel.getBoundingClientRect().top <= navbarHeight)
    }

    handleScroll()
    window.addEventListener('scroll', handleScroll, { passive: true })
    window.addEventListener('resize', handleScroll)
    return () => {
      window.removeEventListener('scroll', handleScroll)
      window.removeEventListener('resize', handleScroll)
    }
  }, [])

  // חושף את הגובה בפועל של שורת המיון/סינון/זום כמשתנה CSS (--toolbar-height) - כדי שכשפותחים
  // "סינון" תוך כדי גלילה, פאנל האפשרויות יוכל להידבק בדיוק מתחתיה (ולא להיעלם למטה בדף, במקום
  // שבו השורה הייתה עומדת אילולא נדבקה) - ראו .filters-panel ב-CSS
  useEffect(() => {
    const el = toolbarRef.current
    if (!el) return

    function updateHeight() {
      document.documentElement.style.setProperty('--toolbar-height', `${el.offsetHeight}px`)
    }

    updateHeight()
    const observer = new ResizeObserver(updateHeight)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  // חיפוש מהתיבה שבהידר (זמינה בכל עמוד) מנווט לכאן עם ?q=... - מסונכרן גם אם הדף כבר טעון
  useEffect(() => {
    const q = searchParams.get('q')
    if (q !== null) setSearch(q)
  }, [searchParams])

  const [filtersOpen, setFiltersOpen] = useState(false)
  const [filters, setFilters] = useState(emptyFilters)

  const [addItemOpen, setAddItemOpen] = useState(false)
  const [rentItem, setRentItem] = useState(null)
  const [ratings, setRatings] = useState({})

  useEffect(() => {
    reviewsApi
      .summary()
      .then((r) => setRatings(r ?? {}))
      .catch(() => {})
  }, [])

  // קישור ישיר לחלון פריט (למשל מהמייל "הפריט התפנה"): /?item=<id>
  const deepLinkItemId = searchParams.get('item')
  const [confirmedCallId, setConfirmedCallId] = useState(null)
  // שעות מהקישור (st/rt) וכתובת לחזרה אחרי התחברות - כדי שהקישור "להמשך ההזמנה" יעבוד גם למי שלא מחוברת
  const [deepLinkTimes, setDeepLinkTimes] = useState(null)
  const [deepLinkReturnTo, setDeepLinkReturnTo] = useState(null)
  useEffect(() => {
    if (!deepLinkItemId || loading) return
    let cancelled = false
    const found = items.find((i) => i.id === deepLinkItemId)
    const from = searchParams.get('from')
    const to = searchParams.get('to')
    const confirmed = searchParams.get('confirmed')
    const st = searchParams.get('st')
    const rt = searchParams.get('rt')
    const q = Number(searchParams.get('q')) || null
    const returnTo = `/?${searchParams.toString()}`
    const open = (item) => {
      if (cancelled || !item) return
      if (from && to) setFilters((prev) => ({ ...prev, dateFrom: from, dateTo: to }))
      setDeepLinkTimes(from && to && /^\d\d:\d\d$/.test(st ?? '') && /^\d\d:\d\d$/.test(rt ?? '') ? { st, rt, q } : q ? { q } : null)
      setDeepLinkReturnTo(returnTo)
      setConfirmedCallId(confirmed || null)
      setRentItem(item)
      navigate('/', { replace: true })
    }
    if (found) open(found)
    else itemsApi.getById(deepLinkItemId).then(open).catch(() => {})
    return () => {
      cancelled = true
    }
  }, [deepLinkItemId, loading, items, navigate, searchParams])

  const load = useCallback(async () => {
    if (!cachedCatalog) setLoading(true)
    setError('')
    let fullLoaded = false
    if (!cachedCatalog)
      itemsApi
        .firstPage(PAGE_SIZE, initialSaleMode.current)
        .then((first) => {
          if (fullLoaded || !Array.isArray(first) || !first.length) return
          first.forEach((it, i) => randomRank.current.set(it.id, i / 1000 - 1))
          setItems(first)
          setLoading(false)
          setLoadingRest(true)
        })
        .catch(() => {})
    try {
      const [categoriesResult, itemsResult] = await Promise.all([
        categoriesApi.getAll().then((result) => {
          setCategories(result ?? [])
          return result
        }),
        itemsApi.getAll(),
      ])
      fullLoaded = true
      setItems(itemsResult ?? [])
      writeCatalog(categoriesResult ?? [], itemsResult ?? [])
    } catch (err) {
      if (!cachedCatalog) setError(err.message)
    } finally {
      fullLoaded = true
      setLoading(false)
      setLoadingRest(false)
    }

    // נתוני ההשכרות משמשים רק לחישוב "מיון לפי פופולאריות" (כמה פעמים כל פריט הושכר בפועל).
    // נטען בנפרד ובלי לחסום את הדף: אם הטעינה נכשלת, המיון לפי פופולאריות פשוט לא יבדיל בין
    // הפריטים (כולם ייחשבו כ-0 השכרות) במקום לשבור את הצגת הקטלוג כולו.
    try {
      const counts = await loansApi.popularity()
      setLoans(Object.entries(counts ?? {}).map(([itemId, count]) => ({ itemId, count })))
    } catch {
      // שרת ישן (בלי popularity) - נופלים חזרה לרשימה המלאה
      try {
        const loansResult = await loansApi.getAll()
        const counts = new Map()
        for (const loan of loansResult ?? []) counts.set(loan.itemId, (counts.get(loan.itemId) ?? 0) + 1)
        setLoans([...counts].map(([itemId, count]) => ({ itemId, count })))
      } catch {
        setLoans([])
      }
    }
  }, [cachedCatalog])

  useEffect(() => {
    load()
  }, [load])

  const cities = useMemo(() => {
    const set = new Set(items.map((item) => item.city).filter(Boolean))
    return Array.from(set).sort((a, b) => a.localeCompare(b, 'he'))
  }, [items])

  // סדר הקטגוריות בשורת הצ'יפים - לפי הסדר שקבעה המנהלת במסך הניהול (ובשוויון - לפי א"ב)
  const sortedCategories = useMemo(
    () =>
      [...categories].sort(
        (a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name, 'he'),
      ),
    [categories],
  )

  // קטגוריות ראשיות (בשורת הצ'יפים) ותתי-קטגוריות של הנבחרת (בשורה שנייה)
  const categoryChains = useMemo(() => buildCategoryChains(categories), [categories])

  const topCategories = useMemo(() => sortedCategories.filter((c) => !c.parentId), [sortedCategories])
  const activeSubCategories = useMemo(
    () => sortedCategories.filter((c) => c.parentId && c.parentId === activeCategoryId),
    [sortedCategories, activeCategoryId],
  )

  const subCategoryIdsOfActive = useMemo(
    () => new Set(sortedCategories.filter((c) => c.parentId === activeCategoryId).map((c) => c.id)),
    [sortedCategories, activeCategoryId],
  )

  // כמה פעמים כל פריט הושכר בפועל (לפי היסטוריית ההשכרות האמיתית) - הבסיס למיון "לפי פופולאריות".
  const rentalCountByItemId = useMemo(() => {
    const map = new Map()
    for (const loan of loans) {
      map.set(loan.itemId, (map.get(loan.itemId) ?? 0) + (loan.count ?? 1))
    }
    return map
  }, [loans])

  // "זמינים בלבד" אינו נספר כאן - הוא כפתור עצמאי וקבוע בשורת הכלים (לא מוסתר מאחורי "סינון"),
  // ולכן אין טעם ב"תג" שמזכיר למשתמש שהוא פעיל
  const activeFilterCount =
    (filters.priceMin ? 1 : 0) +
    (filters.priceMax ? 1 : 0) +
    (filters.city !== 'all' ? 1 : 0) +
    (filters.dateFrom && filters.dateTo ? 1 : 0)

  // "פנוי בתאריכים שלי": כשנבחרו שני התאריכים - השרת מחזיר אילו פריטים תפוסים בטווח
  const datesValid = Boolean(filters.dateFrom && filters.dateTo && filters.dateTo >= filters.dateFrom)
  const [busyIds, setBusyIds] = useState(null)
  useEffect(() => {
    if (!datesValid) return undefined
    let cancelled = false
    itemsApi
      .busy(filters.dateFrom, filters.dateTo)
      .then((ids) => {
        if (!cancelled) setBusyIds({ key: `${filters.dateFrom}|${filters.dateTo}`, ids: new Set(ids ?? []) })
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [datesValid, filters.dateFrom, filters.dateTo])
  const busySet = datesValid && busyIds?.key === `${filters.dateFrom}|${filters.dateTo}` ? busyIds.ids : null

  // כשיש טקסט חיפוש, החיפוש פועל על כל הקטלוג (לא רק על הקטגוריה שנבחרה כרגע) - אחרת פריט
  // שנמצא בקטגוריה אחרת פשוט "נעלם" ונראה כאילו החיפוש לא עובד.
  const isSearching = search.trim().length > 0

  const filteredItems = useMemo(() => {
    const term = search.trim().toLowerCase()
    const min = filters.priceMin ? Number(filters.priceMin) : null
    const max = filters.priceMax ? Number(filters.priceMax) : null

    return items.filter((item) => {
      // קטגוריה ראשית כוללת גם את הפריטים שבתתי-הקטגוריות שלה; תת-קטגוריה נבחרת מצמצמת אליה בלבד
      const priceOf = item.forSale ? item.salePrice : item.dailyPrice
      if (saleMode !== Boolean(item.forSale)) return false
      const matchesCategory =
        isSearching ||
        activeCategoryId === 'all' ||
        [item.categoryId, item.extraCategoryId].some((id) =>
          activeSubId ? id === activeSubId : id && (id === activeCategoryId || subCategoryIdsOfActive.has(id)),
        )
      const matchesSearch =
        !term ||
        item.name.toLowerCase().includes(term) ||
        (item.description ?? '').toLowerCase().includes(term)
      const matchesMin = min === null || priceOf >= min
      const matchesMax = max === null || priceOf <= max
      const matchesCity = filters.city === 'all' || item.city === filters.city
      const matchesAvailability = !filters.onlyAvailable || item.status === 'Available'
      const matchesDates = !busySet || !busySet.has(item.id)
      return (
        matchesCategory && matchesSearch && matchesMin && matchesMax && matchesCity && matchesAvailability && matchesDates
      )
    })
  }, [items, activeCategoryId, activeSubId, subCategoryIdsOfActive, search, filters, isSearching, busySet, saleMode])

  // עיר הייחוס למיון "לפי קרבה": קודם עיר שנבחרה בסינון (אם נבחרה), אחרת עיר המגורים
  // השמורה בפרופיל המשתמש המחובר. פריטים מאותה עיר עולים ראשונים; שאר הפריטים ממוינים
  // לפי עיר בסדר א"ב כדי שפריטים "קרובים" עדיין יקובצו זה ליד זה.
  // מחוברת בלי עיר שמורה בדפדפן (כניסה ישנה) - העיר נשלפת מהחשבון
  useEffect(() => {
    if (!isAuthenticated || !user?.id || user.city) return
    usersApi
      .getById(user.id)
      .then((fresh) => {
        if (fresh?.city) updateUser({ city: fresh.city })
      })
      .catch(() => {})
  }, [isAuthenticated, user?.id, user?.city, updateUser])

  // לא מחוברת (או בלי עיר): "העיר שלי" לבחירה ליד המיון, נשמרת בדפדפן
  const [myCity, setMyCity] = useState(() => {
    try {
      return localStorage.getItem(MY_CITY_KEY) || ''
    } catch {
      return ''
    }
  })
  function chooseMyCity(city) {
    setMyCity(city)
    try {
      if (city) localStorage.setItem(MY_CITY_KEY, city)
      else localStorage.removeItem(MY_CITY_KEY)
    } catch {
      // בלי אחסון - רק לביקור הנוכחי
    }
  }
  // "לפי המיקום שלי": רק בלחיצה. מוצאים את העיר בקטלוג הקרובה ביותר למיקום הדפדפן
  const [locating, setLocating] = useState('')
  const [myPoint, setMyPoint] = useState(null)
  function locateMe() {
    if (!navigator.geolocation) {
      setLocating('failed')
      return
    }
    setLocating('busy')
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        setMyPoint({ lat: coords.latitude, lng: coords.longitude })
        const rad = (d) => (d * Math.PI) / 180
        let best = null
        for (const c of Object.values(cityCoords)) {
          if (!c) continue
          const dLat = rad(c.lat - coords.latitude)
          const dLng = rad(c.lng - coords.longitude)
          const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(coords.latitude)) * Math.cos(rad(c.lat)) * Math.sin(dLng / 2) ** 2
          if (!best || h < best.h) best = { city: c.city, h }
        }
        if (best) {
          chooseMyCity(best.city)
          setLocating('')
        } else {
          setLocating('failed')
        }
      },
      () => setLocating('failed'),
      { timeout: 10000, maximumAge: 600000 },
    )
  }

  const catalogCities = useMemo(
    () => [...new Set(items.map((i) => i.city).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'he')),
    [items],
  )

  const referenceCity = (filters.city !== 'all' ? filters.city : null) ?? user?.city ?? (myCity || null)

  // מיקומי הערים (נשלפים פעם אחת מהשרת) - למיון לפי מרחק אמיתי בק"מ
  const [cityCoords, setCityCoords] = useState({})
  useEffect(() => {
    if (sortBy !== 'proximity') return
    const wanted = [...new Set([referenceCity, ...items.map((i) => i.city)].filter(Boolean))].filter((c) => !(c in cityCoords))
    if (wanted.length === 0) return
    let cancelled = false
    // בקבוצות של 8: השרת מאתר עד 8 ערים חדשות בכל בקשה
    ;(async () => {
      for (let i = 0; i < wanted.length && !cancelled; i += 8) {
        const chunk = wanted.slice(i, i + 8)
        try {
          const list = await itemsApi.cityCoords(chunk)
          if (cancelled) return
          setCityCoords((prev) => {
            const next = { ...prev }
            for (const c of chunk) next[c] = null
            for (const c of list ?? []) next[c.city] = c
            return next
          })
        } catch {
          return
        }
      }
    })()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sortBy, referenceCity, items])

  // מרחק מכל פריט לפי הרחוב שלו (גם בתוך אותה עיר) - מחושב בשרת
  const [itemDistances, setItemDistances] = useState(null)
  useEffect(() => {
    if (sortBy !== 'proximity') return
    let cancelled = false
    itemsApi
      .distances({ lat: myPoint?.lat, lng: myPoint?.lng, city: referenceCity })
      .then((r) => {
        if (!cancelled) setItemDistances(r?.distances && Object.keys(r.distances).length ? r.distances : null)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [sortBy, myPoint, referenceCity, user?.id])

  const distanceKm = useCallback(
    (city) => {
      const a = cityCoords[referenceCity]
      const b = cityCoords[city]
      if (!a || !b) return null
      const rad = (d) => (d * Math.PI) / 180
      const dLat = rad(b.lat - a.lat)
      const dLng = rad(b.lng - a.lng)
      const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2
      return 6371 * 2 * Math.asin(Math.sqrt(h))
    },
    [cityCoords, referenceCity],
  )

  useEffect(() => {
    setVisibleCount(PAGE_SIZE)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeCategoryId, activeSubId, search, filters, sortBy])

  useEffect(() => {
    const el = moreRef.current
    if (!el || typeof IntersectionObserver === 'undefined') return undefined
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) setVisibleCount((n) => n + PAGE_SIZE)
      },
      { rootMargin: '800px 0px' },
    )
    observer.observe(el)
    return () => observer.disconnect()
  })

  const sortedItems = useMemo(() => {
    const result = [...filteredItems]

    switch (sortBy) {
      case 'random': {
        const rank = (id) => {
          if (!randomRank.current.has(id)) randomRank.current.set(id, Math.random())
          return randomRank.current.get(id)
        }
        result.sort((a, b) => rank(a.id) - rank(b.id))
        break
      }
      case 'priceAsc':
        result.sort((a, b) => (a.forSale ? a.salePrice : a.dailyPrice) - (b.forSale ? b.salePrice : b.dailyPrice))
        break
      case 'priceDesc':
        result.sort((a, b) => (b.forSale ? b.salePrice : b.dailyPrice) - (a.forSale ? a.salePrice : a.dailyPrice))
        break
      case 'popularity':
        // ציון (השכרות + שמירות), ובשוויון: פריטים זמינים קודם, ואז החדשים - כדי שיקבלו חשיפה
        result.sort(
          (a, b) =>
            (rentalCountByItemId.get(b.id) ?? 0) - (rentalCountByItemId.get(a.id) ?? 0) ||
            (a.status === 'Available' ? 0 : 1) - (b.status === 'Available' ? 0 : 1) ||
            (b.createdAtUtc ?? '').localeCompare(a.createdAtUtc ?? ''),
        )
        break
      case 'proximity':
        result.sort((a, b) => {
          const ia = itemDistances?.[a.id]
          const ib = itemDistances?.[b.id]
          if (ia != null && ib != null && ia !== ib) return ia - ib
          if ((ia == null) !== (ib == null)) return ia == null ? 1 : -1
          if (referenceCity) {
            const aIsNear = a.city === referenceCity ? 0 : 1
            const bIsNear = b.city === referenceCity ? 0 : 1
            if (aIsNear !== bIsNear) return aIsNear - bIsNear
            const da = distanceKm(a.city)
            const db = distanceKm(b.city)
            if (da != null && db != null && da !== db) return da - db
            if ((da == null) !== (db == null)) return da == null ? 1 : -1
          }
          return (a.city ?? '').localeCompare(b.city ?? '', 'he')
        })
        break
      default:
        // "מומלצים": פריטים זמינים עולים קודם (אין טעם להמליץ על פריט תפוס), ובתוך כל קבוצה
        // (זמין/תפוס) הפריטים המבוקשים ביותר בקהילה (הכי הרבה השכרות בפועל) עולים ראשונים.
        result.sort((a, b) => {
          const aAvailable = a.status === 'Available' ? 0 : 1
          const bAvailable = b.status === 'Available' ? 0 : 1
          if (aAvailable !== bAvailable) return aAvailable - bAvailable
          return (rentalCountByItemId.get(b.id) ?? 0) - (rentalCountByItemId.get(a.id) ?? 0)
        })
        break
    }

    return result
  }, [filteredItems, sortBy, rentalCountByItemId, referenceCity, distanceKm, itemDistances])

  const activeCategory = categories.find((c) => c.id === activeCategoryId)
  const activeSub = categories.find((c) => c.id === activeSubId)

  function handleOpenAddItem() {
    if (!isAuthenticated) {
      navigate('/login')
      return
    }
    setAddItemOpen(true)
  }

  function handleItemCreated(created) {
    setItems((prev) => [...prev, created])
  }

  function handleItemRented() {
    if (!rentItem) return
    setItems((prev) => prev.map((i) => (i.id === rentItem.id ? { ...i, status: 'Rented' } : i)))
  }

  function updateFilter(field, value) {
    setFilters((prev) => ({ ...prev, [field]: value }))
  }

  function resetFilters() {
    setFilters(emptyFilters)
  }

  return (
    <div className="page">
      <Splash />
      {saleMode ? (
        <section className="hero-banner">
          <h1>
            ככה קונים <mark>יד 2.</mark>
          </h1>

          <div className="trust-pillars">
            <div className="trust-pillar">
              <h2>
                בוחרים
                {' '}<br />
                פריט
              </h2>
              <PillarMark>
                <SearchGlyph className="gl gl-search" />
              </PillarMark>
              <p>
                ציוד צילום של צלמות
                <br className="br-d" /> מהקהילה, לפי
                <br className="br-d" /> קטגוריות.
              </p>
            </div>
            <div className="trust-pillar">
              <h2>
                ישירות
                {' '}<br />
                מצלמת
                {' '}<br />
                בקהילה
              </h2>
              <PillarMark>
                <UsersGlyph className="gl gl-users" />
              </PillarMark>
              <p>
                כל פריט מגיע מצלמת
                <br className="br-d" /> בקהילה שלנו - מה שמבטיח
                <br className="br-d" /> אמינות ומוצר מתוחזק.
              </p>
            </div>
            <div className="trust-pillar">
              <h2>
                שירות
                {' '}<br />
                משלוחים
                {' '}<br />
                אמין
              </h2>
              <PillarMark>
                <TruckGlyph className="gl gl-truck" />
              </PillarMark>
              <p>
                ניתן לבחור באיסוף עם
                <br className="br-d" /> משלוחן אמין וזול בהפנייה
                <br className="br-d" /> מהאתר.
              </p>
            </div>
          </div>
        </section>
      ) : (
        <section className="hero-banner">
          <h1>
            ככה שוכרים <mark>היום.</mark>
          </h1>
          <p className="hero-statement">
            הסוף לחיפוש פריטים בפורומים, הכל מרוכז במקום אחד ונותן לך מעטפת שלמה ליום הצילום שלך!
          </p>
          <p className="hero-lead">
            חדש מבית ONYX: אוסף נבחר של מצלמות, עדשות ואביזרים מקצועיים, ישירות מצלמות באזור שלך.
          </p>

          {/* שלושה עמודים: כותרת גדולה מימין, ומשמאלה טבעת פתוחה עם סימן המותג והאייקון שלו */}
          <div className="trust-pillars">
            <div className="trust-pillar">
              <h2>
                אימות
                {' '}<br />
                אישי
                {' '}<br />
                לפני כל
                {' '}<br />
                השכרה
              </h2>
              <PillarMark>
                <ShieldGlyph className="gl gl-shield" />
              </PillarMark>
              <p>
                כל השכרה מאושרת
                <br className="br-d" /> בשיחת טלפון ישירה מול
                <br className="br-d" /> המשכירה, לפני התשלום.
              </p>
            </div>
            <div className="trust-pillar">
              <h2>
                ישירות
                {' '}<br />
                מצלמת
                {' '}<br />
                בקהילה
              </h2>
              <PillarMark>
                <UsersGlyph className="gl gl-users" />
              </PillarMark>
              <p>
                כל פריט מגיע מצלמת
                <br className="br-d" /> בקהילה שלנו - מה שמבטיח
                <br className="br-d" /> אמינות ומוצר מתוחזק.
              </p>
            </div>
            <div className="trust-pillar">
              <h2>
                שירות
                {' '}<br />
                משלוחים
                {' '}<br />
                אמין
              </h2>
              <PillarMark>
                <TruckGlyph className="gl gl-truck" />
              </PillarMark>
              <p>
                ניתן לבחור באיסוף עם
                <br className="br-d" /> משלוחן אמין וזול בהפנייה
                <br className="br-d" /> מהאתר.
              </p>
            </div>
          </div>
        </section>
      )}

      <div className="market-switch">
        {saleMode ? (
          <Link to="/" className="btn btn-ghost market-switch-btn">
            ← לקטלוג ההשכרה
          </Link>
        ) : (
          <Link to="/yad2" className="market-switch-btn market-switch-yad2">
            מעבר ללוח יד 2
          </Link>
        )}
      </div>

      <h2 className="catalog-heading">{saleMode ? 'לוח יד 2 - ציוד צילום למכירה' : 'המוצר שחסר לך במרחק לחיצה!'}</h2>



      <div className="chip-row">
        <button
          type="button"
          className={`chip ${activeCategoryId === 'all' ? 'chip-active' : ''}`}
          style={{ '--i': 0 }}
          onClick={() => {
            setActiveCategoryId('all')
            setActiveSubId('')
          }}
        >
          הכל
        </button>
        {topCategories.map((category, index) => (
          <button
            key={category.id}
            type="button"
            className={`chip ${activeCategoryId === category.id ? 'chip-active' : ''}`}
            style={{ '--i': index + 1 }}
            onClick={() => {
              setActiveCategoryId(category.id)
              setActiveSubId('')
            }}
          >
            {category.name}
          </button>
        ))}
        <button type="button" className="suggest-category-link" onClick={() => setSuggestOpen(true)}>
          + הוספת קטגוריה
        </button>
      </div>
      {suggestOpen && <SuggestCategoryModal topCategories={topCategories} onClose={() => setSuggestOpen(false)} />}

      <div className="catalog-section">
        {/* אלמנט "עוקב" בלתי נראה - כשהוא יוצא מהתצוגה בגלילה, זה הרגע שבו שורת המיון בפועל נדבקת */}
        <div className="sticky-sentinel" ref={stickySentinelRef} />

        <div className={`search-filter-row ${toolbarStuck ? 'is-stuck' : ''}`} ref={toolbarRef}>
          <div className="search-wrap">
            <SearchIcon className="search-icon" />
            <input
              type="search"
              className="search-input"
              placeholder="חפשו..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <button
            type="button"
            className={`btn btn-ghost filter-toggle ${filtersOpen ? 'filter-toggle-active' : ''}`}
            onClick={() => setFiltersOpen((prev) => !prev)}
          >
            <FilterIcon className="filter-toggle-icon" />
            סינון
            {activeFilterCount > 0 && <span className="filter-count-badge">{activeFilterCount}</span>}
          </button>

          <label className={`availability-toggle ${filters.onlyAvailable ? 'availability-toggle-active' : ''}`}>
            <input
              type="checkbox"
              checked={filters.onlyAvailable}
              onChange={(e) => updateFilter('onlyAvailable', e.target.checked)}
            />
            זמינים בלבד
          </label>

          <SortDropdown options={SORT_OPTIONS} value={sortBy} onChange={setSortBy} />

          <div className="grid-zoom" title="גודל תצוגת הפריטים">
            <GridDenseIcon className="grid-zoom-icon" />
            <input
              type="range"
              className="grid-zoom-slider"
              min={ZOOM_MIN}
              max={ZOOM_MAX}
              step="1"
              value={zoomLevel}
              style={{ '--zoom-pos': `${zoomPos}%` }}
              onChange={(e) => setZoomLevel(Number(e.target.value))}
              aria-label="הקטנת או הגדלת גודל הפריטים בתצוגה"
            />
            <GridSparseIcon className="grid-zoom-icon" />
            <span className="grid-zoom-label">{zoomLabel}</span>
          </div>
        </div>
          {sortBy === 'proximity' && !user?.city && filters.city === 'all' && (
            <div className="my-city-bar">
              <label className="my-city-select">
              <span>העיר שלי:</span>
              <select value={myCity} onChange={(e) => chooseMyCity(e.target.value)}>
                <option value="">בחרי עיר</option>
                {catalogCities.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
              <button type="button" className="link-button my-location-btn" onClick={locateMe} disabled={locating === 'busy'}>
                {locating === 'busy' ? 'מאתרת...' : '📍 לפי המיקום שלי'}
              </button>
              {locating === 'failed' && <span className="payment-status-note">לא הצלחנו לאתר מיקום - בחרי עיר</span>}
            </label>
          </div>
          )}

        {filtersOpen && (
          <div className={`filters-panel ${toolbarStuck ? 'is-stuck' : ''}`}>
            <div className="filters-panel-field">
              <label htmlFor="priceMin">מחיר יומי מ-</label>
              <input
                id="priceMin"
                type="number"
                min="0"
                placeholder="0"
                value={filters.priceMin}
                onChange={(e) => updateFilter('priceMin', e.target.value)}
              />
            </div>

            <div className="filters-panel-field">
              <label htmlFor="priceMax">עד</label>
              <input
                id="priceMax"
                type="number"
                min="0"
                placeholder="ללא הגבלה"
                value={filters.priceMax}
                onChange={(e) => updateFilter('priceMax', e.target.value)}
              />
            </div>

            <div className="filters-panel-field">
              <label htmlFor="dateFrom">פנוי מתאריך</label>
              <input
                id="dateFrom"
                type="date"
                min={todayIsraelIso()}
                value={filters.dateFrom}
                onChange={(e) => {
                  const value = e.target.value
                  updateFilter('dateFrom', value)
                  if (value && (!filters.dateTo || filters.dateTo < value)) updateFilter('dateTo', value)
                }}
              />
            </div>

            <div className="filters-panel-field">
              <label htmlFor="dateTo">עד תאריך</label>
              <input
                id="dateTo"
                type="date"
                min={filters.dateFrom || todayIsraelIso()}
                value={filters.dateTo}
                onChange={(e) => updateFilter('dateTo', e.target.value)}
              />
            </div>

            <div className="filters-panel-field">
              <label htmlFor="cityFilter">עיר</label>
              <select id="cityFilter" value={filters.city} onChange={(e) => updateFilter('city', e.target.value)}>
                <option value="all">כל הערים</option>
                {cities.map((city) => (
                  <option key={city} value={city}>
                    {city}
                  </option>
                ))}
              </select>
            </div>

            {activeFilterCount > 0 && (
              <button type="button" className="btn btn-ghost btn-small" onClick={resetFilters}>
                איפוס
              </button>
            )}
          </div>
        )}

        {loading && <Loader label="טוען פריטים" />}
        {error && <p className="state-message state-error">{error}</p>}

        {/* "שיתוף ציוד חדש" - תמיד מרכוז במרכז, בכל הלשוניות (גם ב"הכל" - שם בוחרים קטגוריה בחלון) */}
        {!loading && !error && (
          <div className="share-row">
            {!isSearching && activeCategoryId !== 'all' && <h2>{[activeCategory?.name, activeSub?.name].filter(Boolean).join(' › ')}</h2>}
            {activeSubCategories.length > 0 && (
              <div className="subchip-row" role="group" aria-label={`תתי-קטגוריות של ${activeCategory?.name ?? ''}`}>
                <button
                  type="button"
                  className={`subchip ${activeSubId === '' ? 'subchip-active' : ''}`}
                  onClick={() => setActiveSubId('')}
                >
                  כל ה{activeCategory?.name}
                </button>
                {activeSubCategories.map((sub, index) => (
                  <button
                    key={sub.id}
                    type="button"
                    className={`subchip ${activeSubId === sub.id ? 'subchip-active' : ''}`}
                    style={{ '--i': index + 1 }}
                    onClick={() => setActiveSubId(sub.id)}
                  >
                    {sub.name}
                  </button>
                ))}
              </div>
            )}
            <button type="button" className="btn btn-primary" onClick={handleOpenAddItem}>
              {saleMode ? '+ פרסום פריט למכירה' : '+ שיתוף ציוד חדש'}
            </button>
          </div>
        )}

        {!loading && !error && sortedItems.length === 0 && (
          <p className="state-message">לא מצאנו בדיוק את זה… נסי סינון אחר</p>
        )}

        {!loading && !error && isSearching && sortedItems.length > 0 && (
          <p className="state-message">
            {sortedItems.length} תוצאות עבור "{search.trim()}"
          </p>
        )}

        {!loading && !error && sortedItems.length > 0 && (
          <div className="items-grid" style={{ '--cols': zoomColumns }}>
            {sortedItems.slice(0, visibleCount).map((item, index) => (
              <ItemCard
                key={item.id}
                item={item}
                categoryChains={categoryChains}
                rating={ratings[item.id]}
                style={{ '--stagger-index': index % 12 }}
                onClick={() => setRentItem(item)}
              />
            ))}
          </div>
        )}
        {!loading && !error && visibleCount < sortedItems.length && (
          <div ref={moreRef} className="items-more">
            <button type="button" className="btn btn-ghost" onClick={() => setVisibleCount((n) => n + PAGE_SIZE)}>
              להצגת עוד פריטים ({sortedItems.length - visibleCount})
            </button>
          </div>
        )}
        {loadingRest && <Loader label="טוען פריטים" compact />}
      </div>

      {!saleMode && (
        <section className="how-it-works">
          <h2>איך זה עובד?</h2>
          {/* גל של חצאי עיגולים לרוחב כל המסך (כמו בעיצוב של המעצבת). הסימנים עם האייקונים יושבים
              על הקו עצמו, במיקומים שנמדדו מהעיצוב */}
          <div className="how-it-works-stage">
            <svg className="how-it-works-wave" viewBox="0 0 852 260" aria-hidden="true" focusable="false">
              <path d={HOW_IT_WORKS_WAVE}>
                {!prefersReducedMotion && (
                  <animate
                    attributeName="d"
                    dur="9s"
                    repeatCount="indefinite"
                    values={HOW_IT_WORKS_WAVE_KEYFRAMES}
                  />
                )}
              </path>
            </svg>

            <BrandMark className="hiw-mark hiw-mark-decor">
              <ExtraCameraGlyph className="gl gl-camera2" />
            </BrandMark>

            <ol className="how-it-works-grid">
              <li className="how-it-works-step">
                <span className="how-it-works-number">1</span>
                <h3>חיפוש ובחירה</h3>
                <p>
                  עייני בקטלוג הציוד, סנני לפי מחיר וקרבה גיאוגרפית, ובחרי את הפריט שמתאים בדיוק ליום
                  הצילומים שלך.
                </p>
                <BrandMark className="hiw-mark">
                  <SearchGlyph className="gl gl-search" />
                </BrandMark>
              </li>
              <li className="how-it-works-step">
                <span className="how-it-works-number">2</span>
                <h3>אימות זמינות אישי</h3>
                <p>
                  עוד לפני שסוגרים, הצלמת שמשכירה את הפריט מאשרת בשיחה אישית שהוא אכן פנוי - להבטחת
                  זמינות.
                </p>
                <BrandMark className="hiw-mark">
                  <PhoneGlyph className="gl gl-phone" />
                </BrandMark>
              </li>
              <li className="how-it-works-step">
                <span className="how-it-works-number">3</span>
                <h3>תשלום גמיש ומאובטח</h3>
                <p>
                  מזומן באיסוף עצמי, העברה בנקאית ישירה או כרטיס אשראי באמצעות שער סליקה מאובטח -
                  הבחירה שלך.
                </p>
                <BrandMark className="hiw-mark">
                  <CardGlyph className="gl gl-card" />
                </BrandMark>
              </li>
              <li className="how-it-works-step">
                <span className="how-it-works-number">4</span>
                <h3>איסוף או משלוח</h3>
                <p>
                  תאמו איסוף עצמי - פרטי המשכיר נחשפים לכם ברגע שהתשלום הושלם, או קבלו משלוח עד הבית.
                </p>
                <BrandMark className="hiw-mark">
                  <BoxGlyph className="gl gl-box" />
                </BrandMark>
              </li>
            </ol>
          </div>
        </section>
      )}

      {addItemOpen && (
        <AddItemModal
          categoryId={activeSub?.id ?? activeCategory?.id ?? ''}
          categories={sortedCategories}
          ownerId={user.id}
          initialForSale={saleMode}
          onClose={() => setAddItemOpen(false)}
          onCreated={handleItemCreated}
        />
      )}

      {rentItem?.forSale && (
        <BuyModal key={rentItem.id} item={rentItem} categoryChains={categoryChains} onClose={() => setRentItem(null)} />
      )}

      {rentItem && !rentItem.forSale && (
        <RentModal
          key={rentItem.id}
          item={rentItem}
          initialStartDate={datesValid ? filters.dateFrom : undefined}
          initialReturnDate={datesValid ? filters.dateTo : undefined}
          initialStartTime={datesValid ? deepLinkTimes?.st : undefined}
          initialReturnTime={datesValid ? deepLinkTimes?.rt : undefined}
          initialQuantity={deepLinkTimes?.q}
          confirmedCallId={confirmedCallId}
          loginReturnTo={deepLinkReturnTo}
          onClose={() => {
            setRentItem(null)
            setConfirmedCallId(null)
            setDeepLinkTimes(null)
            setDeepLinkReturnTo(null)
          }}
          onRented={handleItemRented}
          categoryChains={categoryChains}
        />
      )}
    </div>
  )
}
