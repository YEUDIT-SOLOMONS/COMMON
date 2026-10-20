const ITEM_STATUS_LABELS = {
  Available: 'זמין',
  Rented: 'מושכר',
  Maintenance: 'בתחזוקה',
}

const LOAN_STATUS_LABELS = {
  Active: 'פעילה',
  Returned: 'הוחזרה',
  Overdue: 'באיחור',
  Cancelled: 'בוטלה',
}

const DELIVERY_METHOD_LABELS = {
  SelfPickup: 'איסוף עצמי',
  Delivery: 'משלוח',
}

const PAYMENT_METHOD_LABELS = {
  Cash: 'מזומן',
  BankTransfer: 'העברה בנקאית',
  CreditCard: 'כרטיס אשראי',
}

const PAYMENT_STATUS_LABELS = {
  Pending: 'ממתין לתשלום',
  Paid: 'שולם',
}

const RETURN_METHOD_LABELS = {
  SelfDropoff: 'מסירה עצמית',
  CourierPickup: 'איסוף שליח',
}

export function deliveryMethodLabel(method) {
  return DELIVERY_METHOD_LABELS[method] ?? method
}

export function returnMethodLabel(method) {
  return RETURN_METHOD_LABELS[method] ?? method
}

export function paymentMethodLabel(method) {
  return PAYMENT_METHOD_LABELS[method] ?? method
}

export function paymentStatusLabel(status) {
  return PAYMENT_STATUS_LABELS[status] ?? status
}

export function ItemStatusBadge({ status, className = '' }) {
  return (
    <span className={`badge badge-item-${status} ${className}`.trim()}>
      {ITEM_STATUS_LABELS[status] ?? status}
    </span>
  )
}

export function LoanStatusBadge({ status }) {
  return (
    <span className={`badge badge-loan-${status}`}>
      {LOAN_STATUS_LABELS[status] ?? status}
    </span>
  )
}
