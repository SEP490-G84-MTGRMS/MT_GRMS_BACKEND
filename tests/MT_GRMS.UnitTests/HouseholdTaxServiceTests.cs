using MT_GRMS.Application.Tax.Security;

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

        public decimal PriorYearAccumulatedRevenue { get; set; } = 0m;
        public Dictionary<int, decimal> AnnualRevenuesByYear { get; } = new();

        public Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default)
        {
            if (AnnualRevenuesByYear.TryGetValue(year, out var yearlyRev))
            {
                return Task.FromResult(yearlyRev);
            }

            // Nếu hỏi doanh thu của năm trước
            if (year == 2025 && PriorYearAccumulatedRevenue > 0)
            {
                return Task.FromResult(PriorYearAccumulatedRevenue);
            }
            if (year < 2026)
            {
                return Task.FromResult(0m);
            }

            // Nếu ngày hỏi là trước kỳ hiện tại -> trả về PriorPeriodsAccumulatedRevenue

            if (upToDateUtc < new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc) && PriorPeriodsAccumulatedRevenue > 0)

            {

                return Task.FromResult(PriorPeriodsAccumulatedRevenue);

            }

            return Task.FromResult(AnnualAccumulatedRevenue);

        }

        public Dictionary<Guid, (decimal Gross, decimal Discount, decimal Return)> BranchPeriodRevenues { get; } = new();

        public Task<(decimal GrossRevenue, decimal DiscountAmount, decimal ReturnAmount)> GetPeriodRevenueAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            CancellationToken cancellationToken = default)
        {
            if (branchId.HasValue && BranchPeriodRevenues.TryGetValue(branchId.Value, out var branchRev))
            {
                return Task.FromResult((branchRev.Gross, branchRev.Discount, branchRev.Return));
            }
            return Task.FromResult((GrossRevenue, DiscountAmount, ReturnAmount));
        }

        public decimal CogsAmount { get; set; } = 0m;

        public decimal OpexAmount { get; set; } = 0m;

        public bool IsCogsEstimated { get; set; } = false;

        public Task<decimal> GetPeriodDeductibleExpensesAsync(

            DateTime fromDateUtc,

            DateTime toDateUtc,

            Guid? branchId,

            CancellationToken cancellationToken = default) =>

            Task.FromResult(DeductibleExpenses);

        public Task<List<ExpenseVoucher>> GetDeductibleExpenseVouchersAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ExpenseVoucher>());

        public Task<(decimal TotalCogs, bool IsEstimated)> GetPeriodCogsAsync(

            DateTime fromDateUtc,

            DateTime toDateUtc,

            Guid? branchId,

            CancellationToken cancellationToken = default) =>

            Task.FromResult((CogsAmount, IsCogsEstimated));

        public Task<decimal> GetPeriodOpexAsync(

            DateTime fromDateUtc,

            DateTime toDateUtc,

            Guid? branchId,

            CancellationToken cancellationToken = default) =>

            Task.FromResult(OpexAmount > 0 ? OpexAmount : DeductibleExpenses);

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
            Task.FromResult(SavedSnapshots.FirstOrDefault(s => s.Year == year && s.PeriodType == periodType && s.PeriodNumber == periodNumber && (branchId == null || s.BranchId == branchId)) ?? (ExistingSnapshot != null && ExistingSnapshot.PeriodNumber == periodNumber ? ExistingSnapshot : null));

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

        public decimal ReducedVatRevenue { get; set; } = 0m;

        public decimal StandardVatRevenue { get; set; } = 0m;

        public Dictionary<int, HouseholdTaxYearSetting> YearSettings { get; } = new();

        public Task<(decimal ReducedVatRevenue, decimal StandardVatRevenue)> GetPeriodRevenueSplitAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            Guid? branchId,
            CancellationToken cancellationToken = default)
        {
            if (ReducedVatRevenue > 0 || StandardVatRevenue > 0)
            {
                return Task.FromResult((ReducedVatRevenue, StandardVatRevenue));
            }

            var isYtd = fromDateUtc.Month == 1 || (fromDateUtc.Month == 12 && fromDateUtc.Day == 31);
            var net = isYtd && AnnualAccumulatedRevenue > 0
                ? AnnualAccumulatedRevenue
                : Math.Max(0, GrossRevenue - DiscountAmount - ReturnAmount);

            return Task.FromResult((net, 0m));
        }

        public Task<HouseholdTaxYearSetting?> GetYearSettingAsync(int year, CancellationToken cancellationToken = default)

        {

            if (YearSettings.TryGetValue(year, out var direct))

                return Task.FromResult<HouseholdTaxYearSetting?>(direct);

            var locked = YearSettings.Values

                .Where(s => s.Year <= year && s.LockedUntilYear >= year)

                .OrderByDescending(s => s.Year)

                .FirstOrDefault();

            return Task.FromResult(locked);

        }

        public Task UpsertYearSettingAsync(HouseholdTaxYearSetting setting, CancellationToken cancellationToken = default)

        {

            YearSettings[setting.Year] = setting;

            return Task.CompletedTask;

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

        // GTGT khi vượt 1 tỷ giữa năm: Mặc định là số thận trọng YTD cả năm (1.2 tỷ * 1% = 12.000.000 VNĐ)
        // Phương án phụ theo quý vượt là 300M * 1% = 3.000.000 VNĐ
        Assert.Equal(12_000_000m, result.VatAmount);
        Assert.Equal(3_000_000m, result.QuarterlyVatAmount);
        Assert.Equal(12_000_000m, result.RetrospectiveVatAmount);

        // TNCN CHỈ tính trên phần vượt 1 tỷ: (1.2 tỷ - 1.0 tỷ) - 0 = 200M * 0.5% = 1.000.000 VNĐ
        Assert.Equal(0.5m, result.PitRate);
        Assert.Equal(200_000_000m, result.PitTaxableRevenue);
        Assert.Equal(1_000_000m, result.PitAmount);

        // Tổng thuế = 12M (GTGT thận trọng) + 1M (TNCN) = 13.000.000 VNĐ
        Assert.Equal(13_000_000m, result.TotalTaxAmount);

        Assert.Equal("S2a-HKD", result.BooksSummary.PrimaryBookCode);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_ShouldCalculatePitOnTaxableIncome()

    {

        // Arrange: Phương pháp Kê khai theo mô hình lũy kế YTD (Nhóm 2 doanh thu 1.5B > 1B)

        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;

        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_000_000_000m;

        _fakeRepo.GrossRevenue = 500_000_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        _fakeRepo.DeductibleExpenses = 1_300_000_000m; // Chi phí YTD: 1.3B -> Thu nhập YTD = 1.5B - 1.3B = 200M

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 2,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 0m

        };

        // Act

        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert

        Assert.Equal(500_000_000m, result.TaxableRevenueInPeriod);

        Assert.Equal(1_300_000_000m, result.DeductibleExpensesInPeriod);

        Assert.Equal(200_000_000m, result.TaxableIncomeYtd); // 1.5B - 1.3B = 200M

        Assert.Equal(30_000_000m, result.TaxDueYtd); // 200M * 15% = 30M

        Assert.Equal(30_000_000m, result.DeclarationPitAmount); // Phải nộp kỳ 2: 30M

        Assert.Equal(0m, result.OverpaidTaxCarry);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_FourQuartersYTDCycle_ShouldMatchAnnualTaxExactly()

    {

        // Kịch bản đối chiếu 4 quý theo chuẩn phản biện (Doanh thu năm thuộc Nhóm 2 > 1 tỷ):

        // Q1: DT YTD=1.1B, CP YTD=1.15B (Lỗ -50M) -> YTD Lỗ -50M -> Thuế Q1 = 0

        // Q2: DT YTD=1.3B, CP YTD=1.25B (Lãi +100M) -> YTD Lãi +50M -> Thuế Q2 = 50M * 15% = 7.5M

        // Q3: DT YTD=1.4B, CP YTD=1.37B (Lỗ -20M) -> YTD Lãi +30M (Thuế YTD=4.5M, đã nộp 7.5M) -> Thuế Q3 = 0, nộp thừa 3M

        // Q4: DT YTD=1.6B, CP YTD=1.47B (Lãi +100M) -> YTD Lãi +130M (Thuế YTD=19.5M, đã nộp 7.5M) -> Thuế Q4 = 12M

        // Tổng thuế 4 quý = 0 + 7.5 + 0 + 12 = 19.5M (khớp đúng 130M * 15%)

        // --- Quý 1 ---

        _fakeRepo.AnnualAccumulatedRevenue = 1_100_000_000m;

        _fakeRepo.DeductibleExpenses = 1_150_000_000m;

        var q1 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 0m

        });

        Assert.Equal(0m, q1.TaxableIncomeYtd);

        Assert.Equal(0m, q1.DeclarationPitAmount);

        Assert.Equal(0m, q1.OverpaidTaxCarry);

        // --- Quý 2 ---

        _fakeRepo.AnnualAccumulatedRevenue = 1_300_000_000m;

        _fakeRepo.DeductibleExpenses = 1_250_000_000m;

        var q2 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 2,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 0m // Q1 nộp 0

        });

        Assert.Equal(50_000_000m, q2.TaxableIncomeYtd); // YTD = 1.3B - 1.25B = 50M

        Assert.Equal(7_500_000m, q2.TaxDueYtd); // 50M * 15% = 7.5M

        Assert.Equal(7_500_000m, q2.DeclarationPitAmount); // Q2 phải nộp = 7.5M

        Assert.Equal(0m, q2.OverpaidTaxCarry);

        // --- Quý 3 ---

        _fakeRepo.AnnualAccumulatedRevenue = 1_400_000_000m;

        _fakeRepo.DeductibleExpenses = 1_370_000_000m;

        var q3 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 3,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 7_500_000m // Đã nộp Q1(0) + Q2(7.5M)

        });

        Assert.Equal(30_000_000m, q3.TaxableIncomeYtd); // YTD = 1.4B - 1.37B = 30M

        Assert.Equal(4_500_000m, q3.TaxDueYtd); // 30M * 15% = 4.5M

        Assert.Equal(0m, q3.DeclarationPitAmount); // Q3 phải nộp = 0 (vì đã nộp 7.5M > 4.5M)

        Assert.Equal(3_000_000m, q3.OverpaidTaxCarry); // Nộp thừa 3.0M chuyển sang kỳ sau

        // --- Quý 4 ---

        _fakeRepo.AnnualAccumulatedRevenue = 1_600_000_000m;

        _fakeRepo.DeductibleExpenses = 1_470_000_000m;

        var q4 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 4,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 7_500_000m // Đã nộp Q1(0) + Q2(7.5M) + Q3(0)

        });

        Assert.Equal(130_000_000m, q4.TaxableIncomeYtd); // YTD cả năm = 1.6B - 1.47B = 130M

        Assert.Equal(19_500_000m, q4.TaxDueYtd); // Cả năm = 130M * 15% = 19.5M

        Assert.Equal(12_000_000m, q4.DeclarationPitAmount); // Q4 phải nộp = 19.5M - 7.5M = 12M

        Assert.Equal(0m, q4.OverpaidTaxCarry);

        // Tổng số thuế TNCN tạm nộp 4 quý khớp 100% nghĩa vụ thuế cả năm:

        var totalTaxPaid = q1.DeclarationPitAmount + q2.DeclarationPitAmount + q3.DeclarationPitAmount + q4.DeclarationPitAmount;

        Assert.Equal(19_500_000m, totalTaxPaid);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_ConsecutiveLosses_ShouldPayZeroAndAccumulateLoss()

    {

        // Kịch bản: 2 quý liên tiếp bị lỗ (Doanh thu YTD 1.15B, Chi phí YTD 1.22B -> YTD lỗ -70M)

        _fakeRepo.AnnualAccumulatedRevenue = 1_150_000_000m;

        _fakeRepo.DeductibleExpenses = 1_220_000_000m; // Chi phí YTD > Doanh thu YTD 70M

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 2,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 0m

        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(0m, result.TaxableIncomeYtd);

        Assert.Equal(0m, result.TaxDueYtd);

        Assert.Equal(0m, result.DeclarationPitAmount);

        Assert.Equal(0m, result.OverpaidTaxCarry);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_ProfitableAfterOverpaid_ShouldOffsetOverpaidCorrectly()

    {

        // Kịch bản: Đang nộp thừa 5M ở quý trước, quý này lãi nhẹ khiến thuế YTD tăng lên 4M -> vẫn nộp 0, nộp thừa còn 1M

        _fakeRepo.AnnualAccumulatedRevenue = 1_400_000_000m;

        _fakeRepo.DeductibleExpenses = 1_373_333_333m; // Lãi YTD ~ 26.666.667đ * 15% = 4.000.000đ

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 3,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 5_000_000m // Đã nộp trước 5M

        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(4_000_000m, result.TaxDueYtd);

        Assert.Equal(0m, result.DeclarationPitAmount); // Không phải nộp thêm

        Assert.Equal(1_000_000m, result.OverpaidTaxCarry); // Nộp thừa còn lại: 5M - 4M = 1M

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

    public async Task FinalizeTaxSnapshot_WhenPriorPeriodNotFinalized_ShouldThrowBusinessException()

    {

        // Arrange: Chốt Quý 2 khi Quý 1 chưa được chốt

        _fakeRepo.ExistingSnapshot = null; // Không có snapshot kỳ trước

        var request = new CreateTaxSnapshotRequest

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 2

        };

        // Act & Assert

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>

            _service.FinalizeTaxSnapshotAsync(request, "accountant-user"));

        Assert.Equal("PRIOR_PERIOD_NOT_FINALIZED", ex.ErrorCode);

    }

    [Fact]

    public async Task GetTaxSummary_WhenApplyVatReduction2026_ShouldCalculateVatAt0Point8Percent()
    {
        // Arrange: Năm 2026 áp dụng chính sách giảm 20% tỷ lệ GTGT (NQ 204/2025/QH15 & NĐ 174/2025/NĐ-CP)
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_000_000_001m;

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

        // YTD = 3.5B - 500M = 3.0B

        Assert.Equal(3_000_000_000m, result.TaxableIncomeYtd);

        Assert.Equal(17.0m, result.PitRate); // Thuế suất Nhóm 3: 17%

        Assert.Equal(510_000_000m, result.DeclarationPitAmount); // 3.0B * 17% = 510M

        Assert.Equal("Bộ 5 sổ: S2b, S2c, S2d, S2e, S3a-HKD", result.BooksSummary.PrimaryBookCode);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_WithRealCogsAndOpex_DeductsBothCorrectly()

    {

        // Kịch bản chuẩn Q3: Doanh thu 467.5tr, COGS 327.25tr (70%), OPEX 20tr

        // Thu nhập chịu thuế = 467.5tr - 327.25tr - 20tr = 120.25tr (không còn bị 447.5tr)

        _fakeRepo.AnnualAccumulatedRevenue = 1_417_500_000m;

        _fakeRepo.GrossRevenue = 467_500_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        _fakeRepo.CogsAmount = 327_250_000m;

        _fakeRepo.OpexAmount = 20_000_000m;

        _fakeRepo.IsCogsEstimated = false;

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 3,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 0m

        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(467_500_000m, result.TaxableRevenueInPeriod);

        Assert.Equal(327_250_000m, result.CogsAmount);

        Assert.Equal(20_000_000m, result.OpexAmount);

        Assert.False(result.IsCogsEstimated);

        Assert.Equal(347_250_000m, result.DeductibleExpensesInPeriod); // 327.25M + 20M

    }

    [Fact]

    public async Task GetTaxSummary_WithMixedVatCatalog_CalculatesEffectiveVatRateCorrectly()

    {

        // Giả lập kỳ có cả hàng thường được giảm 0.8% và hàng TTĐB chịu 1.0%

        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;

        _fakeRepo.GrossRevenue = 400_000_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 3,

            CalculationMethod = TaxCalculationMethod.RevenuePercentage,

            ApplyVatReduction2026 = true,

            ReducedVatRevenueInPeriod = 300_000_000m,  // 300M * 0.8% = 2.4M

            StandardVatRevenueInPeriod = 100_000_000m // 100M * 1.0% = 1.0M

        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(400_000_000m, result.TaxableRevenueInPeriod);

        Assert.Equal(3_400_000m, result.VatAmount); // 2.4M + 1.0M = 3.4M

        Assert.Equal(0.85m, result.VatRate); // 3.4M / 400M = 0.85%

        Assert.Equal(300_000_000m, result.VatBreakdown.RevenueReducedRate08);

        Assert.Equal(2_400_000m, result.VatBreakdown.TaxAmountReducedRate08);

        Assert.Equal(100_000_000m, result.VatBreakdown.RevenueStandardRate10);

        Assert.Equal(1_000_000m, result.VatBreakdown.TaxAmountStandardRate10);

    }

    [Fact]

    public async Task FinalizeTaxSnapshot_WhenCogsEstimatedWithoutConfirmation_ThrowsBusinessException()

    {

        _fakeRepo.AnnualAccumulatedRevenue = 4_000_000_000m;

        _fakeRepo.GrossRevenue = 500_000_000m;

        _fakeRepo.CogsAmount = 350_000_000m;

        _fakeRepo.IsCogsEstimated = true; // Sử dụng giá vốn ước tính 70%

        var request = new CreateTaxSnapshotRequest

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            ConfirmEstimatedCogsResponsible = false // Chưa xác nhận trách nhiệm

        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>

            _service.FinalizeTaxSnapshotAsync(request, "admin_user"));

        Assert.Equal("ESTIMATED_COGS_CONFIRMATION_REQUIRED", ex.ErrorCode);

    }

    [Fact]

    public async Task FinalizeTaxSnapshot_WhenCogsEstimatedWithConfirmation_RecordsAuditEvidenceInBookData()

    {

        _fakeRepo.AnnualAccumulatedRevenue = 4_000_000_000m;

        _fakeRepo.GrossRevenue = 500_000_000m;

        _fakeRepo.CogsAmount = 350_000_000m;

        _fakeRepo.IsCogsEstimated = true;

        var request = new CreateTaxSnapshotRequest

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            ConfirmEstimatedCogsResponsible = true,

            EstimatedCogsConfirmationStatement = "Tôi cam kết chịu trách nhiệm pháp lý khi dùng COGS ước tính."

        };

        var result = await _service.FinalizeTaxSnapshotAsync(request, "store_owner_nguyen");

        Assert.NotNull(result);

        Assert.True(result.IsCogsEstimated);

        Assert.NotEmpty(result.AuditSignatureHash);

        var savedSnapshot = await _fakeRepo.GetSnapshotAsync(2026, TaxPeriodType.Quarter, 1, null);

        Assert.NotNull(savedSnapshot);

        Assert.Contains("ownerConfirmation", savedSnapshot.BookDataJson);

        Assert.Contains("store_owner_nguyen", savedSnapshot.BookDataJson);

        Assert.Contains("Tôi cam kết chịu trách nhiệm pháp lý", savedSnapshot.BookDataJson);

    }

    [Fact]

    public void TaxSnapshotSignatureService_WhenBookDataJsonTampered_VerificationFails()

    {

        var sigService = new TaxSnapshotSignatureService();

        var snapshot = new HouseholdTaxSnapshot

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 1,

            TaxableRevenueInPeriod = 500_000_000m,

            TotalTaxAmount = 25_000_000m,

            BookDataJson = "{\"metadata\":{\"policy\":\"NQ 204\"}}",

            FinalizedAtUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),

            FinalizedBy = "admin"

        };

        snapshot.AuditSignatureHash = sigService.ComputeSignature(snapshot);

        Assert.True(sigService.VerifySignature(snapshot));

        // Kẻ gian sửa đổi metadata trong BookDataJson

        snapshot.BookDataJson = "{\"metadata\":{\"policy\":\"FAKE MODIFIED POLICY\"}}";

        // Verification phải báo false ngay lập tức!

        Assert.False(sigService.VerifySignature(snapshot));

    }

    [Fact]

    public async Task GetTaxSummary_WhenThresholdCrossedMidYear_ShouldSetFlagAndNeutralNote()

    {

        // GIVEN: Kỳ trước tích lũy 700 triệu (<= 1 tỷ), kỳ này phát sinh thêm 500 triệu -> Lũy kế 1.2 tỷ (> 1 tỷ)

        _fakeRepo.PriorPeriodsAccumulatedRevenue = 700_000_000m;

        _fakeRepo.AnnualAccumulatedRevenue = 1_200_000_000m;

        _fakeRepo.GrossRevenue = 500_000_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 2,

            CalculationMethod = TaxCalculationMethod.RevenuePercentage

        };

        // WHEN

        var summary = await _service.GetTaxSummaryAsync(filter);

        // THEN

        Assert.True(summary.IsThresholdCrossedMidYear);

        Assert.NotNull(summary.ThresholdExceededNote);

        Assert.Contains("vượt ngưỡng 1 tỷ VNĐ trong kỳ này", summary.ThresholdExceededNote);

        Assert.Contains("tham vấn cơ quan thuế địa bàn", summary.ThresholdExceededNote);

        Assert.False(summary.IsTaxExempt);

        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, summary.HouseholdGroup);

    }

    [Fact]

    public async Task GetTaxSummary_Group2_WhenTier2MethodIsProfit15_ShouldUseDeclarationStrategyWith15PercentPit()

    {

        // GIVEN: Hộ Nhóm 2 (Doanh thu 1.5 tỷ), chọn phương án PROFIT_15 (15% lợi nhuận)

        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;

        _fakeRepo.GrossRevenue = 400_000_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        _fakeRepo.CogsAmount = 200_000_000m;

        _fakeRepo.OpexAmount = 50_000_000m;

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 3,

            Tier2Method = "PROFIT_15"

        };

        // WHEN

        var summary = await _service.GetTaxSummaryAsync(filter);

        // THEN

        Assert.Equal("PROFIT_15", summary.Tier2Method);

        Assert.Equal(TaxCalculationMethod.DeclarationBased, summary.CalculationMethod);

        // Mô hình YTD Kê khai: Thu nhập tính thuế lũy kế = 1.500tr (YTD Rev) - 250tr (YTD Exp: 200tr COGS + 50tr OPEX) = 1.250.000.000đ

        // Thuế TNCN (15% theo PROFIT_15) = 1.250tr * 15% = 187.500.000đ

        Assert.Equal(187_500_000m, summary.PitAmount);

        Assert.Equal(15.0m, summary.PitRate);

    }

    [Fact]
    public async Task GetTaxSummary_WhenCrossed3BMidYear_ShouldKeepRevenuePercentageUntilEndOfYear()
    {
        // Arrange: Hộ đang dùng tỷ lệ %, đến Q2 vượt 3 tỷ (từ 2 tỷ lên 3.5 tỷ)
        _fakeRepo.AnnualAccumulatedRevenue = 3_500_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 2_000_000_000m; // Đầu kỳ chưa đến 3 tỷ
        _fakeRepo.GrossRevenue = 1_500_000_000m;
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

        // Assert: Tiếp tục giữ phương pháp tỷ lệ % đến hết năm, chưa áp kê khai ngay
        Assert.Equal(TaxCalculationMethod.RevenuePercentage, result.CalculationMethod);
        Assert.True(result.Is3BCrossedMidYear);
        Assert.True(result.IsExceeding3BThreshold);
        Assert.Equal(2027, result.DeclarationMethodStartYear); // Kê khai từ năm sau
        Assert.Contains("tiếp tục thực hiện phương pháp tính thuế hiện tại", result.ThresholdExceededNote);
    }

    [Fact]
    public async Task GetTaxSummary_WhenLockedFromPriorYear_ShouldEnforceDeclarationMethod()
    {
        // Arrange: Năm trước (2026) đã khóa bắt buộc kê khai cho 2027-2028
        await _fakeRepo.UpsertYearSettingAsync(new HouseholdTaxYearSetting
        {
            Year = 2027,
            PitMethod = TaxCalculationMethod.DeclarationBased,
            LockedUntilYear = 2028,
            LockReason = "REVENUE_EXCEEDED_3B"
        });

        _fakeRepo.AnnualAccumulatedRevenue = 400_000_000m; // Doanh thu mới đầu năm
        _fakeRepo.GrossRevenue = 400_000_000m;
        _fakeRepo.CogsAmount = 250_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2027,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage // Người dùng cố chọn % nhưng bị khóa
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Bắt buộc dùng phương pháp kê khai theo khóa
        Assert.Equal(TaxCalculationMethod.DeclarationBased, result.CalculationMethod);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenCrossed3BMidYear_ShouldLockDeclarationMethodForNextTwoYears()
    {
        // Arrange: Chốt kỳ 1 trước để thỏa mãn tính tuần tự
        _fakeRepo.SavedSnapshots.Add(new HouseholdTaxSnapshot { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 });

        _fakeRepo.AnnualAccumulatedRevenue = 3_200_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 2_100_000_000m; // Vừa vượt 3 tỷ giữa năm (tại Q2)
        _fakeRepo.GrossRevenue = 1_100_000_000m;

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };

        // Act
        var snapshotDto = await _service.FinalizeTaxSnapshotAsync(request, "owner-01");

        // Assert: Phương pháp snapshot kỳ hiện tại vẫn là tỷ lệ %
        Assert.Equal(TaxCalculationMethod.RevenuePercentage, snapshotDto.CalculationMethod);

        // Kiểm tra setting năm sau (2027) đã được khóa tự động ổn định 2 năm (2027-2028)
        var nextYearSetting = await _fakeRepo.GetYearSettingAsync(2027);
        Assert.NotNull(nextYearSetting);
        Assert.Equal(TaxCalculationMethod.DeclarationBased, nextYearSetting.PitMethod);
        Assert.Equal(2028, nextYearSetting.LockedUntilYear);
        Assert.Equal("REVENUE_EXCEEDED_3B", nextYearSetting.LockReason);
    }
    [Fact]
    public async Task GetTaxSummary_ThresholdBoundaryExact3Billion_ShouldNotBeExceeding3B()
    {
        // Arrange: Doanh thu đúng tròn 3 tỷ VNĐ (biên)
        _fakeRepo.AnnualAccumulatedRevenue = 3_000_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 2_000_000_000m;
        _fakeRepo.GrossRevenue = 1_000_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Không vượt ngưỡng 3 tỷ (vì luật quy định TRÊN 3 tỷ mới chuyển)
        Assert.False(result.IsExceeding3BThreshold);
        Assert.False(result.Is3BCrossedMidYear);
        Assert.Equal(TaxCalculationMethod.RevenuePercentage, result.CalculationMethod);
    }

    [Fact]
    public async Task GetTaxSummary_Year2028_InheritsLockFrom2027Setting()
    {
        // Arrange: Năm 2027 đã khóa kê khai ổn định 2 năm (2027-2028)
        _fakeRepo.YearSettings[2027] = new HouseholdTaxYearSetting
        {
            Year = 2027,
            PitMethod = TaxCalculationMethod.DeclarationBased,
            LockedUntilYear = 2028,
            LockReason = "REVENUE_EXCEEDED_3B"
        };

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2028,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage // User yêu cầu tỷ lệ %
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Năm 2028 vẫn bị khóa kê khai do thừa kế khóa 2 năm 2027-2028
        Assert.Equal(TaxCalculationMethod.DeclarationBased, result.CalculationMethod);
    }

    [Fact]
    public async Task GetTaxSummary_WhenPriorYearExceeded3B_WithoutPriorFinalize_ShouldFallbackToDeclaration()
    {
        // Arrange: Chưa có bản ghi khóa nào trong YearSettings, nhưng năm 2025 doanh thu đã là 3.5 tỷ
        _fakeRepo.PriorYearAccumulatedRevenue = 3_500_000_000m;
        _fakeRepo.AnnualAccumulatedRevenue = 500_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Tự động suy ra năm nay phải Kê khai theo NĐ 68
        Assert.Equal(TaxCalculationMethod.DeclarationBased, result.CalculationMethod);
        Assert.Equal(2026, result.DeclarationMethodStartYear);
    }

    [Fact]
    public async Task GetTaxSummary_Q4_2026_EvaluatedAtAnyTime_ShouldKeepVatReductionAt0Point8Percent()
    {
        // Arrange: Kỳ tính thuế là Q4/2026 (toDateUtc kết thúc 31/12/2026)
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_000_000_000m;
        _fakeRepo.GrossRevenue = 500_000_000m;
        _fakeRepo.ReducedVatRevenue = 200_000_000m;
        _fakeRepo.StandardVatRevenue = 300_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            ApplyVatReduction2026 = true
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Vẫn được tính thuế 0.8% cho 200tr hàng giảm thuế
        Assert.True(result.IsVatReduced);
        Assert.Equal(1_600_000m, result.VatBreakdown.TaxAmountReducedRate08); // 200tr * 0.8%
        Assert.Equal(3_000_000m, result.VatBreakdown.TaxAmountStandardRate10); // 300tr * 1.0%
    }

    [Fact]
    public async Task GetTaxSummary_WhenGrossRevenueIsZero_ShouldHandleSafelyWithoutDivideByZero()
    {
        // Arrange: Gross revenue = 0
        _fakeRepo.AnnualAccumulatedRevenue = 0m;
        _fakeRepo.GrossRevenue = 0m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;
        _fakeRepo.ReducedVatRevenue = 0m;
        _fakeRepo.StandardVatRevenue = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: An toàn tuyệt đối, thuế = 0
        Assert.Equal(0m, result.TaxableRevenueInPeriod);
        Assert.Equal(0m, result.TotalTaxAmount);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenGroup2VoluntaryDeclaration_ShouldLockTwoYears()
    {
        // Arrange: Hộ Nhóm 2 (1-3 tỷ) tự nguyện chọn Kê khai
        _fakeRepo.AnnualAccumulatedRevenue = 1_800_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 0m;
        _fakeRepo.GrossRevenue = 1_800_000_000m;

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.DeclarationBased
        };

        // Act
        var snapshotDto = await _service.FinalizeTaxSnapshotAsync(request, "owner-01");

        // Assert: Năm 2026 được khóa ổn định 2 năm (2026-2027) theo NĐ 68
        var currentSetting = await _fakeRepo.GetYearSettingAsync(2026);
        Assert.NotNull(currentSetting);
        Assert.Equal(TaxCalculationMethod.DeclarationBased, currentSetting.PitMethod);
        Assert.Equal(2027, currentSetting.LockedUntilYear);
        Assert.Equal("VOLUNTARY_DECLARATION_GROUP2", currentSetting.LockReason);
    }
    [Fact]
    public async Task GetTaxSummary_FallbackTwoYears_WhenYearMinus2Exceeded3B_YearMinus1Dropped_ShouldStillBeDeclarationAndStartAtYearMinus1()
    {
        // Arrange: Năm 2026 vượt 3.5 tỷ nhưng chủ hộ chưa từng bấm chốt kỳ.
        // Năm 2027 doanh thu rớt xuống 2.2 tỷ (chưa chốt).
        // Sang năm 2028 (năm thứ 2 của chu kỳ 2 năm): vẫn phải bắt buộc Kê khai, startYear = 2027!
        _fakeRepo.AnnualRevenuesByYear[2026] = 3_500_000_000m;
        _fakeRepo.AnnualRevenuesByYear[2027] = 2_200_000_000m;
        _fakeRepo.AnnualAccumulatedRevenue = 500_000_000m; // Đầu năm 2028 mới 500tr
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 0m;
        _fakeRepo.GrossRevenue = 500_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2028,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Thừa kế bắt buộc Kê khai từ chu kỳ 2027-2028, startYear là 2027
        Assert.Equal(TaxCalculationMethod.DeclarationBased, result.CalculationMethod);
        Assert.Equal(2027, result.DeclarationMethodStartYear);
    }

    [Fact]
    public void GetVietnamTimeZone_ShouldReturnValidOffsetPlus7_WithoutException()
    {
        // Act: Kiểm tra hàm lấy TimeZone Việt Nam chạy an toàn không throw
        var tz = HouseholdTaxService.GetVietnamTimeZone();

        // Assert: Múi giờ UTC+7
        Assert.NotNull(tz);
        Assert.Equal(TimeSpan.FromHours(7), tz.BaseUtcOffset);
    }

    [Fact]
    public async Task GetTaxSummary_WhenReturnsExceedGross_ShouldHandleSafely()
    {
        // Arrange: Kỳ chỉ có hàng trả lại (Gross = 10tr, Returns = 30tr -> Net âm 20tr)
        _fakeRepo.AnnualAccumulatedRevenue = 100_000_000m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 120_000_000m;
        _fakeRepo.GrossRevenue = 10_000_000m;
        _fakeRepo.ReturnAmount = 30_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Chặn an toàn ở bước tính thuế (TaxableRevenueInPeriod = 0, Thuế = 0)
        Assert.Equal(0m, result.TaxableRevenueInPeriod);
        Assert.Equal(0m, result.TotalTaxAmount);
    }

    [Fact]
    public async Task GetTaxSummary_MidYearThresholdExceeded_ShouldIncludeRetrospectiveVatWarning()
    {
        // Arrange: Lũy kế vượt 1 tỷ giữa năm (từ 800tr lên 1.2 tỷ)
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 800_000_000m;
        _fakeRepo.AnnualAccumulatedRevenue = 1_200_000_000m;
        _fakeRepo.GrossRevenue = 400_000_000m;
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

        // Assert: Cảnh báo vượt ngưỡng nêu rõ Điều 10 NĐ 68 và ước tính GTGT hồi tố cả năm (12,000,000đ)
        Assert.NotNull(result.ThresholdExceededNote);
        Assert.Contains("1 tỷ VNĐ", result.ThresholdExceededNote);
        Assert.Contains("Điều 10 NĐ 68", result.ThresholdExceededNote);
        Assert.Contains("12,000,000đ", result.ThresholdExceededNote);
    }

    [Fact]
    public void GetDateRangesForPeriod_NewYearMidnightOrder_FallsInto2027Not2026()
    {
        // Arrange: Đơn hàng phát sinh lúc 00:30 ngày 01/01/2027 giờ Việt Nam
        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var orderVn = new DateTime(2027, 1, 1, 0, 30, 0, DateTimeKind.Unspecified);
        var orderUtc = TimeZoneInfo.ConvertTimeToUtc(orderVn, vnTz); // 2026-12-31 17:30:00 UTC

        // Act: Lấy khoảng nửa mở [start, nextStart) cho Q4/2026 và Q1/2027
        var (q4FromUtc, q4ToUtc, _) = HouseholdTaxService.GetDateRangesForPeriod(2026, TaxPeriodType.Quarter, 4);
        var (q1FromUtc, q1ToUtc, _) = HouseholdTaxService.GetDateRangesForPeriod(2027, TaxPeriodType.Quarter, 1);

        // Assert:
        // Q4/2026 kết thúc tại 17:00:00 UTC -> orderUtc (17:30) KHÔNG thuộc Q4/2026
        Assert.False(orderUtc >= q4FromUtc && orderUtc < q4ToUtc, "Order at 00:30 on 01/01/2027 VN must NOT fall into Q4/2026");

        // Q1/2027 bắt đầu từ 17:00:00 UTC -> orderUtc (17:30) THUỘC Q1/2027
        Assert.True(orderUtc >= q1FromUtc && orderUtc < q1ToUtc, "Order at 00:30 on 01/01/2027 VN must fall into Q1/2027");
    }

    [Fact]
    public void GetDateRangesForPeriod_VatReductionDeadline_BoundaryExactMatch()
    {
        // Arrange & Act
        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var deadlineUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), vnTz);
        var (q4FromUtc, q4ToUtc, _) = HouseholdTaxService.GetDateRangesForPeriod(2026, TaxPeriodType.Quarter, 4);
        var (q1FromUtc, q1ToUtc, _) = HouseholdTaxService.GetDateRangesForPeriod(2027, TaxPeriodType.Quarter, 1);

        // Assert: Q4/2026 toDateUtc đúng bằng mốc deadline (17:00:00 UTC ngày 31/12/2026)
        Assert.Equal(deadlineUtc, q4ToUtc);
        Assert.True(q4ToUtc <= deadlineUtc, "Q4/2026 must be within reduction deadline");
        Assert.False(q1ToUtc <= deadlineUtc, "Q1/2027 must NOT be within reduction deadline");
    }

    [Fact]
    public async Task GetTaxSummary_WhenGrossOver1BNetUnder1B_ShouldTriggerWarningAndRemainExempt()
    {
        // Arrange: Hộ có Gross = 1.05 tỷ (vượt 1 tỷ) nhưng Net = 980 triệu (dưới 1 tỷ)
        _fakeRepo.AnnualAccumulatedRevenue = 980_000_000m;
        _fakeRepo.GrossRevenue = 1_050_000_000m;
        _fakeRepo.DiscountAmount = 70_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Vẫn được miễn thuế nhưng có cảnh báo thận trọng đối chiếu
        Assert.True(result.IsTaxExempt);
        Assert.Equal(0m, result.TotalTaxAmount);
        Assert.True(result.IsGrossOver1BNetUnder1B);
        Assert.NotNull(result.NetVsGrossThresholdWarning);
        Assert.Contains("vượt ngưỡng 1 tỷ VNĐ", result.NetVsGrossThresholdWarning);
        Assert.NotNull(result.GrossBasedEstimatedTaxAmount);
        Assert.True(result.GrossBasedEstimatedTaxAmount > 0);
    }

    [Fact]
    public async Task GetTaxSummary_WhenLockedDeclaration_ButRevenueUnder1B_ShouldStillBeExempt()
    {
        // Arrange: Hộ bị khóa Kê khai (2027-2028), sang 2028 doanh thu rớt xuống 800 triệu (<= 1 tỷ)
        _fakeRepo.YearSettings[2027] = new HouseholdTaxYearSetting
        {
            Year = 2027,
            LockedUntilYear = 2028,
            PitMethod = TaxCalculationMethod.DeclarationBased,
            LockReason = "REVENUE_EXCEEDED_3B"
        };

        _fakeRepo.AnnualAccumulatedRevenue = 800_000_000m; // Dưới 1 tỷ
        _fakeRepo.GrossRevenue = 800_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2028,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Theo Điều 10 NĐ 141, doanh thu <= 1 tỷ được miễn thuế hoàn toàn dù có khóa Kê khai
        Assert.True(result.IsTaxExempt);
        Assert.Equal(0m, result.TotalTaxAmount);
        Assert.Equal(HouseholdTaxGroup.Group1_Under1B, result.HouseholdGroup);
    }

    [Fact]
    public async Task GetTaxSummary_MidYearExceeded_WhenOptionQuarterOnly_ShouldReturnQuarterlyVat()
    {
        // Arrange: Vượt 1 tỷ giữa năm (từ 800tr lên 1.2 tỷ), Q2 phát sinh 400tr
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 800_000_000m;
        _fakeRepo.AnnualAccumulatedRevenue = 1_200_000_000m;
        _fakeRepo.GrossRevenue = 400_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            VatOption = "QUARTER_ONLY" // Tùy chọn chỉ tính phát sinh trong quý vượt
        };

        // Act
        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Trả về số theo quý (400M * 1% = 4.000.000đ), nhưng vẫn lưu số thận trọng (12.000.000đ)
        Assert.Equal(4_000_000m, result.VatAmount);
        Assert.Equal(4_000_000m, result.QuarterlyVatAmount);
        Assert.Equal(12_000_000m, result.RetrospectiveVatAmount);
        Assert.Equal("QUARTER_ONLY", result.VatCalculationOption);
    }
    [Fact]
    public async Task GetTaxSummary_Sequence35_35_20_Year2029_ShouldBeFree()
    {
        // Bug 1 Verification:
        // Năm 2026: 3.5 tỷ (kích hoạt chu kỳ 2027-2028)
        // Năm 2027: 3.5 tỷ (năm 1 của chu kỳ)
        // Năm 2028: 2.0 tỷ (năm cuối của chu kỳ, doanh thu tụt <= 3 tỷ)
        // Năm 2029: Chu kỳ đã hết hạn, hộ phải được TỰ DO chọn phương pháp (không bị ép Kê khai)
        _fakeRepo.AnnualRevenuesByYear[2026] = 3_500_000_000m;
        _fakeRepo.AnnualRevenuesByYear[2027] = 3_500_000_000m;
        _fakeRepo.AnnualRevenuesByYear[2028] = 2_000_000_000m;
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m; // Doanh thu năm 2029
        _fakeRepo.GrossRevenue = 500_000_000m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2029,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            Tier2Method = "REVENUE_EXCESS"
        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(TaxCalculationMethod.RevenuePercentage, result.CalculationMethod);
        Assert.Null(result.DeclarationMethodStartYear);
    }

    [Fact]
    public async Task GetTaxSummary_CustomPeriodSpanningVatDeadline_ShouldApplyReductionToEligiblePortion()
    {
        // Bug 3 Verification:
        // Kỳ tính thuế vắt qua mốc 31/12/2026 (15/12/2026 - 15/01/2027)
        // Doanh thu hàng bán trước hạn có AppliedVatRate = 0.008 vẫn phải được giảm 0.8%
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.ReducedVatRevenue = 200_000_000m; // 200 triệu bán trước hạn
        _fakeRepo.StandardVatRevenue = 300_000_000m; // 300 triệu bán sau hạn hoặc không giảm
        _fakeRepo.GrossRevenue = 500_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            ApplyVatReduction2026 = true
        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.True(result.IsVatReduced);
        Assert.Equal(200_000_000m, result.VatBreakdown.RevenueReducedRate08);
        Assert.Equal(1_600_000m, result.VatBreakdown.TaxAmountReducedRate08); // 200M * 0.8%
        Assert.Equal(300_000_000m, result.VatBreakdown.RevenueStandardRate10);
        Assert.Equal(3_000_000m, result.VatBreakdown.TaxAmountStandardRate10); // 300M * 1.0%
    }

    [Fact]
    public async Task GetTaxSummary_Q3AfterQ2QuarterOnly_WithPriorDeclaredVatTax_ShouldNotDoubleCountOrOmit()
    {
        // Bug 4 Verification:
        // Q2 vượt 1 tỷ: Chủ hộ chốt phương án QUARTER_ONLY nộp 5 triệu (trên 500 triệu của Q2).
        // Sang Q3: Doanh thu lũy kế YTD đạt 1.5 tỷ (chuẩn 1.0% = 15 triệu cả năm).
        // Khi Q3 chọn PRUDENT_YTD với PriorDeclaredVatTaxOverride = 5 triệu:
        // Thuế GTGT Q3 phải là 15M - 5M = 10 triệu (thu đủ 6M của Q1 và 4M của Q3, không tính trùng, không bỏ sót).
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.GrossRevenue = 400_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 900_000_000m; // Vừa vượt trong kỳ này

        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            ApplyVatReduction2026 = false,
            PriorDeclaredVatTaxOverride = 5_000_000m,
            VatOption = "PRUDENT_YTD"
        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(5_000_000m, result.PriorDeclaredVatTax);
        // YTD VAT = 15M, trừ 5M đã nộp kỳ trước = 10M
        Assert.Equal(10_000_000m, result.VatAmount);
    }

    [Fact]
    public async Task GetTaxSummary_MultiBranchAggregation_TenantOver1B_BothBranchesShouldBeGroup2()
    {
        // GIAI ĐOẠN 6: Kiểm thử tổng hợp đa chi nhánh (Multi-Branch Aggregation)
        // Chi nhánh A có doanh thu 600 triệu (nếu đứng độc lập < 1 tỷ)
        // Chi nhánh B có doanh thu 500 triệu (nếu đứng độc lập < 1 tỷ)
        // Tổng doanh thu lũy kế Tenant = 600M + 500M = 1.1 tỷ (> 1 tỷ ngưỡng miễn thuế)
        var branchAId = Guid.NewGuid();
        var branchBId = Guid.NewGuid();

        _fakeRepo.AnnualAccumulatedRevenue = 1_100_000_000m; // Doanh thu lũy kế toàn Tenant
        _fakeRepo.GrossRevenue = 1_100_000_000m;
        _fakeRepo.DiscountAmount = 0m;
        _fakeRepo.ReturnAmount = 0m;
        _fakeRepo.PriorPeriodsAccumulatedRevenue = 0m;

        _fakeRepo.BranchPeriodRevenues[branchAId] = (600_000_000m, 0m, 0m);
        _fakeRepo.BranchPeriodRevenues[branchBId] = (500_000_000m, 0m, 0m);

        // 1. Chi nhánh A truy vấn tóm tắt thuế
        var filterA = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            BranchId = branchAId
        };
        var resultA = await _service.GetTaxSummaryAsync(filterA);

        // 2. Chi nhánh B truy vấn tóm tắt thuế
        var filterB = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 4,
            BranchId = branchBId
        };
        var resultB = await _service.GetTaxSummaryAsync(filterB);

        // THEN: Cả 2 chi nhánh đều bị chuyển sang Nhóm 2 (Group2_1BTo3B) và KHÔNG được miễn thuế
        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, resultA.HouseholdGroup);
        Assert.False(resultA.IsTaxExempt);
        Assert.Equal(600_000_000m, resultA.GrossRevenueInPeriod);
        Assert.Equal(1_100_000_000m, resultA.AnnualAccumulatedRevenue);

        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, resultB.HouseholdGroup);
        Assert.False(resultB.IsTaxExempt);
        Assert.Equal(500_000_000m, resultB.GrossRevenueInPeriod);
        Assert.Equal(1_100_000_000m, resultB.AnnualAccumulatedRevenue);

        // Tổng doanh thu tính thuế của cả 2 chi nhánh khớp chính xác 1.1 tỷ của Tenant
        Assert.Equal(1_100_000_000m, resultA.TaxableRevenueInPeriod + resultB.TaxableRevenueInPeriod);
    }

}
