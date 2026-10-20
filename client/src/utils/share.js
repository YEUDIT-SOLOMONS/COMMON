const SITE_PITCH = 'גיליתי את קומון - קהילת צלמות שמשכירות ציוד צילום אחת לשנייה 📷'

export function whatsappLink(text) {
  return `https://wa.me/?text=${encodeURIComponent(text)}`
}

export function siteShareText() {
  return `${SITE_PITCH} ${window.location.origin}`
}

// בטלפון - חלון השיתוף של המכשיר; במחשב - וואטסאפ
export async function shareItem(item) {
  // /i/{id} מחזיר תצוגה מקדימה לוואטסאפ (תמונה, שם ומחיר) ומעביר לחלון הפריט
  const url = `${window.location.origin}/i/${encodeURIComponent(item.id)}`
  const text = `ראי מה מצאתי בקומון: ${item.name}`
  if (navigator.share) {
    try {
      await navigator.share({ title: item.name, text, url })
      return
    } catch (err) {
      if (err?.name === 'AbortError') return
    }
  }
  window.open(whatsappLink(`${text} ${url}`), '_blank', 'noopener')
}
