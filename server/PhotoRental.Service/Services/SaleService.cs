namespace PhotoRental.Service.Services;

using System.Net;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// מכירות יד 2: תשלום דרך האתר, 5% עמלה מהמוכרת, העברה למוכרת אחרי שהקונה אישרה קבלה (או אוטומטית אחרי 3 ימים)
public class SaleService : ISaleService
{
    public const decimal CommissionRate = 0.05m;
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromDays(3);
    private const string InboxEmail = "common@onyxapp.co.il";

    private readonly ISaleRepository _sales;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly ILoanService _loans;
    private readonly IOrderPaymentService _payments;
    private readonly IEmailService _email;
    private readonly ClientOptions _client;
    private readonly IOutboundCallService _outbound;

    public SaleService(ISaleRepository sales, IItemRepository items, IUserRepository users, ILoanService loans,
        IOrderPaymentService payments, IEmailService email, ClientOptions client, IOutboundCallService outbound)
    {
        _outbound = outbound;
        _sales = sales;
        _items = items;
        _users = users;
        _loans = loans;
        _payments = payments;
        _email = email;
        _client = client;
    }

    private async Task<Item> SaleItemAsync(string itemId)
    {
        var item = await _items.GetByIdAsync(itemId);
        if (item is null || item.DeletedAtUtc is not null || !item.ForSale)
            throw new KeyNotFoundException("הפריט לא נמצא");
        return item;
    }

    public async Task<decimal> QuoteAsync(string itemId, DeliveryAddressDto address) =>
        await _loans.GetSaleDeliveryFeeAsync(await SaleItemAsync(itemId), address);

    public async Task<SaleCreatedDto> CreateAsync(string buyerId, SaleCreateDto dto)
    {
        var sale = await CreateSaleAsync(buyerId, dto);
        return new SaleCreatedDto { Id = sale.Id, Token = sale.BuyerToken };
    }

    private async Task<Sale> CreateSaleAsync(string buyerId, SaleCreateDto dto)
    {
        var method = dto.PaymentMethod is "Cash" or "BankTransfer" ? dto.PaymentMethod : "CreditCard";
        var card = method == "CreditCard";
        var paymentId = card ? dto.PaymentTransactionId ?? string.Empty : string.Empty;
        var all = await _sales.GetAllAsync();
        if (card && all.FirstOrDefault(s => s.PaymentTransactionId == paymentId && s.BuyerId == buyerId) is { } existing)
            return existing;
        if (card && all.Any(s => s.PaymentTransactionId == paymentId))
            throw new InvalidOperationException("התשלום הזה כבר שימש לרכישה אחרת");

        var item = await SaleItemAsync(dto.ItemId);
        var buyer = await _users.GetByIdAsync(buyerId) ?? throw new KeyNotFoundException("המשתמשת לא נמצאה");
        if (item.OwnerId == buyerId)
            throw new InvalidOperationException("אי אפשר לקנות פריט שלך");

        var delivery = dto.DeliveryMethod == "Delivery";
        if (delivery && !card)
            throw new ArgumentException("משלוח עם שליח זמין רק בתשלום בכרטיס אשראי");
        if (!card && item.SoldAtUtc is not null)
            throw new InvalidOperationException("מצטערות, הפריט כבר נמכר");
        var fee = delivery ? await _loans.GetSaleDeliveryFeeAsync(item, dto.DeliveryAddress ?? new()) : 0m;
        if (card)
            await _payments.VerifyAsync(buyer, item.SalePrice + fee, paymentId);

        EmailAttachment? proofAttachment = null;
        string? proofId = null;
        if (method == "BankTransfer")
            (proofId, proofAttachment) = await _loans.StoreSaleProofAsync(dto.PaymentProofDataUrl, dto.PaymentProofFileName);

        if (item.SoldAtUtc is not null)
        {
            await NotifyInboxAsync($"תשלום על פריט שכבר נמכר - צריך החזר", $"{buyer.Name} ({buyer.Email}) שילמה ₪{item.SalePrice + fee:0.##} על \"{item.Name}\" שכבר נמכר. מזהה תשלום: {paymentId}");
            throw new InvalidOperationException("מצטערות, הפריט נמכר בדיוק עכשיו למישהי אחרת. נחזיר לך את התשלום בהקדם.");
        }

        item.SoldAtUtc = DateTime.UtcNow;
        await _items.UpdateAsync(item.Id!, item);

        var commission = Math.Round(item.SalePrice * CommissionRate, 2, MidpointRounding.AwayFromZero);
        var sale = new Sale
        {
            ItemId = item.Id!,
            ItemName = item.Name,
            SellerId = item.OwnerId ?? string.Empty,
            BuyerId = buyerId,
            Price = item.SalePrice,
            DeliveryFee = fee,
            CommissionRate = CommissionRate,
            Commission = commission,
            SellerAmount = item.SalePrice - commission,
            DeliveryMethod = delivery ? "Delivery" : "SelfPickup",
            PaymentMethod = method,
            PaymentTransactionId = paymentId,
            PaymentProofId = proofId,
            BuyerToken = NewToken(),
            SellerToken = NewToken(),
            CreatedAtUtc = DateTime.UtcNow
        };

        if (delivery)
        {
            try
            {
                var (tracking, provider, addressText) = await _loans.DispatchSaleAsync(item, buyer, dto.DeliveryAddress!);
                sale.TrackingId = tracking;
                sale.ProviderId = provider;
                sale.DeliveryAddress = addressText;
            }
            catch (Exception ex)
            {
                sale.DispatchError = ex.Message;
            }
        }

        sale = await _sales.CreateAsync(sale);
        var seller = await _users.GetByIdAsync(sale.SellerId);
        await NotifyCreatedAsync(sale, item, buyer, seller, proofAttachment);
        if (seller is not null)
        {
            var how = sale.DeliveryMethod == "Delivery"
                ? "שליח יגיע לאסוף את הפריט מהכתובת שלך, כדאי להכין אותו ארוז."
                : sale.PaymentMethod == "Cash"
                    ? $"הקונה, {NameText.FirstName(buyer.Name)}, תיצור איתך קשר לתיאום האיסוף, ותשלם לך במזומן."
                    : $"הקונה, {NameText.FirstName(buyer.Name)}, תיצור איתך קשר לתיאום האיסוף.";
            try
            {
                await _outbound.CallMessageAsync(seller, CallKind.SaleNotice,
                    $"שלום {NameText.FirstName(seller.Name)}, זו הודעה מקומון. מכרת את {sale.ItemName} ב-{sale.Price:0.##} שקלים! {how} כל הפרטים נשלחו אלייך במייל. תודה.",
                    sale.ItemName);
            }
            catch
            {
                // המייל כבר נשלח
            }
        }
        return sale;
    }

    private async Task<(Sale Sale, string Role)?> ResolveAsync(string id, string? token, string? userId)
    {
        var sale = await _sales.GetByIdAsync(id);
        if (sale is null)
            return null;
        if ((!string.IsNullOrEmpty(token) && token == sale.BuyerToken) || (userId is not null && userId == sale.BuyerId))
            return (sale, "Buyer");
        if ((!string.IsNullOrEmpty(token) && token == sale.SellerToken) || (userId is not null && userId == sale.SellerId))
            return (sale, "Seller");
        return null;
    }

    private async Task<object> ViewAsync(Sale sale, string role)
    {
        var item = await _items.GetByIdAsync(sale.ItemId);
        var other = await _users.GetByIdAsync(role == "Buyer" ? sale.SellerId : sale.BuyerId);
        return new
        {
            sale.Id,
            Role = role,
            sale.ItemName,
            sale.ItemId,
            Image = item is null ? null : ImageLinks.Thumb(item.Id, item.ImageDataUrl),
            sale.Price,
            sale.DeliveryFee,
            Total = sale.Price + sale.DeliveryFee,
            sale.Commission,
            sale.SellerAmount,
            sale.PaymentMethod,
            sale.DeliveryMethod,
            sale.DeliveryAddress,
            PickupAddress = role == "Buyer" && sale.DeliveryMethod == "SelfPickup" && item is not null ? $"{item.Address}, {item.City}" : null,
            OtherName = other?.Name,
            OtherPhone = other?.Phone,
            sale.TrackingId,
            sale.Status,
            sale.CreatedAtUtc,
            sale.ReceivedAtUtc,
            sale.AutoConfirmed,
            sale.HandedOverAtUtc,
            sale.NotHandedAtUtc,
            ConfirmUntilUtc = sale.CreatedAtUtc + ConfirmWindow,
            Token = role == "Buyer" ? sale.BuyerToken : sale.SellerToken
        };
    }

    private async Task ConfirmReceivedAsync(Sale sale, bool auto = false)
    {
        if (sale.Status != "Paid")
            return;
        sale.Status = "Received";
        sale.ReceivedAtUtc = DateTime.UtcNow;
        sale.AutoConfirmed = auto;
        await _sales.UpdateAsync(sale);

        var seller = await _users.GetByIdAsync(sale.SellerId);
        var viaSite = sale.PaymentMethod == "CreditCard";
        if (seller is not null)
            await SendAsync(seller, $"הקונה קיבלה את {sale.ItemName} - common",
                $"<p>{(auto ? "עברו 3 ימים מהרכישה בלי דיווח על בעיה" : "הקונה אישרה שקיבלה את הפריט")} - <strong>{H(sale.ItemName)}</strong>.</p>" +
                (viaSite
                    ? $"<p>₪{sale.SellerAmount:0.##} (מחיר המכירה פחות 5% עמלה) יועברו אלייך בימים הקרובים.</p>"
                    : $"<p>עמלת השירות (₪{sale.Commission:0.##}) תיכלל בדוח העמלות החודשי שלך.</p>"));
        if (viaSite)
            await NotifyInboxAsync($"מכירה לתשלום למוכרת: {sale.ItemName}",
                $"{seller?.Name} - להעביר ₪{sale.SellerAmount:0.##} ({(auto ? "אישור אוטומטי" : "הקונה אישרה קבלה")}). מסך הניהול - לשונית מכירות.");
    }

    // המוכרת מסמנת אם מסרה את הפריט. "עוד לא נמסר" עוצר את האישור האוטומטי ומעדכן את הנהלת האתר
    private async Task MarkHandedAsync(Sale sale, bool handed)
    {
        if (handed)
        {
            sale.HandedOverAtUtc = DateTime.UtcNow;
            sale.NotHandedAtUtc = null;
        }
        else
        {
            sale.NotHandedAtUtc = DateTime.UtcNow;
            sale.HandedOverAtUtc = null;
        }
        await _sales.UpdateAsync(sale);
        if (!handed)
        {
            var seller = await _users.GetByIdAsync(sale.SellerId);
            var buyer = await _users.GetByIdAsync(sale.BuyerId);
            await NotifyInboxAsync($"מכירה שלא נמסרה: {sale.ItemName}",
                $"{seller?.Name} ({seller?.Phone}) סימנה שהפריט עוד לא נמסר ל{buyer?.Name} ({buyer?.Phone}). האישור האוטומטי נעצר.");
        }
    }

    private async Task ReportProblemAsync(Sale sale, string text)
    {
        if (sale.Status != "Paid")
            throw new InvalidOperationException("כבר אישרת קבלה של הפריט");
        if (DateTime.UtcNow > sale.CreatedAtUtc + ConfirmWindow)
            throw new InvalidOperationException("עברו 3 ימים מהרכישה. אפשר לפנות אלינו בדף צור קשר");
        sale.Status = "Problem";
        sale.ProblemText = text.Trim();
        await _sales.UpdateAsync(sale);
        var buyer = await _users.GetByIdAsync(sale.BuyerId);
        await NotifyInboxAsync($"דיווח על בעיה במכירה: {sale.ItemName}",
            $"{buyer?.Name} ({buyer?.Email}, {buyer?.Phone}) דיווחה: {text}. התשלום למוכרת מוקפא עד להחלטה.");
    }

    // תזכורת לקונה יומיים אחרי הרכישה: לאשר קבלה או לדווח על בעיה (נשאר יום)
    private async Task RemindBuyersAsync()
    {
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        foreach (var sale in (await _sales.GetAllAsync()).Where(s => s.Status == "Paid" && s.BuyerReminderSentAtUtc is null
                     && DateTime.UtcNow > s.CreatedAtUtc + TimeSpan.FromDays(2)))
        {
            sale.BuyerReminderSentAtUtc = DateTime.UtcNow;
            await _sales.UpdateAsync(sale);
            var buyer = await _users.GetByIdAsync(sale.BuyerId);
            if (buyer is null)
                continue;
            var seller = await _users.GetByIdAsync(sale.SellerId);
            if (seller is not null && sale.HandedOverAtUtc is null && sale.NotHandedAtUtc is null)
            {
                var sellerLink = $"{baseUrl}/purchase/{sale.Id}?t={sale.SellerToken}&action=handed";
                await SendAsync(seller, $"מסרת את {sale.ItemName}? - common",
                    $"<p>הקונה עדיין לא אישרה שקיבלה את <strong>{H(sale.ItemName)}</strong>.</p><p>הפריט כבר נמסר לה?</p>" +
                    $"<p><a href=\"{H(sellerLink)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>מסרתי / עוד לא נמסר</strong></a></p>");
            }
            var link = $"{baseUrl}/purchase/{sale.Id}?t={sale.BuyerToken}";
            var pickup = sale.DeliveryMethod == "SelfPickup"
                ? "<p>עוד לא אספת? אפשר לתאם עם המוכרת - הפרטים בקישור למטה.</p>"
                : string.Empty;
            await SendAsync(buyer, $"קיבלת את {sale.ItemName}? - common",
                $"<p>הפריט <strong>{H(sale.ItemName)}</strong> כבר אצלך? אשרי שקיבלת אותו.</p>" + pickup +
                $"<p><a href=\"{H(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>אישור קבלה / דיווח על בעיה</strong></a></p>");
        }
    }

    public async Task<IReadOnlyList<object>> MineAsync(string userId)
    {
        var rows = new List<object>();
        var mine = (await _sales.GetAllAsync()).Where(s => s.BuyerId == userId || s.SellerId == userId).OrderByDescending(s => s.CreatedAtUtc).ToList();
        var items = await _items.GetByIdsAsync(mine.Select(s => s.ItemId));
        foreach (var s in mine)
        {
            var item = items.GetValueOrDefault(s.ItemId);
            var buyer = s.BuyerId == userId;
            rows.Add(new
            {
                s.Id,
                Role = buyer ? "Buyer" : "Seller",
                s.ItemName,
                Image = item is null ? null : ImageLinks.Thumb(item.Id, item.ImageDataUrl),
                s.Price,
                s.Status,
                s.CreatedAtUtc,
                Token = buyer ? s.BuyerToken : s.SellerToken
            });
        }
        return rows;
    }

    public async Task AutoConfirmAsync()
    {
        try { await RemindBuyersAsync(); } catch { }
        foreach (var sale in (await _sales.GetAllAsync()).Where(s => s.Status == "Paid" && DateTime.UtcNow > s.CreatedAtUtc + ConfirmWindow
                     && !(s.NotHandedAtUtc is not null && s.HandedOverAtUtc is null)))
        {
            try { await ConfirmReceivedAsync(sale, auto: true); } catch { }
        }
    }

    public async Task<object?> GetViewAsync(string id, string? token, string? userId)
    {
        var found = await ResolveAsync(id, token, userId);
        return found is null ? null : await ViewAsync(found.Value.Sale, found.Value.Role);
    }

    public async Task<object> ConfirmReceivedAsync(string id, string? token, string? userId)
    {
        var sale = await ResolveForAsync(id, token, userId, "Buyer", "הרכישה לא נמצאה");
        await ConfirmReceivedAsync(sale);
        return await ViewAsync(sale, "Buyer");
    }

    public async Task<object> MarkHandedAsync(string id, string? token, string? userId, bool delivered)
    {
        var sale = await ResolveForAsync(id, token, userId, "Seller", "המכירה לא נמצאה");
        await MarkHandedAsync(sale, delivered);
        return await ViewAsync(sale, "Seller");
    }

    public async Task<object> ReportProblemAsync(string id, string? token, string? userId, string? text)
    {
        var sale = await ResolveForAsync(id, token, userId, "Buyer", "הרכישה לא נמצאה");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("יש לתאר את הבעיה");
        await ReportProblemAsync(sale, text);
        return await ViewAsync(sale, "Buyer");
    }

    private async Task<Sale> ResolveForAsync(string id, string? token, string? userId, string role, string notFound)
    {
        var found = await ResolveAsync(id, token, userId);
        if (found is null || found.Value.Role != role)
            throw new KeyNotFoundException(notFound);
        return found.Value.Sale;
    }

    public async Task<IReadOnlyList<object>> GetAdminListAsync()
    {
        var users = (await _users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);
        return (await _sales.GetAllAsync())
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s =>
            {
                var seller = users.GetValueOrDefault(s.SellerId);
                var buyer = users.GetValueOrDefault(s.BuyerId);
                return (object)new
                {
                    s.Id, s.ItemId, s.ItemName, s.PaymentMethod, s.Price, s.DeliveryFee, s.Commission, s.SellerAmount, s.DeliveryMethod, s.DeliveryAddress,
                    s.TrackingId, s.DispatchError, s.Status, s.HandedOverAtUtc, s.NotHandedAtUtc, s.ProblemText, s.CreatedAtUtc, s.ReceivedAtUtc, s.AutoConfirmed, s.SellerPaidAtUtc,
                    s.PaymentTransactionId,
                    SellerName = seller?.Name, SellerPhone = seller?.Phone, SellerEmail = seller?.Email,
                    BuyerName = buyer?.Name, BuyerPhone = buyer?.Phone, BuyerEmail = buyer?.Email
                };
            })
            .ToList();
    }

    public async Task AdminConfirmReceivedAsync(string id)
    {
        var sale = await _sales.GetByIdAsync(id) ?? throw new KeyNotFoundException("המכירה לא נמצאה");
        sale.Status = "Paid";
        await ConfirmReceivedAsync(sale);
    }

    public async Task MarkSellerPaidAsync(string id)
    {
        var sale = await _sales.GetByIdAsync(id) ?? throw new KeyNotFoundException("המכירה לא נמצאה");
        sale.SellerPaidAtUtc = DateTime.UtcNow;
        await _sales.UpdateAsync(sale);
    }

    private async Task NotifyCreatedAsync(Sale sale, Item item, User buyer, User? seller, EmailAttachment? proof = null)
    {
        var paid = sale.PaymentMethod switch
        {
            "Cash" => $"<p>התשלום (₪{sale.Price:0.##}) במזומן למוכרת בזמן האיסוף.</p>",
            "BankTransfer" => $"<p>שילמת ₪{sale.Price:0.##} בהעברה בנקאית ישירות למוכרת.</p>",
            _ => $"<p>התשלום התקבל (₪{sale.Price + sale.DeliveryFee:0.##}).</p>"
        };
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var button = "display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";
        var buyerLink = $"{baseUrl}/purchase/{sale.Id}?t={sale.BuyerToken}";
        var delivery = sale.DeliveryMethod == "Delivery"
            ? (sale.DispatchError is null
                ? $"<p>הזמנו שליח שיאסוף את הפריט מהמוכרת ויביא אותו אלייך ({H(sale.DeliveryAddress)}).</p>"
                : "<p>נזמין שליח בהקדם ונעדכן אותך.</p>")
            : $"<p><strong>איסוף עצמי:</strong> {H($"{item.Address}, {item.City}")}<br/>המוכרת: {H(seller?.Name)}, {H(seller?.Phone)} - אפשר לתאם איתה את מועד האיסוף.</p>";
        await SendAsync(buyer, $"רכשת את {sale.ItemName} - common",
            $"<p><strong>{H(sale.ItemName)}</strong> שלך!</p>" + paid + delivery +
            "<p>כשהפריט אצלך - אשרי שקיבלת. אם משהו לא תואם לתיאור, אפשר לדווח עד 3 ימים מהרכישה.</p>" +
            $"<p><a href=\"{H(buyerLink)}\" style=\"{button}\"><strong>אישור קבלה / דיווח על בעיה</strong></a></p>");

        if (seller is not null)
        {
            var sellerLink = $"{baseUrl}/purchase/{sale.Id}?t={sale.SellerToken}";
            var how = sale.DeliveryMethod == "Delivery"
                ? (sale.DispatchError is null ? "<p>הזמנו שליח שיגיע לאסוף את הפריט מהכתובת שלך. כדאי להכין אותו ארוז.</p>" : "<p>נזמין שליח לאיסוף בהקדם ונעדכן אותך.</p>")
                : $"<p>הקונה תגיע לאסוף בעצמה: {H(buyer.Name)}, {H(buyer.Phone)}. היא תתאם איתך את המועד.</p>";
            var money = sale.PaymentMethod switch
            {
                "Cash" => $"<p>הקונה תשלם לך ₪{sale.Price:0.##} במזומן באיסוף.</p>",
                "BankTransfer" => $"<p>הקונה העבירה ₪{sale.Price:0.##} לחשבון הבנק שלך - האסמכתא מצורפת.</p>",
                _ => "<p>התשלום יועבר אלייך אחרי שהקונה תאשר שקיבלה את הפריט (או אוטומטית אחרי 3 ימים).</p>"
            };
            await SendAsync(seller, $"מכרת את {sale.ItemName}! - common",
                $"<p><strong>{H(sale.ItemName)}</strong> נמכר ב-₪{sale.Price:0.##}.</p>" + how + money +
                $"<p><a href=\"{H(sellerLink)}&action=handed\" style=\"{button}\"><strong>מסרתי את הפריט</strong></a> &nbsp; <a href=\"{H(sellerLink)}\">פרטי המכירה</a></p>",
                proof is null ? null : new[] { proof });
        }

        await NotifyInboxAsync($"מכירה חדשה: {sale.ItemName}",
            $"{seller?.Name} מכרה ל{buyer.Name} ב-₪{sale.Price:0.##} ({(sale.DeliveryMethod == "Delivery" ? "שליח" : "איסוף עצמי")})." +
            (sale.DispatchError is null ? string.Empty : $" <strong>הזמנת השליח נכשלה: {H(sale.DispatchError)} - צריך להזמין ידנית.</strong>"));
    }

    private async Task SendAsync(User to, string subject, string body, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        if (!_email.IsEnabled || string.IsNullOrWhiteSpace(to.Email))
            return;
        try
        {
            await _email.SendAsync(to.Email, to.Name, subject,
                $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>שלום {H(NameText.FirstName(to.Name))},</p>{body}<p>common</p></div>", attachments);
        }
        catch { }
    }

    private async Task NotifyInboxAsync(string subject, string text)
    {
        if (!_email.IsEnabled)
            return;
        try { await _email.SendAsync(InboxEmail, "common", subject, $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>{text}</p></div>"); }
        catch { }
    }

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
