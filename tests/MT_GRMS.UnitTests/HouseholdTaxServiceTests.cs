using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class HouseholdTaxServiceTests
{
    private class FakeHouseholdTaxRepository : IHouseholdTaxRepository
    {
        public decimal AnnualAccumulatedRevenue { get; set; } = 800_000_000m;
        public decimal GrossRevenue { get; set; } = 250_000_000m;
        public decimal DiscountAmount { get; set; } = 10_000_000m;
        public decimal ReturnAmount { get; set; } = 5_000_000m;

        public List<AccountingBookEntryDto> BookEntries { get; set; } = new()
        {
            new AccountingBookEntryDto
            {
                VoucherNumber = "ORD-001",
                Amount = 235_000_000m,
                BookType = AccountingBookType.S2b_Revenue,
                BookCode = "S2b-HKD"
            },
            new AccountingBookEntryDto
            {
                VoucherNumber = "PC-001",
                Amount = 20_000_000m,
                BookType = AccountingBookType.S2c_Salary,
                BookCode = "S2c-HKD"
            },
            new AccountingBookEntryDto
            {
                VoucherNumber = "PC-002",
                Amount = 5_000_000m,
                BookType = AccountingBookType.S2d_Supplies,
                BookCode = "S2d-HKD"
            },
            new AccountingBookEntryDto
            {
                VoucherNumber = "PC-003",
                Amount = 15_000_000m,
                BookType = AccountingBookType.S2e_Services,
                BookCode = "S2e-HKD"
            }
        };

        public HouseholdTaxSnapshot? ExistingSnapshot { get; set; }
        public List<HouseholdTaxSnapshot> SavedSnapshots { get; } = new();

        public Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnnualAccumulatedRevenue);

        public Task<(decimal GrossRevenue, decimal DiscountAmount, decimal ReturnAmount)> GetPeriodRevenueAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((GrossRevenue, DiscountAmount, ReturnAmount));

        public Task<List<AccountingBookEntryDto>> GetBookEntriesAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            AccountingBookType? bookType,
            CancellationToken cancellationToken = default)
        {
            var result = bookType.HasValue
                ? BookEntries.Where(x => x.BookType == bookType.Value).ToList()
                : BookEntries;
            return Task.FromResult(result);
        }

        public Task<HouseholdTaxSnapshot?> GetSnapshotAsync(int year, TaxPeriodType periodType, int periodNumber, Guid? branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingSnapshot);

        public Task<HouseholdTaxSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedSnapshots.FirstOrDefault(s => s.Id == id) ?? ExistingSnapshot);

        public Task<List<HouseholdTaxSnapshot>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedSnapshots);

        public Task<HouseholdTaxSnapshot> AddSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            snapshot.Id = Guid.NewGuid();
            SavedSnapshots.Add(snapshot);
            ExistingSnapshot = snapshot;
            return Task.FromResult(snapshot);
        }
    }

    private readonly FakeHouseholdTaxRepository _fakeRepo = new();
    private readonly HouseholdTaxService _service;

    public HouseholdTaxServiceTests()
    {
        _service = new HouseholdTaxService(_fakeRepo);
    }

    [Fact]
    public async Task GetTaxSummary_Under1Billion_ShouldBeExemptFromTax_Group1()
    {
        // Arrange: Lũy kế năm 800 triệu (<= 1 tỷ)
        _fakeRepo.AnnualAccumulatedRevenue = 800_000_000m;
        _fakeRepo.GrossRevenue = 300_000_000m;
        _fakeRepo.DiscountAmount = 10_000_000m;
        _fakeRepo.ReturnAmount = 5_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert
        Assert.Equal(HouseholdTaxGroup.Group1_Under1B, result.HouseholdGroup);
        Assert.True(result.IsTaxExempt);
        Assert.Equal(285_000_000m, result.TaxableRevenueInPeriod); // 300M - 10M - 5M
        Assert.Equal(0m, result.VatRate);
        Assert.Equal(0m, result.VatAmount);
        Assert.Equal(0m, result.PitRate);
        Assert.Equal(0m, result.PitAmount);
        Assert.Equal(0m, result.TotalTaxAmount);
    }

    [Fact]
    public async Task GetTaxSummary_Between1BAnd3B_ShouldApplyRetailRates_Group2()
    {
        // Arrange: Lũy kế năm 1.8 tỷ (thuộc nhóm 2: 1B - 3B)
        _fakeRepo.AnnualAccumulatedRevenue = 1_800_000_000m;
        _fakeRepo.GrossRevenue = 400_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert
        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, result.HouseholdGroup);
        Assert.False(result.IsTaxExempt);
        Assert.Equal(400_000_000m, result.TaxableRevenueInPeriod);
        Assert.Equal(1.0m, result.VatRate);
        Assert.Equal(4_000_000m, result.VatAmount); // 400M * 1%
        Assert.Equal(0.5m, result.PitRate);
        Assert.Equal(2_000_000m, result.PitAmount); // 400M * 0.5%
        Assert.Equal(6_000_000m, result.TotalTaxAmount);
    }

    [Fact]
    public async Task GetTaxSummary_Between3BAnd50B_ShouldBeGroup3()
    {
        // Arrange: Lũy kế năm 10 tỷ
        _fakeRepo.AnnualAccumulatedRevenue = 10_000_000_000m;

        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Year, PeriodNumber = 1 };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert
        Assert.Equal(HouseholdTaxGroup.Group3_3BTo50B, result.HouseholdGroup);
        Assert.False(result.IsTaxExempt);
    }

    [Fact]
    public async Task GetTaxSummary_Over50B_ShouldBeGroup4()
    {
        // Arrange: Lũy kế năm 65 tỷ
        _fakeRepo.AnnualAccumulatedRevenue = 65_000_000_000m;

        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Year, PeriodNumber = 1 };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert
        Assert.Equal(HouseholdTaxGroup.Group4_Over50B, result.HouseholdGroup);
        Assert.False(result.IsTaxExempt);
    }

    [Fact]
    public async Task GetTaxSummary_BooksSummary_ShouldAggregateS2b_S2c_S2d_S2e_Correctly()
    {
        // Act
        var result = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 });

        // Assert
        Assert.Equal(235_000_000m, result.BooksSummary.S2bTotalRevenue);
        Assert.Equal(20_000_000m, result.BooksSummary.S2cTotalSalary);
        Assert.Equal(5_000_000m, result.BooksSummary.S2dTotalSupplies);
        Assert.Equal(15_000_000m, result.BooksSummary.S2eTotalServices);
        Assert.Equal(40_000_000m, result.BooksSummary.TotalDeductibleExpenses);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenNewPeriod_ShouldCreateSnapshotSuccessfully()
    {
        // Arrange
        _fakeRepo.ExistingSnapshot = null;
        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            Notes = "Chốt thuế quý 1"
        };

        // Act
        var snapshot = await _service.FinalizeTaxSnapshotAsync(request, "accountant-user");

        // Assert
        Assert.NotEqual(Guid.Empty, snapshot.Id);
        Assert.Equal("accountant-user", snapshot.FinalizedBy);
        Assert.Equal(2026, snapshot.Year);
        Assert.Equal(TaxPeriodType.Quarter, snapshot.PeriodType);
        Assert.Equal(1, snapshot.PeriodNumber);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenPeriodAlreadyFinalized_ShouldThrowBusinessException()
    {
        // Arrange: Đã tồn tại snapshot
        _fakeRepo.ExistingSnapshot = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.FinalizeTaxSnapshotAsync(request, "accountant-user"));

        Assert.Equal("TAX_PERIOD_LOCKED", ex.ErrorCode);
    }
}
