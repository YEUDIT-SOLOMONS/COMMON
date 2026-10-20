import { api } from './client.js'

// אימות
export const authApi = {
  register: (dto) => api.post('/auth/register', dto, { auth: false }),
  login: (dto) => api.post('/auth/login', dto, { auth: false }),
  // התחברות/הרשמה אוטומטית עם Google - idToken מתקבל מ-Google Identity Services בדפדפן (ראו GoogleSignInButton.jsx)
  google: (idToken) => api.post('/auth/google', { idToken }, { auth: false }),
  forgotPassword: (email) => api.post('/auth/forgot-password', { email }, { auth: false }),
  resetPassword: (dto) => api.post('/auth/reset-password', dto, { auth: false }),
}

// קטגוריות
// סקירת מנהלת: מספרים כלליים ומעקב משתמשים (מנהלות בלבד)
// "חלון הסימון" של המשכירה (קישור במייל, בלי התחברות): הטוקן הסודי t נשלח בכל בקשה
export const loanActionsApi = {
  get: (id, t) => api.get(`/loan-actions/${id}?t=${encodeURIComponent(t)}`),
  pickedUp: (id, t) => api.post(`/loan-actions/${id}/pickup?t=${encodeURIComponent(t)}`),
  returned: (id, t) => api.post(`/loan-actions/${id}/return-confirmation?t=${encodeURIComponent(t)}`),
  notReturned: (id, t) => api.post(`/loan-actions/${id}/non-return-reports?t=${encodeURIComponent(t)}`),
  paymentReceived: (id, t) => api.post(`/loan-actions/${id}/payment-confirmation?t=${encodeURIComponent(t)}`),
  proof: (id, t) => api.get(`/loan-actions/${id}/payment-proof?t=${encodeURIComponent(t)}`),
  extension: (id, t, a) => api.post(`/loan-actions/${id}/extension?t=${encodeURIComponent(t)}&a=${a}`),
}

export const availabilityAnswersApi = {
  get: (id, t) => api.get(`/availability-answers/${id}?t=${encodeURIComponent(t)}`),
  answer: (id, t, a) => api.post(`/availability-answers/${id}?t=${encodeURIComponent(t)}&a=${a}`),
}

export const renterPickupApi = {
  get: (id, t) => api.get(`/renter-pickup/${id}?t=${encodeURIComponent(t)}`),
  mark: (id, t) => api.post(`/renter-pickup/${id}?t=${encodeURIComponent(t)}`),
}

// "חלון זמין / להסרה" של בעלת הפריט (קישורים במייל "הפריט לא הושכר 6 שבועות", בלי התחברות): הטוקן t נשלח בכל בקשה
export const itemActionsApi = {
  get: (id, t) => api.get(`/item-actions/${id}?t=${encodeURIComponent(t)}`),
  available: (id, t) => api.post(`/item-actions/${id}/availability-confirmation?t=${encodeURIComponent(t)}`),
  remove: (id, t) => api.post(`/item-actions/${id}/removal?t=${encodeURIComponent(t)}`),
}

// "חלון העלאת אסמכתא" מהקישור במייל דוח העמלה החודשי (בלי התחברות): o=בעלים, m=חודש, t=טוקן סודי
const proofQuery = (o, m, t) =>
  `o=${encodeURIComponent(o)}&m=${encodeURIComponent(m)}&t=${encodeURIComponent(t)}`
export const commissionProofApi = {
  get: (o, m, t) => api.get(`/commission-proof?${proofQuery(o, m, t)}`),
  submit: (o, m, t, dto) => api.post(`/commission-proof?${proofQuery(o, m, t)}`, dto),
}

// תשלום עמלה באשראי (סאמיט): start יוצר דף תשלום חדש ומחזיר את כתובתו; confirm מאמת את התשלום אחרי החזרה
export const commissionPayApi = {
  start: (o, m, t, consent) => api.post(`/commission-payments?${proofQuery(o, m, t)}&consent=${consent ? 'true' : 'false'}`, {}),
  confirm: (paymentId, externalIdentifier) => api.post('/commission-payments/confirmation', { paymentId, externalIdentifier }),
  email: (o, m, t) => api.post(`/commission-payments/emails?${proofQuery(o, m, t)}`, {}),
}

// השלמת כתובות לפי מאגר היישובים והרחובות הרשמי
export const shippingPayApi = {
  status: () => api.get('/shipping-payments/status'),
  start: (amount, consent, draft) =>
    api.post('/shipping-payments', { amount, consent, draft: draft ? JSON.stringify(draft.draft) : null, isBatch: Boolean(draft?.isBatch) }),
  confirm: (paymentId, externalIdentifier) => api.post('/shipping-payments/confirmation', { paymentId, externalIdentifier }),
}

export const callsApi = {
  commonNumber: () => api.get('/calls/common-number'),
  connect: (loanId) => api.post('/calls/connections', { loanId }),
}

export const addressApi = {
  cities: (q) => api.get(`/addresses/cities?q=${encodeURIComponent(q)}`),
  streets: (city, q) => api.get(`/addresses/streets?city=${encodeURIComponent(city)}&q=${encodeURIComponent(q)}`),
}

export const adminApi = {
  overview: () => api.get('/admin/overview'),
  calls: () => api.get('/admin/calls'),
  resendCall: (id) => api.post(`/admin/calls/${id}/resends`),
  cancelCall: (id) => api.post(`/admin/calls/${id}/cancellation`),
  resendAvailability: (id, call, email) => api.post(`/admin/calls/${id}/availability-resends`, { call, email }),
  payouts: () => api.get('/admin/payouts'),
  markPayoutPaid: (ownerId, loanIds, proof) =>
    api.post('/admin/payouts/payments', { ownerId, loanIds, proofDataUrl: proof?.dataUrl, proofFileName: proof?.fileName }),
  payoutProof: (loanId) => api.get(`/admin/payouts/${loanId}/proof`),
  visits: () => api.get('/admin/visits'),
  neighborhoods: () => api.get('/admin/neighborhoods'),
  items: () => api.get('/admin/items'),
  questions: () => api.get('/admin/questions'),
  adminQuestionMessage: (id, text) => api.post(`/questions/${id}/admin-messages`, { text }),
  editQuestionMessage: (id, index, text) => api.put(`/admin/questions/${id}/messages/${index}`, { text }),
  setItemCategory: (id, categoryId, extraCategoryId) =>
    api.put(`/admin/items/${id}/category`, { categoryId, extraCategoryId: extraCategoryId || null }),
  setNeighborhood: (city, street, neighborhood) => api.put('/admin/neighborhoods', { city, street, neighborhood }),
  favorites: () => api.get('/admin/favorites'),
  loans: () => api.get('/admin/loans'),
  deleteLoan: (id) => api.del(`/admin/loans/${id}`),
}

// עמלת שירות: הדוח של המשכירה המחוברת + ניהול הגבייה למנהלות (month = "yyyy-MM", ריק = החודש הנוכחי)
export const commissionsApi = {
  debt: () => api.get('/commissions/debt'),
  my: (month) => api.get(month ? `/commissions/my?month=${encodeURIComponent(month)}` : '/commissions/my'),
  adminOverview: (month) =>
    api.get(month ? `/commissions/admin?month=${encodeURIComponent(month)}` : '/commissions/admin'),
  adminSetPaid: (dto) => api.put('/commissions/admin/paid', dto),
  adminSend: (dto) => api.post('/commissions/admin/notifications', dto),
  adminProof: (ownerId, month) =>
    api.get(`/commissions/admin/proof?ownerId=${encodeURIComponent(ownerId)}&month=${encodeURIComponent(month)}`),
  adminPreview: (ownerId, month) =>
    api.get(`/commissions/admin/preview?ownerId=${encodeURIComponent(ownerId)}&month=${encodeURIComponent(month)}`),
}

export const categoriesApi = {
  getAll: () => api.get('/categories'),
  getById: (id) => api.get(`/categories/${id}`),
  create: (dto) => api.post('/categories', dto),
  update: (id, dto) => api.put(`/categories/${id}`, dto),
  // שינוי סדר ההצגה בקטלוג: { ids: [...] } לפי הסדר הרצוי (מנהלות בלבד)
  reorder: (dto) => api.put('/categories/order', dto),
  remove: (id) => api.del(`/categories/${id}`),
}

// משתמשים
export const usersApi = {
  getById: (id) => api.get(`/users/${id}`),
  // שמירת כתובת מגורים ועיר עבור משכיר - נדרש פעם אחת לפני הוספת הפריט הראשון להשכרה, וניתן
  // לעדכן גם בהמשך (למשל מהאזור האישי)
  updateAddress: (id, dto) => api.patch(`/users/${id}/address`, dto),
  // עדכון פרטים אישיים (שם/טלפון) מהאזור האישי
  updateProfile: (id, dto) => api.patch(`/users/${id}/profile`, dto),
  // אישור תנאי עמלת השירות (פעם אחת, בעת הוספת הפריט הראשון)
  acceptCommission: (id) => api.patch(`/users/${id}/commission-consent`),
  acceptSaleCommission: (id) => api.patch(`/users/${id}/sale-commission-consent`),
  // פרטי חשבון בנק לקבלת תשלומים (משכיר בלבד, אך זמין לכל משתמש למקרה שירצה לשתף ציוד בעתיד)
  getPayout: (id) => api.get(`/users/${id}/payout`),
  updatePayout: (id, dto) => api.patch(`/users/${id}/payout`, dto),
}

// פריטי ציוד
export const itemsApi = {
  getAll: (categoryId) =>
    api.get(categoryId ? `/items?categoryId=${encodeURIComponent(categoryId)}` : '/items'),
  getByOwner: (ownerId) => api.get(`/items?ownerId=${encodeURIComponent(ownerId)}`),
  getPage: ({ page, pageSize, categoryId, sort }) =>
    api.get(`/items/page?${new URLSearchParams({ page, pageSize, sort, ...(categoryId ? { categoryId } : {}) })}`),
  // "פנוי בתאריכים שלי": מזהי הפריטים התפוסים בטווח
  busy: (from, to) => api.get(`/items/busy?from=${from}&to=${to}`, { auth: false }),
  cityCoords: (names) => api.get(`/cities/coords?names=${encodeURIComponent(names.join('|'))}`, { auth: false }),
  distances: ({ lat, lng, city }) =>
    api.get(
      `/cities/distances?${new URLSearchParams(
        Object.fromEntries(Object.entries({ lat, lng, city }).filter(([, v]) => v != null && v !== '')),
      )}`,
    ),
  schedule: (id) => api.get(`/items/${encodeURIComponent(id)}/schedule`),
  addBlock: (id, dto) => api.post(`/items/${encodeURIComponent(id)}/blocks`, dto),
  deleteBlock: (id, blockId) => api.del(`/items/${encodeURIComponent(id)}/blocks/${encodeURIComponent(blockId)}`),
  getById: (id) => api.get(`/items/${id}`),
  // הכתובת המדויקת של הפריט - נדרשת בנפרד, ורק כאשר השוכר בוחר באיסוף עצמי (לא כלולה ב-getById)
  getAddress: (id) => api.get(`/items/${id}/address`),
  // פרטי חשבון הבנק של בעל הפריט - לתשלום בהעברה בנקאית
  getOwnerPayout: (id) => api.get(`/items/${id}/owner-payout`),
  create: (dto) => api.post('/items', dto),
  update: (id, dto) => api.put(`/items/${id}`, dto),
  remove: (id) => api.del(`/items/${id}`),
}

// עסקאות השכרה
export const loansApi = {
  getAll: () => api.get('/loans'),
  // כמה פעמים כל פריט הושכר (itemId -> מספר) - פתוח לכולן, למיון "המבוקשים בקהילה"
  popularity: () => api.get('/loans/popularity'),
  // השכרות על הציוד שלי (משכירה)
  getIncoming: (ownerId, limit) => api.get(`/loans/incoming/${ownerId}${limit ? `?limit=${limit}` : ''}`),
  // אסמכתת ההעברה הבנקאית של השכרה: { fileName, contentType, dataUrl }
  getPaymentProof: (id) => api.get(`/loans/${id}/payment-proof`),
  getPayoutProof: (id) => api.get(`/loans/${id}/payout-proof`),
  ownerSummary: (ownerId) => api.get(`/loans/owner-summary/${ownerId}`),
  // מצב משלוח חי (סטטוס, זמן הגעה, שליח, מיקום): { outbound, return }
  getShippingStatus: (id) => api.get(`/loans/${id}/shipping-status`),
  // מסך "ההזמנה בוצעה": כל ההשכרות של אותה הזמנה
  getOrder: (id) => api.get(`/loans/${id}/order`),
  // "התחרטתי": שינוי אופן ההחזרה באמצע ההשכרה { returnMethod, returnPickupAddress }
  changeReturnMethod: (id, dto) => api.put(`/loans/${id}/return-method`, dto),
  getByUser: (userId, limit) => api.get(`/loans/user/${userId}${limit ? `?limit=${limit}` : ''}`),
  // הצעת מחיר משלוח אמיתית (רגיל/אקספרס) לכתובת מסירה נתונה - נקרא לפני אימות הזמינות כדי להציג
  // ללקוח את המחיר האמיתי (ראו RentModal.jsx)
  getShippingQuote: (dto) => api.post('/loans/shipping-quotes', dto),
  create: (dto) => api.post('/loans', dto),
  // פתיחת הזמנה אחת עם כמה פריטים מאותו חבר קהילה (משכיר) בבת אחת - עלות משלוח/שילוח נספרים פעם אחת בלבד
  createBatch: (dto) => api.post('/loans/batch', dto),
  return: (id) => api.post(`/loans/${id}/return-confirmation`),
  cancel: (id) => api.post(`/loans/${id}/cancellation`),
  markPickedUp: (id) => api.post(`/loans/${id}/pickup`),
  // אישור קבלת תשלום ידני (מזומן/העברה בנקאית) ע"י המשכיר - פותח את חשיפת פרטי הקשר שלו לשוכר
  confirmPayment: (id) => api.post(`/loans/${id}/payment-confirmation`),
}

// שיחות אימות זמינות מדומות (IVR) - ראו הערה מפורטת בשרת ב-SimulatedAvailabilityCallService
export const availabilityCallsApi = {
  start: (itemId, details = {}) => api.post('/availabilitycalls', { itemId, ...details }),
  recent: (itemId, details = {}) => api.post('/availabilitycalls/recent', { itemId, ...details }),
  getStatus: (callId) => api.get(`/availabilitycalls/${callId}`),
  // הדמיית לחיצת המשכיר על הטלפון: digit=1 (פנוי) / digit=2 (תפוס)
  respond: (callId, digit) => api.post(`/availabilitycalls/${callId}/responses`, { digit }),
  request: (requestId) => api.get(`/availabilitycalls/requests/${requestId}`),
  mine: () => api.get('/availabilitycalls/mine'),
  cancelRequest: (requestId) => api.post(`/availabilitycalls/requests/${requestId}/cancellation`),
  retryLater: (callId, notify) => api.post(`/availabilitycalls/${callId}/retries${notify ? `?notify=${notify}` : ''}`),
  cancelRetry: (retryId) => api.del(`/availabilitycalls/retries/${retryId}`),
}

// סליקת אשראי מדומה מול UPAY - ראו הערה מפורטת בשרת ב-SimulatedUpayPaymentService
export const salesApi = {
  quote: (itemId, deliveryAddress) => api.post('/sales/quotes', { itemId, deliveryAddress }),
  mine: () => api.get('/sales/mine'),
  create: (dto) => api.post('/sales', dto),
  get: (id, token) => api.get(`/sales/${id}${token ? `?t=${encodeURIComponent(token)}` : ''}`),
  handed: (id, token, delivered) =>
    api.post(`/sales/${id}/handover?${token ? `t=${encodeURIComponent(token)}&` : ''}delivered=${delivered ? 'true' : 'false'}`),
  received: (id, token) => api.post(`/sales/${id}/receipt${token ? `?t=${encodeURIComponent(token)}` : ''}`),
  problem: (id, token, text) => api.post(`/sales/${id}/problems${token ? `?t=${encodeURIComponent(token)}` : ''}`, { text }),
  adminList: () => api.get('/sales/admin'),
  adminSellerPaid: (id) => api.post(`/sales/admin/${id}/seller-payment`),
  adminRelease: (id) => api.post(`/sales/admin/${id}/receipt`),
}

export const orderPayApi = {
  status: () => api.get('/order-payments/status'),
  start: (amount, saveCard, draft) =>
    api.post('/order-payments', { amount, consent: saveCard, draft: draft ? JSON.stringify(draft.draft) : null, isBatch: Boolean(draft?.isBatch) }),
  confirm: (paymentId, externalIdentifier) => api.post('/order-payments/confirmation', { paymentId, externalIdentifier }),
}

// פניות מדף "יצירת קשר"
export const questionsApi = {
  ask: (itemId, text, images = []) => api.post('/questions', { itemId, text, images }),
  mine: () => api.get('/questions/mine'),
  get: (id, token) => api.get(`/questions/${id}${token ? `?t=${encodeURIComponent(token)}` : ''}`),
  reply: (id, token, text, images = []) =>
    api.post(`/questions/${id}/messages${token ? `?t=${encodeURIComponent(token)}` : ''}`, { text, images }),
}

export const contactApi = {
  send: (dto) => api.post('/contact', dto, { auth: false }),
}

export const favoritesApi = {
  mine: () => api.get('/favorites'),
  update: (itemId, dto) => api.put(`/favorites/${encodeURIComponent(itemId)}`, dto),
}

export const reviewsApi = {
  forItem: (itemId) => api.get(`/reviews/item/${encodeURIComponent(itemId)}`),
  summary: () => api.get('/reviews/summary'),
  pending: () => api.get('/reviews/pending'),
  thanks: () => api.get('/reviews/thanks'),
  submit: (loanId, dto) => api.post(`/reviews/${encodeURIComponent(loanId)}`, dto),
  dismiss: (loanId) => api.post(`/reviews/${encodeURIComponent(loanId)}/dismissal`, {}),
  adminList: () => api.get('/reviews/admin'),
  setHidden: (reviewId, hidden) => api.put(`/reviews/admin/${encodeURIComponent(reviewId)}/hidden`, { hidden }),
}
