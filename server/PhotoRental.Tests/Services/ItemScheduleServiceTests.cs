using Moq;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;
using PhotoRental.Service.Services;

namespace PhotoRental.Tests.Services;

public class ItemScheduleServiceTests
{
    private readonly Mock<ILoanRepository> _loans = new();
    private readonly Mock<IItemBlockRepository> _blocks = new();
    private readonly Item _item = new() { Id = "item-1", Name = "עדשה", Quantity = 1 };

    public ItemScheduleServiceTests()
    {
        _loans.Setup(r => r.GetByStatusAsync(LoanStatus.Overdue)).ReturnsAsync(new List<Loan>());
        _blocks.Setup(r => r.GetByItemAsync(It.IsAny<string>())).ReturnsAsync(new List<ItemBlock>());
    }

    private ItemScheduleService CreateService() => new(_loans.Object, Mock.Of<IItemRepository>(), _blocks.Object, TestMapper.Instance);

    private void GivenActiveLoans(params Loan[] loans) =>
        _loans.Setup(r => r.GetActiveLoansForItemAsync("item-1")).ReturnsAsync(loans.ToList());

    private static Loan LoanFor(DateTime start, int days, int quantity = 1) => new()
    {
        Id = Guid.NewGuid().ToString(),
        ItemCopyId = "item-1",
        StartDate = start,
        RentalDays = days,
        ExpectedReturnDate = start.AddDays(days),
        Quantity = quantity,
        Status = LoanStatus.Active
    };

    [Fact]
    public async Task EnsureFree_WhenNoOverlappingLoans_Passes()
    {
        var start = DateTime.Today.AddDays(10);
        GivenActiveLoans(LoanFor(start.AddDays(-5), 2));

        await CreateService().EnsureFreeAsync(_item, start, start.AddDays(2));
    }

    [Fact]
    public async Task EnsureFree_WhenSingleCopyIsAlreadyBooked_Throws()
    {
        var start = DateTime.Today.AddDays(10);
        GivenActiveLoans(LoanFor(start, 3));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().EnsureFreeAsync(_item, start.AddDays(1), start.AddDays(2)));
    }

    [Fact]
    public async Task EnsureFree_WhenSomeUnitsRemain_AllowsUpToRemaining()
    {
        _item.Quantity = 3;
        var start = DateTime.Today.AddDays(10);
        GivenActiveLoans(LoanFor(start, 3, quantity: 2));

        await CreateService().EnsureFreeAsync(_item, start, start.AddDays(1), units: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().EnsureFreeAsync(_item, start, start.AddDays(1), units: 2));
    }
}
