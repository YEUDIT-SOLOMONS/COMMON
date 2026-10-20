import { useEffect, useId, useRef, useState } from 'react'
import { addressApi } from '../api/endpoints.js'

// השוואה סלחנית: בלי גרשיים/מקפים/רווחים כפולים ובלי הבדל בין אותיות - כמו בשרת (AddressDirectory.Normalize)
export function normalizeAddressText(text) {
  return (text ?? '')
    .replace(/["'׳״`]/g, '')
    .replace(/[-–־]/g, ' ')
    .split(/\s+/)
    .filter(Boolean)
    .join(' ')
    .toLowerCase()
}

// "ישראלזון 10" -> { street: 'ישראלזון', number: '10' }; כתובת בלי מספר בסוף נשארת רחוב בלבד
export function splitStreetAndNumber(address) {
  const match = /^(.*\S)\s+(\d+[֐-׿A-Za-z/-]*)$/.exec((address ?? '').trim())
  return match ? { street: match[1], number: match[2] } : { street: (address ?? '').trim(), number: '' }
}

export function joinStreetAndNumber(street, number) {
  return [street.trim(), number.trim()].filter(Boolean).join(' ')
}

// תיבת טקסט עם רשימת הצעות (combobox נגיש): מקלידים, בוחרים מהרשימה (עכבר/מקלדת/מגע). הערך תקף רק אם הוא זהה (בהשוואה
// סלחנית) להצעה מהמאגר הרשמי. אם המאגר לא זמין (שגיאת רשת/שרת) לא חוסמים - הערך נחשב תקף כדי לא לתקוע הזמנה.
export function AutocompleteInput({ id, value, onChange, fetchSuggestions, onValidChange, disabled, required, placeholder, label, autoFocus, dependsOn }) {
  const listId = useId()
  const [suggestions, setSuggestions] = useState([])
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [checked, setChecked] = useState(false) // האם כבר נבדק הערך הנוכחי מול המאגר
  const [valid, setValid] = useState(false)
  const [touched, setTouched] = useState(false)
  const [failed, setFailed] = useState(false)
  const requestRef = useRef(0)

  useEffect(() => {
    if (!value.trim()) {
      setSuggestions([])
      setChecked(true)
      setValid(!required)
      return undefined
    }
    setChecked(false)
    const requestId = ++requestRef.current
    const timer = window.setTimeout(async () => {
      try {
        const result = await fetchSuggestions(value)
        if (requestId !== requestRef.current) return
        setSuggestions(result)
        setFailed(false)
        setValid(result.some((s) => normalizeAddressText(s) === normalizeAddressText(value)))
      } catch {
        if (requestId !== requestRef.current) return
        setSuggestions([])
        setFailed(true)
        setValid(true) // המאגר לא זמין - לא חוסמים
      }
      setChecked(true)
    }, 220)
    return () => window.clearTimeout(timer)
  }, [value, fetchSuggestions, required, dependsOn])

  useEffect(() => {
    onValidChange?.(checked && valid)
  }, [checked, valid, onValidChange])

  function choose(text) {
    onChange(text)
    setOpen(false)
    setActive(-1)
  }

  function handleKeyDown(e) {
    if (!open || suggestions.length === 0) return
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((i) => (i + 1) % suggestions.length)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => (i <= 0 ? suggestions.length - 1 : i - 1))
    } else if (e.key === 'Enter' && active >= 0) {
      e.preventDefault()
      choose(suggestions[active])
    } else if (e.key === 'Escape') {
      e.stopPropagation()
      setOpen(false)
    }
  }

  const hasQuery = value.trim() !== ''
  const showList = open && suggestions.length > 0 && !(suggestions.length === 1 && normalizeAddressText(suggestions[0]) === normalizeAddressText(value))
  const panelMessage = !open || showList
    ? null
    : !hasQuery
      ? 'התחילי להקליד ובחרי מהרשימה שתיפתח'
      : !checked
        ? 'מחפשת...'
        : failed
          ? 'לא ניתן לטעון הצעות כרגע - אפשר להמשיך להקליד'
          : suggestions.length === 0
            ? 'לא נמצאו התאמות. נסי לכתוב רק חלק מהשם (למשל בלי תואר או מספר)'
            : null
  const showHint = checked && !valid && hasQuery

  return (
    <div className="ac-wrap">
      {label && <label htmlFor={id}>{label}</label>}
      <input
        id={id}
        type="text"
        role="combobox"
        aria-expanded={showList}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={active >= 0 ? `${listId}-${active}` : undefined}
        aria-invalid={showHint || undefined}
        autoComplete="off"
        autoFocus={autoFocus}
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        required={required}
        onChange={(e) => {
          onChange(e.target.value)
          setOpen(true)
          setActive(-1)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => {
          setTouched(true)
          window.setTimeout(() => setOpen(false), 120)
        }}
        onKeyDown={handleKeyDown}
      />
      {panelMessage && (
        <div className="ac-list ac-message" role="status">
          {panelMessage}
        </div>
      )}
      {showList && (
        <ul id={listId} className="ac-list" role="listbox">
          {suggestions.map((s, i) => (
            <li
              key={s}
              id={`${listId}-${i}`}
              role="option"
              aria-selected={i === active}
              className={`ac-option ${i === active ? 'ac-option-active' : ''}`}
              onMouseDown={(e) => {
                e.preventDefault()
                choose(s)
              }}
            >
              {s}
            </li>
          ))}
        </ul>
      )}
      {showHint && <span className="field-hint ac-hint">בחרי מהרשימה שנפתחת בזמן ההקלדה, כדי שהכתובת תהיה מדויקת.</span>}
    </div>
  )
}

// עיר + רחוב מתוך הרשימה הרשמית. onValidChange מדווח אם שניהם תקפים (חובה לפני שליחה/שמירה)
export function CityStreetFields({ idPrefix, city, street, onCityChange, onStreetChange, onValidChange, disabled, autoFocus }) {
  const [cityOk, setCityOk] = useState(false)
  const [streetOk, setStreetOk] = useState(false)
  const cityRef = useRef(city)
  cityRef.current = city

  useEffect(() => {
    onValidChange?.(cityOk && streetOk)
  }, [cityOk, streetOk, onValidChange])

  const fetchCities = useRef((q) => addressApi.cities(q)).current
  // הצעות הרחוב תלויות בעיר הנוכחית (ref כדי שהפונקציה תישאר יציבה ולא תפעיל מחדש את החיפוש בכל הקלדה בעיר)
  const fetchStreets = useRef((q) => addressApi.streets(cityRef.current, q)).current

  return (
    <>
      <AutocompleteInput
        id={`${idPrefix}City`}
        label="עיר"
        value={city}
        onChange={onCityChange}
        fetchSuggestions={fetchCities}
        onValidChange={setCityOk}
        disabled={disabled}
        required
        autoFocus={autoFocus}
      />
      <AutocompleteInput
        id={`${idPrefix}Street`}
        label="רחוב"
        value={street}
        onChange={onStreetChange}
        fetchSuggestions={fetchStreets}
        onValidChange={setStreetOk}
        disabled={disabled || !city.trim()}
        required
        dependsOn={city}
        placeholder={city.trim() ? '' : 'קודם בחרי עיר'}
      />
    </>
  )
}
