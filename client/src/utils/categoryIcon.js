// בחירת אייקון לפריט שאין לו תמונה, לפי הקטגוריה שלו (ותת-הקטגוריה), ובהיעדר התאמה - לפי שם הפריט.
// הקטגוריות נערכות במסך הניהול, ולכן ההתאמה היא לפי מילות מפתח בשם (עברית ואנגלית) ולא לפי מזהים קבועים.
// הסדר חשוב: "עדשות למצלמה" צריך להיות עדשה ולא מצלמה, "חצובות למצלמה" - חצובה.
const RULES = [
  ['speaker', ['רמקול', 'speaker']],
  ['microphone', ['מיקרופון', 'סאונד', 'אודיו', 'הקלט', 'mic', 'sound', 'audio']],
  ['lens', ['עדש', 'lens']],
  ['tripod', ['חצוב', 'מונופוד', 'סטנד', 'tripod']],
  ['monitor', ['מוניטור', 'מסך', 'monitor']],
  ['smoke', ['עשן', 'ערפל', 'smoke', 'fog']],
  ['drone', ['רחפן', 'drone']],
  ['newborn', ['ניו בורן', 'ניובורן', 'ינוק', 'תינוק', 'newborn']],
  ['halaka', ['חלאק', 'מספריים', 'halak']],
  ['light', ['תאור', 'פלאש', 'סופטבוקס', 'led', 'flash', 'light']],
  ['backdrop', ['רקע', 'backdrop']],
  ['bag', ['תיק', 'נשיאה', 'bag']],
  ['battery', ['סוללה', 'מטען', 'זיכרון', 'battery']],
  ['camera', ['מצלמ', 'וידאו', 'camera', 'video']],
]

// texts: שמות לפי סדר עדיפות - קודם תת-קטגוריה, אחריה קטגוריה ראשית, ובסוף שם הפריט
export function pickIconKind(texts) {
  for (const text of texts) {
    const lower = String(text ?? '').toLowerCase()
    if (!lower) continue
    const match = RULES.find(([, words]) => words.some((word) => lower.includes(word)))
    if (match) return match[0]
  }
  return 'camera'
}

// מזהה קטגוריה -> [שם הקטגוריה, שם קטגוריית האב (אם יש)]
export function buildCategoryChains(categories) {
  const byId = new Map(categories.map((category) => [category.id, category]))
  return new Map(
    categories.map((category) => [
      category.id,
      [category.name, category.parentId ? byId.get(category.parentId)?.name : undefined],
    ]),
  )
}

export function iconTextsForItem(item, chainById) {
  const chain = (chainById && item?.categoryId && chainById.get(item.categoryId)) || []
  return [...chain, item?.name]
}

export function categoryTrailForItem(item, chainById) {
  const [name, parentName] = (chainById && item?.categoryId && chainById.get(item.categoryId)) || []
  if (!name) return ''
  return parentName ? `${parentName} · ${name}` : name
}
