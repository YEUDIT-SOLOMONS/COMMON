import { pickIconKind } from '../utils/categoryIcon.js'

// אייקון לפריט ללא תמונה - מותאם לקטגוריה (מצלמה, עדשה, חצובה, מוניטור, עשן, מיקרופון, ניו בורן, חלאקה ועוד).
// אותו סגנון בדיוק כמו אייקון המצלמה: קווים בצבע הנוכחי, מילוי עדין.
const S = { stroke: 'currentColor', strokeWidth: 2.5, strokeLinecap: 'round', strokeLinejoin: 'round', fill: 'none' }
const SOFT = { fill: 'currentColor', opacity: 0.18 }

const GLYPHS = {
  camera: (
    <>
      <rect x="6" y="20" width="52" height="32" rx="6" {...SOFT} />
      <rect x="6" y="20" width="52" height="32" rx="6" {...S} />
      <rect x="22" y="12" width="18" height="10" rx="2.5" {...SOFT} />
      <rect x="22" y="12" width="18" height="10" rx="2.5" {...S} />
      <circle cx="32" cy="37" r="11" {...S} />
      <circle cx="32" cy="37" r="5" fill="currentColor" />
      <circle cx="47" cy="28" r="2" fill="currentColor" />
    </>
  ),
  lens: (
    <>
      <circle cx="32" cy="32" r="23" {...SOFT} />
      <circle cx="32" cy="32" r="23" {...S} />
      <circle cx="32" cy="32" r="14" {...S} />
      <circle cx="32" cy="32" r="6" fill="currentColor" />
      <path d="M19 20 L24 24" {...S} />
    </>
  ),
  tripod: (
    <>
      <rect x="22" y="6" width="20" height="12" rx="3" {...SOFT} />
      <rect x="22" y="6" width="20" height="12" rx="3" {...S} />
      <path d="M32 18 V34 M32 34 L14 58 M32 34 L50 58 M32 34 V58" {...S} />
    </>
  ),
  monitor: (
    <>
      <rect x="6" y="10" width="52" height="34" rx="4" {...SOFT} />
      <rect x="6" y="10" width="52" height="34" rx="4" {...S} />
      <path d="M32 44 V53 M20 55 H44" {...S} />
      <path d="M28 21 L40 27 L28 33 Z" fill="currentColor" />
    </>
  ),
  smoke: (
    <>
      <rect x="6" y="34" width="34" height="18" rx="4" {...SOFT} />
      <rect x="6" y="34" width="34" height="18" rx="4" {...S} />
      <rect x="40" y="39" width="10" height="8" rx="2" {...S} />
      <circle cx="54" cy="31" r="5" {...S} />
      <circle cx="47" cy="21" r="6" {...S} />
      <circle cx="58" cy="17" r="4" {...S} />
      <path d="M14 43 H26" {...S} />
    </>
  ),
  drone: (
    <>
      <ellipse cx="22" cy="13" rx="7" ry="2.2" {...SOFT} />
      <ellipse cx="22" cy="13" rx="7" ry="2.2" {...S} />
      <ellipse cx="42" cy="13" rx="7" ry="2.2" {...SOFT} />
      <ellipse cx="42" cy="13" rx="7" ry="2.2" {...S} />
      <path d="M26 26 L22 15 M38 26 L42 15" {...S} />
      <ellipse cx="9" cy="20" rx="8" ry="2.6" {...SOFT} />
      <ellipse cx="9" cy="20" rx="8" ry="2.6" {...S} />
      <ellipse cx="55" cy="20" rx="8" ry="2.6" {...SOFT} />
      <ellipse cx="55" cy="20" rx="8" ry="2.6" {...S} />
      <path d="M9 22.5 V29 H22 M55 22.5 V29 H42" {...S} />
      <rect x="22" y="25" width="20" height="11" rx="4.5" {...SOFT} />
      <rect x="22" y="25" width="20" height="11" rx="4.5" {...S} />
      <path d="M32 36 V39" {...S} />
      <circle cx="32" cy="45" r="6" {...SOFT} />
      <circle cx="32" cy="45" r="6" {...S} />
      <circle cx="32" cy="45" r="2.4" fill="currentColor" />
      <path d="M25 36 L21 52 M39 36 L43 52 M16 52 H26 M38 52 H48" {...S} />
    </>
  ),
  newborn: (
    <>
      <circle cx="32" cy="16" r="9" {...S} />
      <ellipse cx="32" cy="37" rx="20" ry="10" {...SOFT} />
      <ellipse cx="32" cy="37" rx="20" ry="10" {...S} />
      <path d="M32 47 C28 47 27 53 29 57 C31 59 33 59 35 57 C37 53 36 47 32 47 Z" fill="currentColor" />
      <circle cx="32" cy="37" r="3" fill="currentColor" />
    </>
  ),
  halaka: (
    <>
      <circle cx="17" cy="48" r="8" {...SOFT} />
      <circle cx="17" cy="48" r="8" {...S} />
      <circle cx="47" cy="48" r="8" {...SOFT} />
      <circle cx="47" cy="48" r="8" {...S} />
      <path d="M22 42 L46 8 M42 42 L18 8" {...S} />
      <circle cx="32" cy="29" r="2.5" fill="currentColor" />
    </>
  ),
  microphone: (
    <>
      <rect x="23" y="6" width="18" height="30" rx="9" {...SOFT} />
      <rect x="23" y="6" width="18" height="30" rx="9" {...S} />
      <path d="M14 29 C14 46 50 46 50 29 M32 43 V55 M22 57 H42" {...S} />
    </>
  ),
  speaker: (
    <>
      <rect x="14" y="5" width="36" height="54" rx="6" {...SOFT} />
      <rect x="14" y="5" width="36" height="54" rx="6" {...S} />
      <circle cx="32" cy="38" r="11" {...S} />
      <circle cx="32" cy="38" r="4" fill="currentColor" />
      <circle cx="32" cy="17" r="4" {...S} />
    </>
  ),
  light: (
    <>
      <path d="M32 6 C21 6 14 14 14 23 C14 30 18 34 22 39 V44 H42 V39 C46 34 50 30 50 23 C50 14 43 6 32 6 Z" {...SOFT} />
      <path d="M32 6 C21 6 14 14 14 23 C14 30 18 34 22 39 V44 H42 V39 C46 34 50 30 50 23 C50 14 43 6 32 6 Z" {...S} />
      <path d="M24 50 H40 M27 56 H37 M27 28 L32 34 L37 28" {...S} />
    </>
  ),
  backdrop: (
    <>
      <rect x="9" y="6" width="46" height="40" rx="3" {...SOFT} />
      <rect x="9" y="6" width="46" height="40" rx="3" {...S} />
      <path d="M9 38 C21 31 31 44 55 36 M22 46 V58 M42 46 V58" {...S} />
    </>
  ),
  bag: (
    <>
      <rect x="9" y="23" width="46" height="32" rx="6" {...SOFT} />
      <rect x="9" y="23" width="46" height="32" rx="6" {...S} />
      <path d="M21 23 V17 C21 11 25 8 32 8 C39 8 43 11 43 17 V23" {...S} />
      <rect x="27" y="33" width="10" height="9" rx="2" fill="currentColor" />
    </>
  ),
  battery: (
    <>
      <rect x="6" y="20" width="44" height="24" rx="4" {...SOFT} />
      <rect x="6" y="20" width="44" height="24" rx="4" {...S} />
      <rect x="50" y="28" width="7" height="8" rx="2" {...S} />
      <path d="M31 24 L24 33 H31 L27 41 L40 30 H33 Z" fill="currentColor" />
    </>
  ),
}

export function CategoryIcon({ texts, className }) {
  const kind = pickIconKind(texts ?? [])
  return (
    <svg className={className} viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true" data-kind={kind}>
      {GLYPHS[kind] ?? GLYPHS.camera}
    </svg>
  )
}
