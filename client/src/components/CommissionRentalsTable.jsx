import { formatDate, formatDateTime, formatMoney } from '../utils/format.js'

// טבלת הפירוט של העמלה: כל ההשכרות שהוחזרו בחודש, עם סכום הציוד והעמלה של כל אחת (משותפת לאזור
// האישי ולמסך הניהול)
export function CommissionRentalsTable({ rentals, totalRentalAmount, totalCommission }) {
  if (rentals.length === 0) return <p className="state-message">לא הוחזרו השכרות בחודש הזה.</p>

  return (
    <div className="table-wrap commission-table">
      <table>
        <thead>
          <tr>
            <th>פריט</th>
            <th>תחילת השכרה</th>
            <th>הוחזר</th>
            <th>ימים</th>
            <th>סכום הציוד</th>
            <th>עמלה</th>
          </tr>
        </thead>
        <tbody>
          {rentals.map((rental) => (
            <tr key={rental.loanId}>
              <td>{rental.itemName}</td>
              <td>
                {formatDate(rental.startDate)}
                {rental.startTime ? ` · ${rental.startTime}` : ''}
              </td>
              <td>{formatDateTime(rental.returnedAt)}</td>
              <td>{rental.rentalDays}</td>
              <td>{formatMoney(rental.rentalAmount)}</td>
              <td>
                <strong>{formatMoney(rental.commission)}</strong>
                <span className="payment-status-note"> ({Math.round(rental.rate * 10000) / 100}%)</span>
              </td>
            </tr>
          ))}
          <tr className="commission-total-row">
            <td colSpan={4}>
              <strong>סה"כ ({rentals.length} השכרות)</strong>
            </td>
            <td>
              <strong>{formatMoney(totalRentalAmount)}</strong>
            </td>
            <td>
              <strong>{formatMoney(totalCommission)}</strong>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}
