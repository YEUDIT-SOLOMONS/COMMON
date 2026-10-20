using Moq;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Exceptions;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Service.Services;

namespace PhotoRental.Tests.Services;

public class LoanServiceTests
{
    private readonly Mock<ILoanRepository> _loans = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IItemScheduleService> _schedule = new();

    private readonly Item _item = new()
    {
        Id = "item-1",
        Name = "מצלמה",
        DailyPrice = 100,
        Quantity = 1,
        OwnerId = "owner-1",
        Version = 3,
        ConcurrencyToken = "2026-10-18T10:00:00.000000Z"
    };

    public LoanServiceTests()
    {
        _items.Setup(r => r.GetByIdAsync(_item.Id!, It.IsAny<CancellationToken>())).ReturnsAsync(_item);
        _users.Setup(r => r.GetByIdAsync("renter-1", It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = "renter-1", Name = "שוכרת", Phone = "0501234567" });
        _users.Setup(r => r.GetByIdAsync("owner-1", It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = "owner-1", Name = "משכירה", Phone = "0507654321" });
        _loans.Setup(r => r.CreateWithItemCheckAsync(It.IsAny<IReadOnlyList<(Loan Loan, Item Item)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<(Loan Loan, Item Item)> b, CancellationToken _) =>
            {
                b[0].Loan.Id = "loan-1";
                return b.Select(x => x.Loan).ToList();
            });
    }

    private LoanService CreateService() => new(
        Mock.Of<IFavoriteRepository>(),
        _loans.Object,
        _items.Object,
        _users.Object,
        Mock.Of<IShippingService>(),
        Mock.Of<IEmailService>(),
        new CommissionOptions(),
        Mock.Of<IPaymentProofRepository>(),
        new ClientOptions(),
        Mock.Of<IOutboundCallService>(),
        Mock.Of<IShippingPaymentService>(),
        Mock.Of<IOrderPaymentService>(),
        Mock.Of<IExtraChargeService>(),
        Mock.Of<IMarkerRepository>(),
        _schedule.Object);

    private static LoanRequestDto Request() => new()
    {
        ItemId = "item-1",
        BorrowerId = "renter-1",
        StartDate = DateTime.Today.AddDays(7),
        RentalDays = 2,
        DeliveryMethod = "SelfPickup",
        ReturnMethod = "SelfDropoff",
        PaymentMethod = "Cash",
        TermsAccepted = true,
        PhoneConfirmed = true
    };

    [Fact]
    public async Task CreateAsync_WhenItemIsFree_SavesWithConcurrencyCheck()
    {
        var result = await CreateService().CreateAsync(Request());

        Assert.Equal("loan-1", result.Id);
        _loans.Verify(r => r.CreateWithItemCheckAsync(
            It.Is<IReadOnlyList<(Loan Loan, Item Item)>>(b => b.Count == 1 && b[0].Item.ConcurrencyToken == _item.ConcurrencyToken),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenItemWasTakenMeanwhile_ThrowsConflict()
    {
        _loans.Setup(r => r.CreateWithItemCheckAsync(It.IsAny<IReadOnlyList<(Loan Loan, Item Item)>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("stale", "item-1"));

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => CreateService().CreateAsync(Request()));

        Assert.Contains("נתפס", ex.Message);
        Assert.Equal("item-1", ex.ResourceId);
    }

    [Fact]
    public async Task CreateAsync_WhenDatesAreTaken_RejectsWithoutSaving()
    {
        _schedule.Setup(s => s.EnsureFreeAsync(It.IsAny<Item>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("כבר מושכר"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(Request()));

        _loans.Verify(r => r.CreateWithItemCheckAsync(It.IsAny<IReadOnlyList<(Loan Loan, Item Item)>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PassesCancellationTokenToSave()
    {
        using var cts = new CancellationTokenSource();

        await CreateService().CreateAsync(Request(), cts.Token);

        _loans.Verify(r => r.CreateWithItemCheckAsync(It.IsAny<IReadOnlyList<(Loan Loan, Item Item)>>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithoutTermsAccepted_ThrowsArgumentException()
    {
        var request = Request();
        request.TermsAccepted = false;

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().CreateAsync(request));
    }
}
