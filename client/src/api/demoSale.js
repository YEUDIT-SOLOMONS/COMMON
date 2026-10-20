// מצב הדגמה מקומי ליד 2 (‎?demo=sale, נשאר פעיל בכל הלשוניות עד ‎?demo=off) - רק בהרצה מקומית. שום בקשה לא יוצאת לשרת ושום דבר לא נשמר
const FLAG = 'common_demo_sale'

function detect() {
  if (!import.meta.env.DEV || typeof window === 'undefined') return false
  const q = new URLSearchParams(window.location.search).get('demo')
  if (q === 'sale' || window.location.pathname === '/purchase/demo-sale') localStorage.setItem(FLAG, '1')
  if (q === 'off') {
    localStorage.removeItem(FLAG)
    if (localStorage.getItem('photorental.user')?.includes('demo-me')) {
      localStorage.removeItem('photorental.user')
      localStorage.removeItem('photorental.token')
    }
  }
  return localStorage.getItem(FLAG) === '1'
}

export const DEMO_SALE = detect()

const img = (emoji, bg) =>
  `data:image/svg+xml;utf8,${encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="400"><rect width="400" height="400" fill="${bg}"/><text x="200" y="250" font-size="160" text-anchor="middle">${emoji}</text></svg>`,
  )}`

const ME = {
  id: 'demo-me',
  name: 'יהודית (הדגמה)',
  email: 'demo@example.com',
  phone: '0500000000',
  role: 'Admin',
  city: 'בני ברק',
  address: 'רבי עקיבא 10',
  addressFloor: '2',
  addressApartment: 'כהן',
  commissionConsentAtUtc: '2026-01-01T00:00:00Z',
}

const CATEGORIES = [
  { id: 'c1', name: 'מצלמות', parentId: null, order: 1 },
  { id: 'c2', name: 'עדשות', parentId: null, order: 2 },
  { id: 'c3', name: 'תאורה', parentId: null, order: 3 },
]

const base = { status: 'Available', quantity: 1, minRentalUnit: 'Days', minRentalAmount: 1, imageCount: 1, createdAtUtc: '2026-10-10T10:00:00Z' }
let ITEMS = [
  { ...base, id: 'd1', name: 'Canon EOS R6', description: 'גוף בלבד, 15 אלף שאטרים, כולל 2 סוללות ומטען.', categoryId: 'c1', ownerId: 'u2', city: 'בני ברק', street: 'ז׳בוטינסקי', forSale: true, salePrice: 6200, saleCondition: 'LikeNew', imageDataUrl: img('📷', '#f6d6d6') },
  { ...base, id: 'd2', name: 'Sigma 24-70 f2.8 Art', description: 'עדשה במצב מצוין, עם פילטר UV ומכסים.', categoryId: 'c2', ownerId: 'u3', city: 'ירושלים', street: 'הרב בוקשפן', forSale: true, salePrice: 2900, saleCondition: 'Used', imageDataUrl: img('🔭', '#d6e8f6') },
  { ...base, id: 'd3', name: 'מבזק Godox V1', description: 'חדש באריזה, לא נפתח.', categoryId: 'c3', ownerId: 'demo-me', city: 'בני ברק', street: 'רבי עקיבא', forSale: true, salePrice: 850, saleCondition: 'New', imageDataUrl: img('⚡', '#e2f6d6') },
  { ...base, id: 'd4', name: 'Sony A7 III', description: 'פריט להשכרה רגיל - לדוגמה.', categoryId: 'c1', ownerId: 'u2', city: 'בני ברק', dailyPrice: 180, imageDataUrl: img('🎥', '#eee') },
  { ...base, id: 'd5', name: 'סופטבוקס 90 ס"מ', description: 'פריט להשכרה רגיל - לדוגמה.', categoryId: 'c3', ownerId: 'u3', city: 'ירושלים', dailyPrice: 40, imageDataUrl: img('💡', '#fff3c4') },
]

let SALE = {
  id: 'demo-sale',
  itemId: 'd1',
  itemName: 'Canon EOS R6',
  image: ITEMS[0].imageDataUrl,
  price: 6200,
  deliveryFee: 0,
  total: 6200,
  commission: 496,
  sellerAmount: 5704,
  deliveryMethod: 'SelfPickup',
  deliveryAddress: null,
  pickupAddress: 'ז׳בוטינסקי 40, בני ברק',
  otherName: 'רחלי (המוכרת)',
  otherPhone: '0501234567',
  trackingId: null,
  status: 'Paid',
  createdAtUtc: new Date().toISOString(),
  confirmUntilUtc: new Date(Date.now() + 3 * 86400000).toISOString(),
}

try {
  const saved = localStorage.getItem('common_demo_sale_state')
  if (saved) SALE = JSON.parse(saved)
} catch {
  // בלי אחסון - נשארים עם ברירת המחדל
}
const keep = () => {
  try {
    localStorage.setItem('common_demo_sale_state', JSON.stringify(SALE))
  } catch {
    // לא קריטי בהדגמה
  }
}

const ADMIN_ROWS = () => [
  { ...SALE, paymentMethod: SALE.paymentMethod ?? 'CreditCard', sellerName: 'רחלי כהן', sellerPhone: '0501234567', buyerName: 'יהודית (הדגמה)', buyerPhone: '0500000000', autoConfirmed: false, sellerPaidAtUtc: null },
  { id: 's2', paymentMethod: 'CreditCard', itemId: 'd2', itemName: 'Sigma 24-70 f2.8 Art', price: 2900, deliveryFee: 45, commission: 232, sellerAmount: 2668, deliveryMethod: 'Delivery', trackingId: 'GP-12345', status: 'Received', autoConfirmed: true, createdAtUtc: '2026-10-12T09:00:00Z', sellerName: 'מירי לוי', sellerPhone: '0527654321', buyerName: 'שרה', buyerPhone: '0541112222', sellerPaidAtUtc: null },
  { id: 's3', paymentMethod: 'Cash', itemId: 'd5', itemName: 'עדשת 50mm', price: 400, deliveryFee: 0, commission: 32, sellerAmount: 368, deliveryMethod: 'SelfPickup', status: 'Problem', problemText: 'העדשה הגיעה עם שריטה שלא צוינה', createdAtUtc: '2026-10-15T09:00:00Z', sellerName: 'דבורה', sellerPhone: '0533334444', buyerName: 'חני', buyerPhone: '0585556666', sellerPaidAtUtc: null },
]

if (DEMO_SALE) {
  const payload = btoa(JSON.stringify({ exp: 9999999999 })).replace(/=+$/, '')
  localStorage.setItem('photorental.token', `x.${payload}.x`)
  localStorage.setItem('photorental.user', JSON.stringify(ME))
}

const wait = (v) => new Promise((r) => setTimeout(() => r(v), 250))

export function demoRequest(path, { method = 'GET', body } = {}) {
  const p = path.split('?')[0]
  const m = method.toUpperCase()
  if (m === 'GET') {
    if (p === '/categories') return wait(CATEGORIES)
    if (p === '/items') {
      const owner = new URLSearchParams(path.split('?')[1]).get('ownerId')
      if (owner)
        return wait([
          ...ITEMS.filter((i) => i.ownerId === owner),
          { ...base, id: 'd9', name: 'עדשת Canon 50mm 1.8', ownerId: owner, categoryId: 'c2', forSale: true, salePrice: 350, saleCondition: 'Used', sold: true },
        ])
      return wait(ITEMS.filter((i) => !i.sold))
    }
    if (p === '/items/busy' || p === '/questions/mine' || p === '/availability-calls/mine') return wait([])
    if (p === '/sales/admin') return wait(ADMIN_ROWS())
    if (p === '/sales/mine')
      return wait([
        { id: 'demo-sale', role: 'Buyer', itemName: SALE.itemName, image: SALE.image, price: SALE.price, status: SALE.status, createdAtUtc: SALE.createdAtUtc, token: 'demo' },
        { id: 'demo-sale', role: 'Seller', itemName: 'מבזק Godox V1', image: ITEMS[2].imageDataUrl, price: 850, status: 'Received', createdAtUtc: '2026-10-14T10:00:00Z', token: 'seller' },
      ])
    if (p.startsWith('/sales/')) {
      const seller = path.includes('t=seller')
      return wait({ ...SALE, role: seller ? 'Seller' : 'Buyer', otherName: seller ? 'יהודית (הקונה)' : SALE.otherName, otherPhone: seller ? '0500000000' : SALE.otherPhone })
    }
    if (/^\/items\/[^/]+$/.test(p)) {
      const item = ITEMS.find((i) => i.id === p.split('/')[2])
      return wait(item ? { ...item, imageDataUrls: [item.imageDataUrl] } : null)
    }
    if (p === '/order-payments/status') return wait({ enabled: true })
    if (p === '/addresses/cities' || p === '/addresses/streets') {
      const q = new URLSearchParams(path.split('?')[1]).get('q') ?? ''
      const list = p === '/addresses/cities'
        ? ['בני ברק', 'ירושלים', 'אלעד', 'בית שמש', 'מודיעין עילית', 'ביתר עילית', 'פתח תקווה']
        : ['רבי עקיבא', 'ז׳בוטינסקי', 'הרב כהנמן', 'חזון איש', 'השומר', 'בירנבוים']
      return wait(list.filter((x) => x.includes(q.trim())))
    }
    if (/^\/items\/[^/]+\/owner-payout$/.test(p))
      return wait({ isConfigured: true, accountHolderName: 'רחלי כהן', bankName: 'לאומי', branchNumber: '800', accountNumber: '123456' })
    if (/^\/users\/[^/]+\/payout$/.test(p)) return wait({ isConfigured: true })
    if (/^\/users\/[^/]+$/.test(p)) return wait(ME)
    if (p === '/loans/popularity' || p === '/reviews/summary' || p === '/cities/coords') return wait({})
    return wait(null)
  }
  if (p === '/sales/quotes') return wait({ fee: 45 })
  if (p === '/sales' && m === 'POST') {
    const item = ITEMS.find((i) => i.id === body?.itemId) ?? ITEMS[0]
    SALE = { ...SALE, itemId: item.id, itemName: item.name, image: item.imageDataUrl, price: item.salePrice, deliveryFee: 0, total: item.salePrice,
      commission: +(item.salePrice * 0.05).toFixed(2), sellerAmount: +(item.salePrice * 0.95).toFixed(2), deliveryMethod: 'SelfPickup',
      deliveryAddress: null, trackingId: null, status: 'Paid', paymentMethod: body?.paymentMethod ?? 'Cash' }
    keep()
    return wait({ id: 'demo-sale', token: 'demo' })
  }
  if (p === '/order-payments') {
    const sale = body?.draft ? JSON.parse(body.draft).sale : null
    const item = ITEMS.find((i) => i.id === sale?.itemId) ?? ITEMS[0]
    const fee = sale?.deliveryMethod === 'Delivery' ? 45 : 0
    SALE = { ...SALE, itemId: item.id, itemName: item.name, image: item.imageDataUrl, price: item.salePrice, deliveryFee: fee, total: item.salePrice + fee,
      commission: +(item.salePrice * 0.05).toFixed(2), sellerAmount: +(item.salePrice * 0.95).toFixed(2), deliveryMethod: sale?.deliveryMethod ?? 'SelfPickup',
      deliveryAddress: fee ? `${sale.deliveryAddress.street} ${sale.deliveryAddress.houseNumber}, ${sale.deliveryAddress.city}` : null, trackingId: fee ? 'GP-DEMO' : null, status: 'Paid', paymentMethod: 'CreditCard' }
    keep()
    window.alert('הדגמה: כאן הייתה נפתחת עמוד התשלום באשראי. נדלג ישר לדף הרכישה.')
    return wait({ redirectUrl: '/purchase/demo-sale?t=demo' })
  }
  if (p === '/sales/demo-sale/handover') {
    const delivered = path.includes('delivered=true')
    SALE = { ...SALE, handedOverAtUtc: delivered ? new Date().toISOString() : null, notHandedAtUtc: delivered ? null : new Date().toISOString() }
    keep()
    return wait({ ...SALE, role: 'Seller', otherName: 'יהודית (הקונה)', otherPhone: '0500000000' })
  }
  if (p === '/sales/demo-sale/receipt') {
    SALE = { ...SALE, status: 'Received' }
    keep()
    return wait({ ...SALE, role: 'Buyer' })
  }
  if (p === '/sales/demo-sale/problems') {
    SALE = { ...SALE, status: 'Problem' }
    keep()
    return wait({ ...SALE, role: 'Buyer' })
  }
  if (p === '/items' && m === 'POST') {
    const created = { ...base, ...body, id: `n${Date.now()}`, imageDataUrl: body.imageDataUrls?.[0] ?? img('📦', '#eee') }
    ITEMS = [created, ...ITEMS]
    return wait(created)
  }
  if (p === '/questions') return wait({ id: 'demo-q', token: 'demo' })
  return wait({})
}
