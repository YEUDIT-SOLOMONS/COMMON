// תצוגת כוכבים (קריאה בלבד), למשל ★★★★☆
export function Stars({ value, className = '' }) {
  const rounded = Math.round(value)
  return (
    <span className={`stars ${className}`} aria-label={`${value} מתוך 5 כוכבים`} role="img">
      {'★'.repeat(rounded)}
      <span className="stars-empty">{'★'.repeat(5 - rounded)}</span>
    </span>
  )
}

// בחירת דירוג 1-5 בלחיצה
export function StarPicker({ value, onChange }) {
  return (
    <div className="star-picker" role="radiogroup" aria-label="דירוג">
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          type="button"
          role="radio"
          aria-checked={value === n}
          aria-label={`${n} כוכבים`}
          className={`star-picker-star ${n <= value ? 'is-on' : ''}`}
          onClick={() => onChange(n)}
        >
          ★
        </button>
      ))}
    </div>
  )
}
