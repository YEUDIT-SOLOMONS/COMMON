namespace PhotoRental.Service.Services;

using System.Net;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

public class LoanExtensionService : ILoanExtensionService
{
    private const int ExtensionDays = 1;
    private const int MaxOwnerCallAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);

    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly IOutboundCallService _calls;
    private readonly IEmailService _email;

    private readonly IExtraChargeService _charges;

    public LoanExtensionService(
        ILoanRepository loans,
        IItemRepository items,
        IUserRepository users,
        IOutboundCallService calls,
        IEmailService email,
        IExtraChargeService charges)
    {
        _charges = charges;
        _loans = loans;
        _items = items;
        _users = users;
        _calls = calls;
        _email = email;
    }

    public async Task<string> RequestFromRenterAsync(string loanId)
    {
        var loan = await _loans.GetByIdAsync(loanId);
        if (loan is null || loan.Status != LoanStatus.Active)
            return "לא ניתן לבקש הארכה להשכרה הזו. תודה.";
        if (loan.ExtensionStatus == "Pending")
            return "בקשת ההארכה כבר נשלחה למשכירה. נעדכן אותך בהקדם. תודה.";
        if (loan.NotReturnedReportedAt is not null || loan.ExpectedReturnDate <= IsraelTime.Now)
            return "לא ניתן לבקש הארכה אחרי מועד ההחזרה. אנא החזירי את הציוד בהקדם. תודה.";

        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
        var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await _users.GetByIdAsync(item.OwnerId) : null;
        if (item is null || owner is null || string.IsNullOrWhiteSpace(owner.Phone) || !_calls.IsEnabled)
            return "לא ניתן לבקש הארכה כרגע. אנא החזירי את הציוד בזמן. תודה.";

        var now = DateTime.UtcNow;
        await _loans.UpdateExtensionAsync(loan.Id!, "Pending", ExtensionDays, now, 1);
        loan.ExtensionStatus = "Pending";
        loan.ExtensionDays = ExtensionDays;
        loan.ExtensionRequestedAt = now;
        loan.ExtensionAttempts = 1;

        await _calls.CallForLoanAsync(CallKind.ExtensionRequest, loan, item);
        return "בקשת ההארכה נשלחה למשכירה. נעדכן אותך בהקדם. אם לא תקבלי אישור, אנא החזירי את הציוד בזמן. תודה.";
    }

    public async Task<string?> GetOwnerMessageAsync(string loanId)
    {
        var loan = await _loans.GetByIdAsync(loanId);
        if (loan is null || loan.ExtensionStatus != "Pending") return null;
        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
        return item is null ? null : CallMessages.ExtensionRequest(item, loan, Math.Max(1, loan.ExtensionDays));
    }

    public async Task<string> OwnerAnsweredAsync(string loanId, string? digits)
    {
        var loan = await _loans.GetByIdAsync(loanId);
        if (loan is null || loan.ExtensionStatus != "Pending")
            return "הבקשה כבר טופלה. תודה, להתראות.";

        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
        var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _users.GetByIdAsync(loan.BorrowerId) : null;
        if (item is null || borrower is null)
            return "אירעה שגיאה. להתראות.";

        if (digits == "1")
        {
            var days = Math.Max(1, loan.ExtensionDays);
            var newReturn = loan.ExpectedReturnDate.AddDays(days);
            var chargedDays = ShabbatPricing.ChargedExtensionDays(loan.ExpectedReturnDate, days);
            var newRentalDays = loan.RentalDays + chargedDays;

            // השכרה ששולמה באשראי: התוספת נגבית מהכרטיס השמור דרך האתר, והארכה מאושרת רק אם החיוב הצליח
            decimal charged = 0;
            if (loan.PaymentMethod == PaymentMethod.CreditCard && chargedDays > 0)
            {
                var amount = loan.RateOr(item.DailyPrice) * chargedDays * loan.Units;
                var charge = await _charges.ChargeAsync(borrower, amount, $"הארכת השכרה - {item.Name}");
                if (!charge.Success)
                {
                    await _loans.UpdateExtensionAsync(loan.Id!, "Declined", loan.ExtensionDays, loan.ExtensionRequestedAt, loan.ExtensionAttempts);
                    loan.ExtensionStatus = "Declined";
                    await NotifyRenterAsync(borrower, item, loan, approved: false, chargeFailed: true);
                    return "לא הצלחנו לחייב את הכרטיס של השוכרת, ולכן ההארכה לא אושרה והשוכרת עודכנה. להתראות.";
                }
                charged = amount;
            }

            await _loans.ApplyExtensionAsync(loan.Id!, newReturn, newRentalDays, loan.ExtensionChargedTotal + charged);
            loan.ExtensionChargedTotal += charged;
            loan.ExpectedReturnDate = newReturn;
            loan.RentalDays = newRentalDays;
            loan.ExtensionStatus = "Approved";

            await NotifyRenterAsync(borrower, item, loan, approved: true, chargedDays: chargedDays);
            return "תודה, ההארכה אושרה והשוכרת עודכנה. להתראות.";
        }

        if (digits == "2")
        {
            await _loans.UpdateExtensionAsync(loan.Id!, "Declined", loan.ExtensionDays, loan.ExtensionRequestedAt, loan.ExtensionAttempts);
            loan.ExtensionStatus = "Declined";
            await NotifyRenterAsync(borrower, item, loan, approved: false);
            return "תודה, הבקשה נדחתה והשוכרת עודכנה. להתראות.";
        }

        return "לא התקבלה תשובה תקינה. להתראות.";
    }

    public async Task OwnerCallEndedWithoutAnswerAsync(string loanId)
    {
        var loan = await _loans.GetByIdAsync(loanId);
        if (loan is null || loan.ExtensionStatus != "Pending" || loan.Status != LoanStatus.Active)
            return;
        if (loan.ExtensionAttempts >= MaxOwnerCallAttempts)
            return;

        loan.ExtensionAttempts += 1;
        await _loans.UpdateExtensionAsync(loan.Id!, "Pending", loan.ExtensionDays, loan.ExtensionRequestedAt, loan.ExtensionAttempts);
        await _calls.CallForLoanAsync(CallKind.ExtensionRequest, loan, atLocal: IsraelTime.Now + RetryDelay);
    }

    public async Task ExpirePendingAsync()
    {
        var nowLocal = IsraelTime.Now;
        var expired = (await _loans.GetByExtensionStatusAsync("Pending"))
            .Where(l => l.ExtensionStatus == "Pending" && (l.ExpectedReturnDate <= nowLocal || l.Status != LoanStatus.Active))
            .ToList();

        foreach (var loan in expired)
        {
            // הציוד כבר הוחזר בזמן שהבקשה ממתינה - סוגרים בשקט, בלי הודעה לשוכרת
            if (loan.Status != LoanStatus.Active)
            {
                await _loans.UpdateExtensionAsync(loan.Id!, "Expired", loan.ExtensionDays, loan.ExtensionRequestedAt, loan.ExtensionAttempts);
                continue;
            }

            var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
            var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _users.GetByIdAsync(loan.BorrowerId) : null;
            await _loans.UpdateExtensionAsync(loan.Id!, "Expired", loan.ExtensionDays, loan.ExtensionRequestedAt, loan.ExtensionAttempts);
            loan.ExtensionStatus = "Expired";
            if (item is not null && borrower is not null)
                await NotifyRenterAsync(borrower, item, loan, approved: false);
        }
    }

    // מייל + שיחה (נדחית אוטומטית בשבת/חג) לשוכרת על תוצאת הבקשה
    private async Task NotifyRenterAsync(User borrower, Item item, Loan loan, bool approved, bool chargeFailed = false, int chargedDays = 0)
    {
        if (_email.IsEnabled && !string.IsNullOrWhiteSpace(borrower.Email))
        {
            try
            {
                string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {H(NameText.FirstName(borrower.Name))},</p>" +
                           (approved
                               ? $"<p>המשכירה אישרה את הארכת ההשכרה של <strong>{H(LoanText.Name(item, loan))}</strong>. מועד ההחזרה החדש: <strong>{loan.ExpectedReturnDate:dd/MM/yyyy HH:mm}</strong>.</p>" +
                                 (chargedDays == 0
                                     ? "<p>ימי ההארכה יוצאים בשבת/חג - ללא תוספת תשלום.</p>"
                                     : loan.PaymentMethod == PaymentMethod.CreditCard
                                     ? $"<p>כרטיס האשראי שלך חויב ב-₪{loan.RateOr(item.DailyPrice) * chargedDays * loan.Units:0.##} על התוספת.</p>"
                                     : $"<p>התשלום על התוספת (₪{loan.RateOr(item.DailyPrice) * chargedDays * loan.Units:0.##}) ישולם ישירות למשכירה.</p>")
                               : (chargeFailed
                                     ? $"<p>המשכירה אישרה את הארכת ההשכרה של <strong>{H(LoanText.Name(item, loan))}</strong>, אך לא הצלחנו לחייב את כרטיס האשראי השמור שלך, ולכן ההארכה לא אושרה. אנא החזירי את הציוד במועד שנקבע.</p>"
                                     : $"<p>המשכירה לא אישרה את הארכת ההשכרה של <strong>{H(LoanText.Name(item, loan))}</strong>. אנא החזירי את הציוד במועד שנקבע.</p>") +
                                 "<p><strong>שימי לב:</strong> איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור.</p>") +
                           "<p>common</p></div>";
                await _email.SendAsync(borrower.Email, borrower.Name,
                    approved ? "ההארכה אושרה - common" : "ההארכה לא אושרה - common", html);
            }
            catch
            {
                // כשל במייל לא מונע את השיחה
            }
        }

        // חיוב שנכשל - רק מייל, בלי שיחה
        if (chargeFailed)
            return;

        try
        {
            await _calls.CallForLoanAsync(approved ? CallKind.ExtensionApproved : CallKind.ExtensionDeclined, loan, item, borrower);
        }
        catch
        {
            // ממשיכים
        }
    }
}
