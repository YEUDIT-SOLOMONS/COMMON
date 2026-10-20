namespace PhotoRental.Service.Services;

using System.Net;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class SaleCreateDto
{
    public string ItemId { get; set; } = string.Empty;
    public List<string>? ExtraItemIds { get; set; }
    public string DeliveryMethod { get; set; } = "SelfPickup";
    public DeliveryAddressDto? DeliveryAddress { get; set; }
    public string PaymentMethod { get; set; } = "CreditCard";
    public string? PaymentTransactionId { get; set; }
    public string? PaymentProofDataUrl { get; set; }
    public string? PaymentProofFileName { get; set; }
    public string? ShippingPaymentId { get; set; }
}

// מכירות יד 2: תשלום דרך האתר, 6% עמלה מהמוכרת, העברה למוכרת אחרי שהקונה אישרה קבלה (או אוטומטית אחרי 3 ימים)
public class SaleService
{
    public const decimal CommissionRate = 0.06m;
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

    private readonly IShippingPaymentService _shippingPayments;

    public SaleService(ISaleRepository sales, IItemRepository items, IUserRepository users, ILoanService loans,
        IOrderPaymentService payments, IEmailService email, ClientOptions client, IOutboundCallService outbound,
        IShippingPaymentService shippingPayments)
    {
        _shippingPayments = shippingPayments;
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

    public async Task<Sale> CreateAsync(string buyerId, SaleCreateDto dto)
    {
        var method = dto.PaymentMethod is "Cash" or "BankTransfer" ? dto.PaymentMethod : "CreditCard";
        var card = method == "CreditCard";
        var paymentId = card ? dto.PaymentTransactionId ?? string.Empty : string.Empty;
        var all = await _sales.GetAllAsync();
        if (card && all.FirstOrDefault(s => s.PaymentTransactionId == paymentId && s.BuyerId == buyerId) is { } existing)
            return existing;
        if (card && all.Any(s => s.PaymentTransactionId == paymentId))
            throw new InvalidOperationException("התשלום הזה כבר שימש לרכישה אחרת");

        var items = new List<Item> { await SaleItemAsync(dto.ItemId) };
        foreach (var extraId in (dto.ExtraItemIds ?? new()).Where(id => !string.IsNullOrEmpty(id) && id != dto.ItemId).Distinct())
            items.Add(await SaleItemAsync(extraId));
        var item = items[0];
        var many = items.Count > 1;
        if (items.Any(i => i.OwnerId != item.OwnerId))
            throw new ArgumentException("אפשר לקנות יחד רק פריטים של אותה מוכרת");
        var buyer = await _users.GetByIdAsync(buyerId) ?? throw new KeyNotFoundException("המשתמשת לא נמצאה");
        if (item.OwnerId == buyerId)
            throw new InvalidOperationException("אי אפשר לקנות פריט שלך");

        var delivery = dto.DeliveryMethod == "Delivery";
        if (delivery && method == "Cash")
            throw new ArgumentException("משלוח עם שליח זמין בתשלום באשראי או בהעברה בנקאית");
        if (!card && items.FirstOrDefault(i => i.SoldAtUtc is not null) is { } soldBefore)
            throw new InvalidOperationException(many ? $"מצטערות, {soldBefore.Name} כבר נמכר" : "מצטערות, הפריט כבר נמכר");
        var fee = delivery ? await _loans.GetSaleDeliveryFeeAsync(item, dto.DeliveryAddress ?? new()) : 0m;
        var price = items.Sum(i => i.SalePrice);
        if (card)
            await _payments.VerifyAsync(buyer, price + fee, paymentId);
        var shippingCharge = delivery && method == "BankTransfer"
            ? await _shippingPayments.ChargeAsync(buyer, fee, dto.ShippingPaymentId)
            : null;

        EmailAttachment? proofAttachment = null;
        string? proofId = null;
        if (method == "BankTransfer")
            (proofId, proofAttachment) = await _loans.StoreSaleProofAsync(dto.PaymentProofDataUrl, dto.PaymentProofFileName);

        if (items.FirstOrDefault(i => i.SoldAtUtc is not null) is { } sold)
        {
            await NotifyInboxAsync($"תשלום על פריט שכבר נמכר - צריך החזר", $"{buyer.Name} ({buyer.Email}) שילמה ₪{price + fee:0.##} על \"{string.Join(", ", items.Select(i => i.Name))}\", ו\"{sold.Name}\" כבר נמכר. מזהה תשלום: {paymentId}");
            throw new InvalidOperationException(many
                ? $"מצטערות, {sold.Name} נמכר בדיוק עכשיו למישהי אחרת. נחזיר לך את התשלום בהקדם."
                : "מצטערות, הפריט נמכר בדיוק עכשיו למישהי אחרת. נחזיר לך את התשלום בהקדם.");
        }

        foreach (var i in items)
        {
            i.SoldAtUtc = DateTime.UtcNow;
            await _items.UpdateAsync(i.Id!, i);
        }

        string? trackingId = null, providerId = null, addressText = null, dispatchError = null;
        if (delivery)
        {
            try
            {
                (trackingId, providerId, addressText) = await _loans.DispatchSaleAsync(item, buyer, dto.DeliveryAddress!);
            }
            catch (Exception ex)
            {
                dispatchError = ex.Message;
            }
        }

        var orderId = many ? NewToken() : null;
        var sales = new List<Sale>();
        foreach (var i in items)
        {
            var commission = Math.Round(i.SalePrice * CommissionRate, 2, MidpointRounding.AwayFromZero);
            sales.Add(await _sales.CreateAsync(new Sale
            {
                ItemId = i.Id!,
                ItemName = i.Name,
                OrderId = orderId,
                SellerId = i.OwnerId ?? string.Empty,
                BuyerId = buyerId,
                Price = i.SalePrice,
                DeliveryFee = sales.Count == 0 ? fee : 0m,
                CommissionRate = CommissionRate,
                Commission = commission,
                SellerAmount = i.SalePrice - commission,
                DeliveryMethod = delivery ? "Delivery" : "SelfPickup",
                DeliveryAddress = addressText,
                TrackingId = trackingId,
                ProviderId = providerId,
                DispatchError = dispatchError,
                PaymentMethod = method,
                PaymentTransactionId = paymentId,
                PaymentProofId = proofId,
                ShippingChargeStatus = sales.Count == 0 ? shippingCharge?.Status : null,
                ShippingPaymentId = sales.Count == 0 ? shippingCharge?.PaymentId : null,
                BuyerToken = NewToken(),
                SellerToken = NewToken(),
                CreatedAtUtc = DateTime.UtcNow
            }));
        }

        var sale = sales[0];
        var seller = await _users.GetByIdAsync(sale.SellerId);
        await NotifyCreatedAsync(sales, item, buyer, seller, proofAttachment);
        if (seller is not null)
        {
            var how = sale.DeliveryMethod == "Delivery"
                ? $"שליח יגיע לאסוף את {(many ? "הפריטים" : "הפריט")} מהכתובת שלך, כדאי להכין {(many ? "אותם" : "אותו")} ארוז."
                : sale.PaymentMethod == "Cash"
                    ? $"הקונה, {NameText.FirstName(buyer.Name)}, תיצור איתך קשר לתיאום האיסוף, ותשלם לך במזומן."
                    : $"הקונה, {NameText.FirstName(buyer.Name)}, תיצור איתך קשר לתיאום האיסוף.";
            try
            {
                await _outbound.CallMessageAsync(seller, CallKind.SaleNotice,
                    $"שלום {NameText.FirstName(seller.Name)}, זו הודעה מקומון. מכרת את {Names(sales)} ב-{price:0.##} שקלים! {how} כל הפרטים נשלחו אלייך במייל. תודה.",
                    Names(sales));
            }
            catch
            {
                // המייל כבר נשלח
            }
        }
        return sale;
    }

    // כל רשומות ההזמנה (פריט אחד או כמה שנקנו יחד), במצבן העדכני
    private async Task<List<Sale>> OrderOfAsync(Sale sale)
    {
        if (string.IsNullOrEmpty(sale.OrderId))
            return new() { await _sales.GetByIdAsync(sale.Id!) ?? sale };
        return (await _sales.GetAllAsync()).Where(s => s.OrderId == sale.OrderId).OrderBy(s => s.CreatedAtUtc).ToList();
    }

    private async Task<List<Sale>> UpdateOrderAsync(Sale sale, Func<Sale, bool> where, Action<Sale> change)
    {
        var targets = (await OrderOfAsync(sale)).Where(where).ToList();
        foreach (var s in targets)
        {
            change(s);
            await _sales.UpdateAsync(s);
        }
        if (targets.Any(s => s.Id == sale.Id))
            change(sale);
        return targets;
    }

    private static string Names(IReadOnlyList<Sale> sales)
    {
        if (sales.Count == 1)
            return sales[0].ItemName;
        var last = sales[^1].ItemName;
        var and = last.Length > 0 && last[0] is >= 'א' and <= 'ת' ? "ו" : "ו-";
        return $"{string.Join(", ", sales.Take(sales.Count - 1).Select(s => s.ItemName))} {and}{last}";
    }

    public async Task<(Sale Sale, string Role)?> ResolveAsync(string id, string? token, string? userId)
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

    public async Task<object> ViewAsync(Sale sale, string role)
    {
        var order = (await OrderOfAsync(sale)).Select(s => s.Id == sale.Id ? sale : s).ToList();
        var item = await _items.GetByIdAsync(sale.ItemId);
        var other = await _users.GetByIdAsync(role == "Buyer" ? sale.SellerId : sale.BuyerId);
        var lines = new List<object>();
        if (order.Count > 1)
            foreach (var s in order)
            {
                var orderItem = s.Id == sale.Id ? item : await _items.GetByIdAsync(s.ItemId);
                lines.Add(new { s.ItemId, s.ItemName, s.Price, Image = orderItem is null ? null : ImageLinks.Thumb(orderItem.Id, orderItem.ImageDataUrl) });
            }
        var price = order.Sum(s => s.Price);
        var fee = order.Sum(s => s.DeliveryFee);
        return new
        {
            sale.Id,
            Role = role,
            ItemName = Names(order),
            sale.ItemId,
            Items = lines,
            Image = item is null ? null : ImageLinks.Thumb(item.Id, item.ImageDataUrl),
            Price = price,
            DeliveryFee = fee,
            Total = price + fee,
            Commission = order.Sum(s => s.Commission),
            SellerAmount = order.Sum(s => s.SellerAmount),
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

    public async Task ConfirmReceivedAsync(Sale sale, bool auto = false)
    {
        if (sale.Status != "Paid")
            return;
        var now = DateTime.UtcNow;
        var done = await UpdateOrderAsync(sale, s => s.Status != "Received", s =>
        {
            s.Status = "Received";
            s.ReceivedAtUtc = now;
            s.AutoConfirmed = auto;
        });
        if (done.Count == 0)
            return;

        var many = done.Count > 1;
        var names = Names(done);
        var sellerAmount = done.Sum(s => s.SellerAmount);
        var seller = await _users.GetByIdAsync(sale.SellerId);
        var viaSite = sale.PaymentMethod == "CreditCard";
        if (seller is not null)
            await SendAsync(seller, $"הקונה קיבלה את {names} - common",
                $"<p>{(auto ? "עברו 3 ימים מהרכישה בלי דיווח על בעיה" : many ? "הקונה אישרה שקיבלה את הפריטים" : "הקונה אישרה שקיבלה את הפריט")} - <strong>{H(names)}</strong>.</p>" +
                (viaSite
                    ? $"<p>₪{sellerAmount:0.##} (מחיר המכירה פחות 6% עמלה) יועברו אלייך בימים הקרובים.</p>"
                    : $"<p>עמלת השירות (₪{done.Sum(s => s.Commission):0.##}) תיכלל בדוח העמלות החודשי שלך.</p>"));
        if (viaSite)
            await NotifyInboxAsync($"מכירה לתשלום למוכרת: {names}",
                $"{seller?.Name} - להעביר ₪{sellerAmount:0.##} ({(auto ? "אישור אוטומטי" : "הקונה אישרה קבלה")}). מסך הניהול - לשונית מכירות.");
    }

    // המוכרת מסמנת אם מסרה את הפריט. "עוד לא נמסר" עוצר את האישור האוטומטי ומעדכן את הנהלת האתר
    public async Task MarkHandedAsync(Sale sale, bool handed)
    {
        var now = DateTime.UtcNow;
        var order = await UpdateOrderAsync(sale, _ => true, s =>
        {
            s.HandedOverAtUtc = handed ? now : null;
            s.NotHandedAtUtc = handed ? null : now;
        });
        if (!handed)
        {
            var seller = await _users.GetByIdAsync(sale.SellerId);
            var buyer = await _users.GetByIdAsync(sale.BuyerId);
            await NotifyInboxAsync($"מכירה שלא נמסרה: {Names(order)}",
                $"{seller?.Name} ({seller?.Phone}) סימנה ש{(order.Count > 1 ? "הפריטים עוד לא נמסרו" : "הפריט עוד לא נמסר")} ל{buyer?.Name} ({buyer?.Phone}). האישור האוטומטי נעצר.");
        }
    }

    public async Task ReportProblemAsync(Sale sale, string text)
    {
        if (sale.Status != "Paid")
            throw new InvalidOperationException("כבר אישרת קבלה של הפריט");
        if (DateTime.UtcNow > sale.CreatedAtUtc + ConfirmWindow)
            throw new InvalidOperationException("עברו 3 ימים מהרכישה. אפשר לפנות אלינו בדף צור קשר");
        var order = await UpdateOrderAsync(sale, s => s.Status == "Paid", s =>
        {
            s.Status = "Problem";
            s.ProblemText = text.Trim();
        });
        var buyer = await _users.GetByIdAsync(sale.BuyerId);
        await NotifyInboxAsync($"דיווח על בעיה במכירה: {Names(order)}",
            $"{buyer?.Name} ({buyer?.Email}, {buyer?.Phone}) דיווחה: {text}. התשלום למוכרת מוקפא עד להחלטה.");
    }

    // תזכורת לקונה יומיים אחרי הרכישה: לאשר קבלה או לדווח על בעיה (נשאר יום)
    private async Task RemindBuyersAsync()
    {
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var reminded = new HashSet<string>();
        foreach (var sale in (await _sales.GetAllAsync()).Where(s => s.Status == "Paid" && s.BuyerReminderSentAtUtc is null
                     && DateTime.UtcNow > s.CreatedAtUtc + TimeSpan.FromDays(2)))
        {
            if (sale.OrderId is not null && !reminded.Add(sale.OrderId))
                continue;
            var now = DateTime.UtcNow;
            var order = await UpdateOrderAsync(sale, s => s.BuyerReminderSentAtUtc is null, s => s.BuyerReminderSentAtUtc = now);
            var many = order.Count > 1;
            var names = Names(order);
            var buyer = await _users.GetByIdAsync(sale.BuyerId);
            if (buyer is null)
                continue;
            var seller = await _users.GetByIdAsync(sale.SellerId);
            if (seller is not null && sale.HandedOverAtUtc is null && sale.NotHandedAtUtc is null)
            {
                var sellerLink = $"{baseUrl}/purchase/{sale.Id}?t={sale.SellerToken}&action=handed";
                await SendAsync(seller, $"מסרת את {names}? - common",
                    $"<p>הקונה עדיין לא אישרה שקיבלה את <strong>{H(names)}</strong>.</p><p>{(many ? "הפריטים כבר נמסרו לה?" : "הפריט כבר נמסר לה?")}</p>" +
                    $"<p><a href=\"{H(sellerLink)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>מסרתי / עוד לא נמסר</strong></a></p>");
            }
            var link = $"{baseUrl}/purchase/{sale.Id}?t={sale.BuyerToken}";
            var pickup = sale.DeliveryMethod == "SelfPickup"
                ? "<p>עוד לא אספת? אפשר לתאם עם המוכרת - הפרטים בקישור למטה.</p>"
                : string.Empty;
            await SendAsync(buyer, $"קיבלת את {names}? - common",
                (many
                    ? $"<p>הפריטים <strong>{H(names)}</strong> כבר אצלך? אשרי שקיבלת אותם.</p>"
                    : $"<p>הפריט <strong>{H(names)}</strong> כבר אצלך? אשרי שקיבלת אותו.</p>") + pickup +
                $"<p><a href=\"{H(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>אישור קבלה / דיווח על בעיה</strong></a></p>");
        }
    }

    public async Task<IReadOnlyList<object>> MineAsync(string userId)
    {
        var rows = new List<object>();
        foreach (var s in (await _sales.GetAllAsync()).Where(s => s.BuyerId == userId || s.SellerId == userId).OrderByDescending(s => s.CreatedAtUtc))
        {
            var item = await _items.GetByIdAsync(s.ItemId);
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

    public Task<IReadOnlyList<Sale>> GetAllAsync() => _sales.GetAllAsync();

    public async Task MarkSellerPaidAsync(string id)
    {
        var sale = await _sales.GetByIdAsync(id) ?? throw new KeyNotFoundException("המכירה לא נמצאה");
        sale.SellerPaidAtUtc = DateTime.UtcNow;
        await _sales.UpdateAsync(sale);
    }

    private async Task NotifyCreatedAsync(IReadOnlyList<Sale> sales, Item item, User buyer, User? seller, EmailAttachment? proof = null)
    {
        var sale = sales[0];
        var many = sales.Count > 1;
        var names = Names(sales);
        var price = sales.Sum(s => s.Price);
        var fee = sales.Sum(s => s.DeliveryFee);
        var paid = sale.PaymentMethod switch
        {
            "Cash" => $"<p>התשלום (₪{price:0.##}) במזומן למוכרת בזמן האיסוף.</p>",
            "BankTransfer" => $"<p>שילמת ₪{price:0.##} בהעברה בנקאית ישירות למוכרת.</p>" +
                              (fee > 0 ? $"<p>דמי המשלוח (₪{fee:0.##}) נגבו מכרטיס האשראי שלך.</p>" : string.Empty),
            _ => $"<p>התשלום התקבל (₪{price + fee:0.##}).</p>"
        };
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var button = "display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";
        var buyerLink = $"{baseUrl}/purchase/{sale.Id}?t={sale.BuyerToken}";
        var delivery = sale.DeliveryMethod == "Delivery"
            ? (sale.DispatchError is null
                ? $"<p>הזמנו שליח שיאסוף את {(many ? "הפריטים" : "הפריט")} מהמוכרת ויביא {(many ? "אותם" : "אותו")} אלייך ({H(sale.DeliveryAddress)}).</p>"
                : "<p>נזמין שליח בהקדם ונעדכן אותך.</p>")
            : $"<p><strong>איסוף עצמי:</strong> {H($"{item.Address}, {item.City}")}<br/>המוכרת: {H(seller?.Name)}, {H(seller?.Phone)} - אפשר לתאם איתה את מועד האיסוף.</p>";
        await SendAsync(buyer, $"רכשת את {names} - common",
            $"<p><strong>{H(names)}</strong> שלך!</p>" + paid + delivery +
            (many
                ? "<p>כשהפריטים אצלך - אשרי שקיבלת. אם משהו לא תואם לתיאור, אפשר לדווח עד 3 ימים מהרכישה.</p>"
                : "<p>כשהפריט אצלך - אשרי שקיבלת. אם משהו לא תואם לתיאור, אפשר לדווח עד 3 ימים מהרכישה.</p>") +
            $"<p><a href=\"{H(buyerLink)}\" style=\"{button}\"><strong>אישור קבלה / דיווח על בעיה</strong></a></p>");

        if (seller is not null)
        {
            var sellerLink = $"{baseUrl}/purchase/{sale.Id}?t={sale.SellerToken}";
            var how = sale.DeliveryMethod == "Delivery"
                ? (sale.DispatchError is null
                    ? $"<p>הזמנו שליח שיגיע לאסוף את {(many ? "הפריטים" : "הפריט")} מהכתובת שלך. כדאי להכין {(many ? "אותם" : "אותו")} ארוז.</p>"
                    : "<p>נזמין שליח לאיסוף בהקדם ונעדכן אותך.</p>")
                : $"<p>הקונה תגיע לאסוף בעצמה: {H(buyer.Name)}, {H(buyer.Phone)}. היא תתאם איתך את המועד.</p>";
            var money = sale.PaymentMethod switch
            {
                "Cash" => $"<p>הקונה תשלם לך ₪{price:0.##} במזומן באיסוף.</p>",
                "BankTransfer" => $"<p>הקונה העבירה ₪{price:0.##} לחשבון הבנק שלך - האסמכתא מצורפת.</p>",
                _ => many
                    ? "<p>התשלום יועבר אלייך אחרי שהקונה תאשר שקיבלה את הפריטים (או אוטומטית אחרי 3 ימים).</p>"
                    : "<p>התשלום יועבר אלייך אחרי שהקונה תאשר שקיבלה את הפריט (או אוטומטית אחרי 3 ימים).</p>"
            };
            await SendAsync(seller, $"מכרת את {names}! - common",
                $"<p><strong>{H(names)}</strong> {(many ? "נמכרו" : "נמכר")} ב-₪{price:0.##}.</p>" + how + money +
                $"<p><a href=\"{H(sellerLink)}&action=handed\" style=\"{button}\"><strong>{(many ? "מסרתי את הפריטים" : "מסרתי את הפריט")}</strong></a> &nbsp; <a href=\"{H(sellerLink)}\">פרטי המכירה</a></p>",
                proof is null ? null : new[] { proof });
        }

        await NotifyInboxAsync($"מכירה חדשה: {names}",
            $"{seller?.Name} מכרה ל{buyer.Name} ב-₪{price:0.##} ({(sale.DeliveryMethod == "Delivery" ? "שליח" : "איסוף עצמי")})." +
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
