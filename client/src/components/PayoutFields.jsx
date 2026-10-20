// שדות טופס פרטי חשבון בנק לקבלת תשלומים - קומפוננטה משותפת המשמשת גם בהגדרה הראשונית
// (AddItemModal) וגם בעריכה מהאזור האישי (Profile)
export function PayoutFields({ form, onChange, idPrefix = 'payout' }) {
  function update(field) {
    return (e) => onChange({ ...form, [field]: e.target.value })
  }

  return (
    <>
      <label htmlFor={`${idPrefix}AccountHolderName`}>שם בעל/ת החשבון</label>
      <input
        id={`${idPrefix}AccountHolderName`}
        value={form.accountHolderName}
        onChange={update('accountHolderName')}
        required
      />

      <label htmlFor={`${idPrefix}BankName`}>שם הבנק</label>
      <input id={`${idPrefix}BankName`} value={form.bankName} onChange={update('bankName')} required />

      <div className="payout-row">
        <div>
          <label htmlFor={`${idPrefix}BranchNumber`}>מספר סניף</label>
          <input
            id={`${idPrefix}BranchNumber`}
            dir="ltr"
            value={form.branchNumber}
            onChange={update('branchNumber')}
            required
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}AccountNumber`}>מספר חשבון</label>
          <input
            id={`${idPrefix}AccountNumber`}
            dir="ltr"
            value={form.accountNumber}
            onChange={update('accountNumber')}
            required
          />
        </div>
      </div>

      {/* הסכמה מפורשת (חוק הגנת הפרטיות) - הפירוט במדיניות הפרטיות.
          ההסכמה נבדקת גם בשרת (UserService.UpdatePayoutAsync) ומועד האישור נשמר. */}
      <label className="checkbox-row payout-consent">
        <input
          type="checkbox"
          checked={form.consentAccepted}
          onChange={(e) => onChange({ ...form, consentAccepted: e.target.checked })}
          required
        />
        <span>
          אני מאשר/ת שפרטי החשבון יישמרו במערכת ויוצגו לשוכר/ת בתשלום בהעברה בנקאית, כמפורט
          ב
          <a href="/terms#privacy" target="_blank" rel="noopener noreferrer">
            מדיניות הפרטיות
          </a>
        </span>
      </label>
    </>
  )
}

export const emptyPayoutForm = {
  accountHolderName: '',
  bankName: '',
  branchNumber: '',
  accountNumber: '',
  // ההסכמה נדרשת מחדש בכל שמירה - לעולם לא מסומנת מראש
  consentAccepted: false,
}
