function formatDateHe(iso) {
  if (!iso) return ''
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

// פרטי האיסוף העצמי שמוצגים לאחר אישור ההשכרה: כתובת האיסוף (הכתובת המלאה נחשפת רק לאחר אימות
// הזמינות מול חבר הקהילה, ולכן בלעדיה מוצגת העיר בלבד) ומועד האיסוף - התאריך והשעה שנבחרו בהשכרה.
export function PickupDetails({ place, notes = '', hasFullAddress = true, date, time }) {
  return (
    <div className="pickup-details">
      <h3>פרטי האיסוף</h3>
      <dl>
        <div>
          <dt>כתובת לאיסוף</dt>
          <dd>{place || 'תימסר על ידי הצלמת בקהילה'}</dd>
          {!hasFullAddress && (
            <span className="pickup-details-note">הכתובת המדויקת תימסר על ידי הצלמת בקהילה.</span>
          )}
        </div>
        {notes && (
          <div>
            <dt>הערות על הכתובת</dt>
            <dd>{notes}</dd>
          </div>
        )}
        <div>
          <dt>מועד האיסוף</dt>
          <dd>
            {formatDateHe(date)} · {time}
          </dd>
        </div>
      </dl>
    </div>
  )
}
