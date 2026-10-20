// כמה עותקים פיזיים של הפריט יש למשכירה
export function QuantityField({ quantity, onChange }) {
  const multiple = quantity > 1
  return (
    <div className="quantity-field">
      <label className="checkbox-row">
        <input type="checkbox" checked={multiple} onChange={(e) => onChange(e.target.checked ? 2 : 1)} />
        <span>יש לי יותר מעותק אחד של הפריט הזה</span>
      </label>
      {multiple && (
        <div className="quantity-field-count">
          <label htmlFor="itemQuantity">כמה עותקים יש לך?</label>
          <input
            id="itemQuantity"
            type="number"
            min="2"
            max="50"
            step="1"
            value={quantity}
            onChange={(e) => onChange(Math.min(50, Math.max(2, Math.round(Number(e.target.value) || 2))))}
          />
          <p className="field-hint">שוכרות יוכלו לבחור כמה יחידות לשכור, והיומן יתעדכן לפי מספר העותקים הפנויים.</p>
        </div>
      )}
    </div>
  )
}
