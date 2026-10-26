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
        Assert.Equal("S2a-HKD", result.BooksSummary.PrimaryBookCode);
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

    [Fact]
    public async Task GetTaxSummary_WhenApplyVatReduction2026_ShouldCalculateVatAt0Point8Percent()
    {
        // Arrange: Năm 2026 áp dụng chính sách giảm 20% tỷ lệ GTGT (NQ 204/2025/QH15 & NĐ 174/2025/NĐ-CP)
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_000_000_000m;
        _fakeRepo.GrossRevenue = 500_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            ApplyVatReduction2026 = true // Kích hoạt chính sách giảm 20%
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Tỷ lệ GTGT giảm từ 1.0% xuống 0.8%
        Assert.True(result.IsVatReduced);
        Assert.Equal(0.8m, result.VatRate);
        Assert.Equal(4_000_000m, result.VatAmount); // 500M * 0.8% = 4.000.000 VNĐ (thay vì 5.000.000 VNĐ)
        Assert.Equal(0.5m, result.PitRate);
        Assert.Equal(2_500_000m, result.PitAmount); // 500M * 0.5% = 2.500.000 VNĐ
        Assert.Equal(6_500_000m, result.TotalTaxAmount);
    }

    [Fact]
    public async Task GetTaxSummary_ThresholdBoundaryExact1Billion_ShouldBeExempt()
    {
        // Arrange: Doanh thu đúng 1.000.000.000 VNĐ
        _fakeRepo.AnnualAccumulatedRevenue = 1_000_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 700_000_000m;
        _fakeRepo.GrossRevenue = 300_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Doanh thu đúng 1 tỷ vẫn thuộc Nhóm 1 và được miễn 100% thuế
        Assert.True(result.IsTaxExempt);
        Assert.Equal(HouseholdTaxGroup.Group1_Under1B, result.HouseholdGroup);
        Assert.Equal(0m, result.VatAmount);
        Assert.Equal(0m, result.PitAmount);
        Assert.Equal(0m, result.TotalTaxAmount);
        Assert.Equal("S1a-HKD", result.BooksSummary.PrimaryBookCode);
    }

    [Fact]
    public async Task GetTaxSummary_ThresholdBoundary1BillionAnd1VND_ShouldCalculateVatAndPitOn1VND()
    {
        // Arrange: Doanh thu 1.000.000.001 VNĐ (vượt đúng 1 đồng)
        _fakeRepo.AnnualAccumulatedRevenue = 1_000_000_001m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_000_000_000m;
        _fakeRepo.GrossRevenue = 1m;
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

        // Assert: Chuyển sang Nhóm 2, GTGT tính trên doanh thu kỳ (1đ * 1% làm tròn = 0đ), TNCN tính trên 1đ vượt (1đ * 0.5% làm tròn = 0đ hoặc integer VND)
        Assert.False(result.IsTaxExempt);
        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, result.HouseholdGroup);
        Assert.Equal(1m, result.VatTaxableRevenue);
        Assert.Equal(1m, result.PitTaxableRevenue);
    }

    [Fact]
    public async Task GetTaxSummary_WhenAnnualRevenueExceeds3Billion_ShouldAutoTransitionToGroup3Declaration()
    {
        // Arrange: Doanh thu năm đạt 3.500.000.000 VNĐ (> 3 tỷ)
        _fakeRepo.AnnualAccumulatedRevenue = 3_500_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 2_800_000_000m;
        _fakeRepo.GrossRevenue = 700_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;
        _fakeRepo.DeductibleExpenses = 500_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage // Dù người dùng chọn Tỷ lệ
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Tự động chuyển sang Nhóm 3 bắt buộc tính theo Thu nhập tính thuế (thuế suất 17%)
        Assert.Equal(HouseholdTaxGroup.Group3_3BTo50B, result.HouseholdGroup);
        Assert.Equal(700_000_000m, result.VatTaxableRevenue);
        Assert.Equal(7_000_000m, result.VatAmount); // 700M * 1% = 7M
        Assert.Equal(200_000_000m, result.TaxableIncomeDeclaration); // 700M - 500M = 200M
        Assert.Equal(17.0m, result.PitRate); // Thuế suất Nhóm 3: 17%
        Assert.Equal(34_000_000m, result.DeclarationPitAmount); // 200M * 17% = 34M
        Assert.Equal("Bộ 4 sổ: S2b, S2c, S2d, S2e-HKD", result.BooksSummary.PrimaryBookCode);
    }
}
