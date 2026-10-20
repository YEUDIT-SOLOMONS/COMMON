export function QuietHoursField({ value, onChange, idPrefix = 'quiet' }) {
  const set = (patch) => onChange({ ...value, ...patch })
  return (
    <div className="quiet-hours" role="radiogroup" aria-label="שעות לשיחות">
      <label className="checkbox-row">
        <input type="radio" name={`${idPrefix}Mode`} checked={!value.enabled} onChange={() => set({ enabled: false })} />
        <span>אני זמינה תמיד</span>
      </label>
      <label className="checkbox-row">
        <input type="radio" name={`${idPrefix}Mode`} checked={value.enabled} onChange={() => set({ enabled: true })} />
        <span>אין לחייג אלי בשעות הלילה בין השעות:</span>
      </label>
      <div className="quiet-hours-times">
        <input
          id={`${idPrefix}From`}
          type="time"
          aria-label="משעה"
          value={value.from}
          onChange={(e) => set({ from: e.target.value, enabled: true })}
        />
        <span>עד</span>
        <input
          id={`${idPrefix}To`}
          type="time"
          aria-label="עד שעה"
          value={value.to}
          onChange={(e) => set({ to: e.target.value, enabled: true })}
        />
      </div>
    </div>
  )
}
