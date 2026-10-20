namespace PhotoRental.Service.Services;

using System.Globalization;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class PayoutService : IPayoutService
{
    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly CommissionOptions _commission;
    private readonly IPaymentProofRepository _proofs;
    private readonly ICommissionStatementRepository _statements;

    public PayoutService(ILoanRepository loans, IItemRepository items, IUserRepository users, IEmailService email, CommissionOptions commission,
        IPaymentProofRepository proofs, ICommissionStatementRepository statements)
    {
        _proofs = proofs;
        _statements = statements;
        _loans = loans;
        _items = items;
        _users = users;
        _email = email;
        _commission = commission;
    }

    private sealed record Row(Loan Loan, Item Item, PayoutRentalDto Dto);

    private async Task<List<Row>> RowsAsync()
    {
        var items = (await _items.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var now = DateTime.UtcNow;
        var rows = new List<Row>();
        foreach (var loan in await _loans.GetAllAsync())
        {
            if (loan.PaymentMethod != PaymentMethod.CreditCard || loan.PaymentStatus != PaymentStatus.Paid)
                continue;
            if (loan.Status != LoanStatus.Returned || loan.ActualReturnDate is null)
                continue;
            if (!items.TryGetValue(loan.ItemCopyId ?? string.Empty, out var item) || string.IsNullOrEmpty(item.OwnerId))
                continue;

            var gross = PayoutMath.Gross(loan, loan.RateOr(item.DailyPrice));
            var rate = loan.CommissionRate ?? _commission.Percent / 100m;
            var commission = PayoutMath.Commission(gross, rate);
            var release = PayoutMath.ReleaseAt(loan)!.Value;
            rows.Add(new Row(loan, item, new PayoutRentalDto
            {
                LoanId = loan.Id ?? string.Empty,
                ItemName = LoanText.Name(item, loan),
                ReturnedAt = loan.ActualReturnDate.Value,
                ReleaseAt = release,
                Gross = gross,
                Commission = commission,
                Net = gross - commission,
                LateFeeUncollected = Math.Max(0, loan.LateDays * loan.RateOr(item.DailyPrice) * loan.Units - loan.LateFeeCharged),
                Released = release <= now,
                Paid = loan.OwnerPayoutPaidAtUtc is not null,
                PaidAt = loan.OwnerPayoutPaidAtUtc,
                HasProof = !string.IsNullOrEmpty(loan.OwnerPayoutProofId)
            }));
        }
        return rows;
    }

    private async Task<List<CommissionStatement>> PendingOffsetsAsync(string? ownerId = null) =>
        (await _statements.GetAllAsync())
            .Where(s => s.OffsetAmount > 0 && s.OffsetSettledAtUtc is null && (ownerId is null || s.OwnerId == ownerId))
            .OrderBy(s => s.Month)
            .ToList();

    public async Task<decimal> GetCardBalanceAsync(string ownerId)
    {
        var due = (await RowsAsync()).Where(r => r.Item.OwnerId == ownerId && r.Dto.Released && !r.Dto.Paid).Sum(r => r.Dto.Net);
        var offsets = (await PendingOffsetsAsync(ownerId)).Sum(s => s.OffsetAmount);
        return Math.Max(0, due - offsets);
    }

    public async Task<IReadOnlyList<OwnerPayoutDto>> GetAsync()
    {
        var users = (await _users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);
        var result = new List<OwnerPayoutDto>();
        var offsets = await PendingOffsetsAsync();

        foreach (var group in (await RowsAsync()).GroupBy(r => r.Item.OwnerId!))
        {
            users.TryGetValue(group.Key, out var owner);
            var rentals = group.Select(g => g.Dto).OrderBy(d => d.ReleaseAt).ToList();
            result.Add(new OwnerPayoutDto
            {
                OwnerId = group.Key,
                OwnerName = owner?.Name ?? "(משתמשת שנמחקה)",
                OwnerEmail = owner?.Email ?? string.Empty,
                OwnerPhone = owner?.Phone ?? string.Empty,
                BankConfigured = !string.IsNullOrWhiteSpace(owner?.PayoutAccountNumber),
                BankHolder = owner?.PayoutAccountHolderName,
                BankName = owner?.PayoutBankName,
                BankBranch = owner?.PayoutBranchNumber,
                BankAccount = owner?.PayoutAccountNumber,
                DueNow = rentals.Where(d => d.Released && !d.Paid).Sum(d => d.Net),
                Pending = rentals.Where(d => !d.Released && !d.Paid).Sum(d => d.Net),
                Rentals = rentals,
                Offsets = offsets.Where(s => s.OwnerId == group.Key)
                    .Select(s => new PayoutOffsetDto { Month = s.Month, Amount = s.OffsetAmount }).ToList()
            });
            var last = result[^1];
            last.TransferNow = Math.Max(0, last.DueNow - last.Offsets.Sum(o => o.Amount));
        }

        return result.OrderByDescending(o => o.DueNow).ThenBy(o => o.OwnerName).ToList();
    }

    public async Task MarkPaidAsync(MarkPayoutPaidDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OwnerId) || dto.LoanIds.Count == 0)
            throw new ArgumentException("יש לבחור משכירה והשכרות לסימון");

        var wanted = dto.LoanIds.ToHashSet(StringComparer.Ordinal);
        var rows = (await RowsAsync())
            .Where(r => r.Item.OwnerId == dto.OwnerId && wanted.Contains(r.Dto.LoanId) && !r.Dto.Paid)
            .ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("אין השכרות פתוחות לתשלום מהרשימה שנבחרה");

        ProofFileValidator.ValidatedProof? proof = null;
        string? proofId = null;
        if (!string.IsNullOrWhiteSpace(dto.ProofDataUrl))
        {
            proof = ProofFileValidator.Validate(dto.ProofDataUrl, dto.ProofFileName);
            var saved = await _proofs.CreateAsync(new PaymentProof
            {
                FileName = proof.FileName,
                ContentType = proof.ContentType,
                DataUrl = dto.ProofDataUrl!,
                UploadedAtUtc = DateTime.UtcNow
            });
            proofId = saved.Id;
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
            await _loans.UpdateOwnerPayoutAsync(row.Dto.LoanId, now, row.Dto.Net, proofId);

        var settled = await PendingOffsetsAsync(dto.OwnerId);
        foreach (var statement in settled)
        {
            statement.OffsetSettledAtUtc = now;
            await _statements.UpsertAsync(statement);
        }
        var offsetTotal = settled.Sum(s => s.OffsetAmount);

        var owner = await _users.GetByIdAsync(dto.OwnerId);
        if (owner is not null && _email.IsEnabled && !string.IsNullOrWhiteSpace(owner.Email))
        {
            try
            {
                var total = Math.Max(0, rows.Sum(r => r.Dto.Net) - offsetTotal);
                var h = (string? t) => System.Net.WebUtility.HtmlEncode(t ?? string.Empty);
                var list = string.Join("", rows.Select(r => $"<li>{h(r.Dto.ItemName)}: ₪{r.Dto.Net.ToString("0.##", CultureInfo.InvariantCulture)}</li>"));
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                           $"<p>העברנו לחשבון הבנק שלך <strong>₪{total.ToString("0.##", CultureInfo.InvariantCulture)}</strong> עבור השכרות ששולמו באשראי דרך האתר (אחרי ניכוי עמלה)</p>" +
                           $"<ul>{list}</ul>" +
                           (offsetTotal > 0
                               ? $"<p>מהסכום קוזזה עמלת השירות החודשית על השכרות במזומן/בהעברה: ₪{offsetTotal.ToString("0.##", CultureInfo.InvariantCulture)}.</p>"
                               : string.Empty) +
                           "<p>ההעברה תופיע בחשבון בתוך יום עסקים או שניים.</p>" +
                           (proof is null ? string.Empty : "<p>אסמכתת ההעברה מצורפת למייל, וזמינה גם באזור האישי.</p>") +
                           "</div>";
                var attachments = proof is null ? null : new[] { new EmailAttachment(proof.FileName, proof.Base64) };
                await _email.SendAsync(owner.Email, owner.Name, "הועבר אלייך תשלום - common", html, attachments);
            }
            catch
            {
            }
        }
    }
}
