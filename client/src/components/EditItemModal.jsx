import { useState } from 'react'
import { itemsApi } from '../api/endpoints.js'
import { ImagePicker } from './ImagePicker.jsx'
import { Modal } from './Modal.jsx'
import { beforeVat, VAT_PERCENT, withVat } from '../utils/vat.js'
import { QuantityField } from './QuantityField.jsx'
import { ExtraCategoryField } from './ExtraCategoryField.jsx'

// עריכת פריט קיים ("עריכת פריט" באזור האישי): אותם שדות כמו בשיתוף פריט חדש, מלאים בערכים הנוכחיים.
export function EditItemModal({ item, categories, ownerId, onClose, onSaved }) {
  // קטגוריה ראשית + תת-קטגוריה אופציונלית (הפריט שמור על הקטגוריה הספציפית ביותר)
  const currentCategory = categories.find((c) => c.id === item.categoryId)
  const [parentId, setParentId] = useState(currentCategory?.parentId || item.categoryId || '')
  const [subId, setSubId] = useState(currentCategory?.parentId ? item.categoryId : '')
  const topCategories = categories.filter((c) => !c.parentId)
  const subCategories = categories.filter((c) => c.parentId && c.parentId === parentId)
  // סטודיו/לוקיישן: מחיר לשעה ומחיר ליום שלם, מינימום בשעות
  // שיוך לקטגוריה נוספת - רק בקטגוריות שבהן פריט מתאים לעיתים קרובות לכמה מקומות
  const allowsExtraCategory = ['ניו בורן', 'לסטודיו ולחוץ', 'ציוד להפקות'].some((n) =>
    categories.find((c) => c.id === parentId)?.name?.includes(n),
  )
  const forSale = Boolean(item.forSale)
  const [salePrice, setSalePrice] = useState(String(item.salePrice || ''))
  const [saleCondition, setSaleCondition] = useState(item.saleCondition || 'Used')
  const isLocationCategory = !forSale && [subId || parentId, parentId].some((id) => categories.find((c) => c.id === id)?.name?.includes('לוקיישן'))
  const [form, setForm] = useState({
    name: item.name ?? '',
    description: item.description ?? '',
    dailyPrice: String(item.addVat ? beforeVat(item.dailyPrice) : (item.dailyPrice ?? '')),
  })
  const [images, setImages] = useState(
    item.imageDataUrls?.length ? item.imageDataUrls : item.imageDataUrl ? [item.imageDataUrl] : [],
  )

  const [advancedOpen, setAdvancedOpen] = useState(
    Boolean(item.hourlyPrice) || (item.minRentalAmount ?? 1) > 1 || item.minRentalUnit === 'Hours',
  )
  const [hourlyEnabled, setHourlyEnabled] = useState(Boolean(item.hourlyPrice))
  const [hourlyPrice, setHourlyPrice] = useState(
    item.hourlyPrice ? String(item.addVat ? beforeVat(item.hourlyPrice) : item.hourlyPrice) : '',
  )
  const [addVat, setAddVat] = useState(Boolean(item.addVat))
  const [quantity, setQuantity] = useState(Math.max(1, item.quantity || 1))
  const [extraCategoryId, setExtraCategoryId] = useState(item.extraCategoryId || '')
  const [minRentalUnit, setMinRentalUnit] = useState(item.minRentalUnit ?? 'Days')
  const [minRentalAmount, setMinRentalAmount] = useState(String(item.minRentalAmount ?? 1))

  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  function updateForm(field) {
    return (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }))
  }

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')

    if (forSale && !(Number(salePrice) > 0)) {
      setError('יש להזין מחיר מכירה')
      return
    }

    if (isLocationCategory && !hourlyPrice) {
      setError('יש להזין מחיר לשעה')
      return
    }

    if (hourlyEnabled && !hourlyPrice) {
      setError('יש להזין מחיר לשעה, או לבטל את אפשרות התמחור השעתי')
      return
    }

    setSubmitting(true)
    try {
      const updated = await itemsApi.update(item.id, {
        name: form.name,
        description: form.description,
        dailyPrice: forSale ? 0 : Number(form.dailyPrice),
        hourlyPrice: !forSale && (hourlyEnabled || isLocationCategory) && hourlyPrice ? Number(hourlyPrice) : null,
        addVat: !forSale && addVat,
        forSale,
        salePrice: forSale ? Number(salePrice) : 0,
        saleCondition: forSale ? saleCondition : null,
        quantity: isLocationCategory || forSale ? 1 : quantity,
        minRentalUnit: isLocationCategory ? 'Hours' : minRentalUnit,
        minRentalAmount: Number(minRentalAmount) || 1,
        categoryId: subId || parentId || null,
        ownerId,
        extraCategoryId: extraCategoryId || null,
        imageDataUrl: images[0] ?? null,
        imageDataUrls: images,
      })
      onSaved(updated)
      onClose()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal title="עריכת פריט" onClose={onClose}>
      <form className="modal-form" onSubmit={handleSubmit}>
        <label htmlFor="editItemCategory">קטגוריה</label>
        <select
          id="editItemCategory"
          value={parentId}
          onChange={(e) => {
            setParentId(e.target.value)
            setSubId('')
            setExtraCategoryId('')
          }}
          required
        >
          {topCategories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>

        {subCategories.length > 0 && (
          <>
            <label htmlFor="editItemSubCategory">תת-קטגוריה (אופציונלי)</label>
            <select id="editItemSubCategory" value={subId} onChange={(e) => setSubId(e.target.value)}>
              <option value="">ללא תת-קטגוריה</option>
              {subCategories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </>
        )}

        {allowsExtraCategory && (
          <ExtraCategoryField
            key={parentId}
            categories={categories}
            value={extraCategoryId}
            onChange={setExtraCategoryId}
            idPrefix="editItem"
            mainParentId={parentId}
            mainSubId={subId}
          />
        )}

        <label htmlFor="editItemName">שם הפריט</label>
        <input id="editItemName" value={form.name} onChange={updateForm('name')} required autoFocus />

        <label htmlFor="editItemDescription">תיאור</label>
        <textarea
          id="editItemDescription"
          rows={3}
          placeholder={isLocationCategory ? "תארי בצורה מיטבית את הלוקיישן שלך, מה הוא כולל, פרטים מיוחדים אם יש וכל מה שצריך כדי לדייק את בחירת השוכרת. אין צורך לכתוב כתובת פיזית של המקום" : "תארי בצורה מיטבית את הפריט שלך, מה הוא כולל, פרטים מיוחדים אם יש וכל מה שאפשר כדי לדייק את בחירת השוכרות."}
          value={form.description}
          onChange={updateForm('description')}
        />

        {forSale && (
          <>
            <label htmlFor="editItemSalePrice">מחיר מכירה (₪)</label>
            <input id="editItemSalePrice" type="number" min="0" step="1" value={salePrice} onChange={(e) => setSalePrice(e.target.value)} required />
            <label htmlFor="editItemCondition">מצב הפריט</label>
            <select id="editItemCondition" value={saleCondition} onChange={(e) => setSaleCondition(e.target.value)}>
              <option value="New">חדש בקופסה</option>
              <option value="LikeNew">כמו חדש - שומש פעמים בודדות</option>
              <option value="Used">משומש</option>
              <option value="Damaged">קצת פגום</option>
            </select>
          </>
        )}
        {!forSale && (
          <>
        {isLocationCategory && (
          <>
            <label htmlFor="editItemHourly">מחיר לשעה (₪)</label>
            <input
              id="editItemHourly"
              type="number"
              min="0"
              step="0.01"
              value={hourlyPrice}
              onChange={(e) => setHourlyPrice(e.target.value)}
              required
            />
            <label htmlFor="editItemMinHours">מינימום שעות להזמנה</label>
            <input
              id="editItemMinHours"
              type="number"
              min="1"
              step="1"
              value={minRentalAmount}
              onChange={(e) => setMinRentalAmount(e.target.value)}
            />
          </>
        )}
        <label htmlFor="editItemPrice">{isLocationCategory ? 'מחיר ליום צילום שלם (₪)' : 'מחיר יומי (₪)'}</label>
        <input
          id="editItemPrice"
          type="number"
          min="0"
          step="0.01"
          value={form.dailyPrice}
          onChange={updateForm('dailyPrice')}
          required
        />

        <label className="checkbox-row vat-row">
          <input type="checkbox" checked={addVat} onChange={(e) => setAddVat(e.target.checked)} />
          <span>הוסיפי למחיר {VAT_PERCENT}% מע״מ באופן אוטומטי</span>
        </label>
        {addVat && Number(form.dailyPrice) > 0 && (
          <p className="field-hint">
            המחיר לשוכרות: ₪{form.dailyPrice} + מע״מ · סה״כ <strong>₪{withVat(form.dailyPrice)}</strong> ליום
          </p>
        )}

        {!isLocationCategory && <QuantityField quantity={quantity} onChange={setQuantity} />}
          </>
        )}

        <label htmlFor="editItemImage">תמונות (אופציונלי)</label>
        <ImagePicker id="editItemImage" images={images} onChange={setImages} />

        <button
          type="button"
          className="advanced-toggle"
          hidden={isLocationCategory || forSale}
          onClick={() => setAdvancedOpen((prev) => !prev)}
          aria-expanded={advancedOpen}
        >
          {advancedOpen ? '▾' : '◂'} הגדרות השכרה מתקדמות (אופציונלי)
        </button>

        {advancedOpen && !isLocationCategory && !forSale && (
          <div className="advanced-panel">
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={hourlyEnabled}
                onChange={(e) => setHourlyEnabled(e.target.checked)}
              />
              אפשר גם תמחור לפי שעה
            </label>

            {hourlyEnabled && (
              <>
                <label htmlFor="editHourlyPrice">מחיר לשעה (₪)</label>
                <input
                  id="editHourlyPrice"
                  type="number"
                  min="0"
                  step="0.01"
                  value={hourlyPrice}
                  onChange={(e) => setHourlyPrice(e.target.value)}
                />
              </>
            )}

            <label htmlFor="editMinRentalAmount">משך השכרה מינימלי</label>
            <div className="min-rental-row">
              <input
                id="editMinRentalAmount"
                type="number"
                min="1"
                step="1"
                value={minRentalAmount}
                onChange={(e) => setMinRentalAmount(e.target.value)}
              />
              <select value={minRentalUnit} onChange={(e) => setMinRentalUnit(e.target.value)}>
                <option value="Days">ימים</option>
                <option value="Hours">שעות</option>
              </select>
            </div>
          </div>
        )}

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'שומר...' : 'שמירת שינויים'}
        </button>
      </form>
    </Modal>
  )
}
