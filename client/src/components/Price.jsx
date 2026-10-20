import { beforeVat } from '../utils/vat.js'

// מחיר לשוכרות: בפריט עם מע"מ נוסף לפני המחיר הרגיל פירוט קטן "X + מע"מ · סה"כ", והמחיר עצמו נשאר בעיצוב הרגיל
export function Price({ amount, vat, unit = ' / יום', compact = false }) {
  return (
    <>
      {vat && !compact && <span className="price-vat-base">₪{beforeVat(amount)} + מע״מ · סה״כ </span>}
      ₪{amount}
      {unit && <span className="item-price-unit">{unit}</span>}
      {vat && compact && <span className="price-vat-note"> כולל מע״מ</span>}
    </>
  )
}
