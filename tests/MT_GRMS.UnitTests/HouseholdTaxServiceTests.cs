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
        public decimal PriorPeriodsAccumulatedRevenue { get; set; } = 0m;
        public decimal GrossRevenue { get; set; } = 250_000_000m;
        public decimal DiscountAmount { get; set; } = 10_000_000m;
        public decimal ReturnAmount { get; set; } = 5_000_000m;
        public decimal DeductibleExpenses { get; set; } = 40_000_000m;

        public List<AccountingBookEntryDto> BookEntries { get; set; } = new();
        public HouseholdTaxSnapshot? ExistingSnapshot { get; set; }
        public List<HouseholdTaxSnapshot> SavedSnapshots { get; } = new();

        public Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default)
        {
            // Nếu ngày hỏi là trước kỳ hiện tại -> trả về PriorPeriodsAccumulatedRevenue
            if (upToDateUtc < new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc) && PriorPeriodsAccumulatedRevenue > 0)
            {
                return Task.FromResult(PriorPeriodsAccumulatedRevenue);
            }
            return Task.FromResult(AnnualAccumulatedRevenue);
        }

        public Task<(decimal GrossRevenue, decimal DiscountAmount, decimal ReturnAmount)> GetPeriodRevenueAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((GrossRevenue, DiscountAmount, ReturnAmount));

        public Task<decimal> GetPeriodDeductibleExpensesAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DeductibleExpenses);

        public Task<List<AccountingBookEntryDto>> GetBookEntriesAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            HouseholdTaxGroup group,
            TaxCalculationMethod method,
            AccountingBookType? bookType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BookEntries);
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
        Assert.Equal(285_000_000m, result.TaxableRevenueInPeriod);
        Assert.Equal(0m, result.VatRate);
        Assert.Equal(0m, result.VatAmount);
        Assert.Equal(0m, result.PitRate);
        Assert.Equal(0m, result.PitAmount);
        Assert.Equal(0m, result.TotalTaxAmount);
        Assert.Equal("S1a-HKD", result.BooksSummary.PrimaryBookCode);
    }

    [Fact]
    public async Task GetTaxSummary_WhenYearExceeds1Billion_ShouldCalculateVatOnTotalAndPitOnlyOnExcess()
    {
        // Arrange:
        // Kỳ trước (Quý 1): Doanh thu lũy kế 900 triệu (chưa vượt 1 tỷ)
        // Kỳ này (Quý 2): Doanh thu phát sinh 300 triệu -> Lũy kế thành 1.2 tỷ (vượt 1 tỷ)
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 900_000_000m;
        _fakeRepo.AnnualAccumulatedRevenue = 1_200_000_000m;
        _fakeRepo.GrossRevenue = 300_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert
        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, result.HouseholdGroup);
        Assert.False(result.IsTaxExempt);
        Assert.Equal(300_000_000m, result.TaxableRevenueInPeriod);

        // GTGT tính trên TOÀN BỘ doanh thu kỳ khi vượt 1 tỷ: 300M * 1% = 3.000.000 VNĐ
        Assert.Equal(1.0m, result.VatRate);
        Assert.Equal(300_000_000m, result.VatTaxableRevenue);
        Assert.Equal(3_000_000m, result.VatAmount);

        // TNCN CHỈ tính trên phần vượt 1 tỷ: (1.2 tỷ - 1.0 tỷ) - 0 = 200M * 0.5% = 1.000.000 VNĐ
        Assert.Equal(0.5m, result.PitRate);
        Assert.Equal(200_000_000m, result.PitTaxableRevenue);
        Assert.Equal(1_000_000m, result.PitAmount);

        // Tổng thuế = 3M (GTGT) + 1M (TNCN) = 4.000.000 VNĐ
        Assert.Equal(4_000_000m, result.TotalTaxAmount);
        Assert.Equal("S2a-HKD (và S3a-HKD)", result.BooksSummary.PrimaryBookCode);
    }

    [Fact]
    public async Task GetTaxSummary_DeclarationMethod_ShouldCalculatePitOnTaxableIncome()
    {
        // Arrange: Phương pháp Kê khai
        _fakeRepo.AnnualAccumulatedRevenue = 2_000_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.GrossRevenue = 500_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;
        _fakeRepo.DeductibleExpenses = 300_000_000m; // Chi phí hợp lý: 300M

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.DeclarationBased
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert
        Assert.Equal(500_000_000m, result.TaxableRevenueInPeriod);
        Assert.Equal(300_000_000m, result.DeductibleExpensesInPeriod);
        Assert.Equal(200_000_000m, result.TaxableIncomeDeclaration); // 500M - 300M = 200M

        // Thuế GTGT 1% trên 500M = 5M
        Assert.Equal(5_000_000m, result.VatAmount);

        // Thuế TNCN Kê khai: 200M * 15% = 30M
        Assert.Equal(30_000_000m, result.DeclarationPitAmount);
        Assert.Equal(35_000_000m, result.TotalTaxAmount);
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
