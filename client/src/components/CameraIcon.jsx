// אייקון מצלמה מינימלי המשמש כלוגו וכתמונת-רקע עבור כרטיסי פריטים ללא תמונה אמיתית
export function CameraIcon({ className }) {
  return (
    <svg
      className={className}
      viewBox="0 0 64 64"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
    >
      <rect x="6" y="20" width="52" height="32" rx="6" fill="currentColor" opacity="0.18" />
      <rect x="6" y="20" width="52" height="32" rx="6" stroke="currentColor" strokeWidth="2.5" />
      <rect x="22" y="12" width="18" height="10" rx="2.5" fill="currentColor" opacity="0.18" />
      <rect x="22" y="12" width="18" height="10" rx="2.5" stroke="currentColor" strokeWidth="2.5" />
      <circle cx="32" cy="37" r="11" stroke="currentColor" strokeWidth="2.5" />
      <circle cx="32" cy="37" r="5" fill="currentColor" />
      <circle cx="47" cy="28" r="2" fill="currentColor" />
    </svg>
  )
}
