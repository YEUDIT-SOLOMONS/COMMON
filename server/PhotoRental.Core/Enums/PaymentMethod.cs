namespace PhotoRental.Core.Enums;

// אמצעי התשלום שבחר השוכר עבור עסקת ההשכרה
public enum PaymentMethod
{
    // מזומן - מותר רק כאשר אופן הקבלה הוא איסוף עצמי
    Cash,

    // העברה בנקאית ישירות לחשבון המשכיר
    BankTransfer,

    // כרטיס אשראי - סליקה מדומה מול UPAY (ראו SimulatedUpayPaymentService)
    CreditCard
}
