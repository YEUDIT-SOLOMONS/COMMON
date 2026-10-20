import { useEffect, useRef, useState } from 'react'
import { CheckIcon, ChevronDownIcon, SortIcon } from './icons.jsx'

// תפריט מיון מותאם אישית (במקום <select> דפדפן רגיל) - כדי לשלוט לגמרי בעיצוב הרשימה
// הפתוחה (ריחוף, סימון האפשרות הנבחרת, אנימציית פתיחה) בהתאם לשפה העיצובית של האתר.
export function SortDropdown({ options, value, onChange }) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef(null)

  const selected = options.find((option) => option.value === value) ?? options[0]

  useEffect(() => {
    if (!open) return

    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) {
        setOpen(false)
      }
    }
    function handleKeyDown(e) {
      if (e.key === 'Escape') setOpen(false)
    }

    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [open])

  function handleSelect(optionValue) {
    onChange(optionValue)
    setOpen(false)
  }

  return (
    <div className="sort-dropdown" ref={rootRef}>
      <button
        type="button"
        className={`sort-dropdown-trigger ${open ? 'sort-dropdown-trigger-open' : ''}`}
        onClick={() => setOpen((prev) => !prev)}
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <SortIcon className="sort-field-icon" />
        <span>{selected.label}</span>
        <ChevronDownIcon className="sort-dropdown-chevron" />
      </button>

      {open && (
        <ul className="sort-dropdown-menu" role="listbox">
          {options.map((option) => (
            <li key={option.value} role="presentation">
              <button
                type="button"
                role="option"
                aria-selected={option.value === value}
                className={`sort-dropdown-option ${option.value === value ? 'sort-dropdown-option-active' : ''}`}
                onClick={() => handleSelect(option.value)}
              >
                <span>{option.label}</span>
                {option.value === value && <CheckIcon className="sort-dropdown-check" />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
