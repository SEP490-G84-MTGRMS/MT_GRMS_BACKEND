using System.Data;
using MT_GRMS.Application.Common;
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
    private sealed class OwnerTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-tax-test";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private sealed class AcceptingPinService : IPinVerificationService
    {
        public Task<PinVerificationResult> VerifyStoreOwnerPinAsync(string userId, string inputPin, CancellationToken ct = default) =>
            Task.FromResult(new PinVerificationResult(true));
        public Task SetStoreOwnerPinAsync(string userId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
        public Task ChangeSecurityPinAsync(string userId, ChangeSecurityPinRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetSecurityPinAsync(string userId, ResetSecurityPinRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

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

        public HouseholdTaxpayerProfileDto TaxpayerProfile { get; set; } = new();

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
            Task.FromResult(SavedSnapshots.LastOrDefault(s => s.Year == year && s.PeriodType == periodType && s.PeriodNumber == periodNumber && (branchId == null || s.BranchId == branchId || s.BranchId == null) && s.Status == TaxSnapshotStatus.Finalized) ?? (ExistingSnapshot != null && ExistingSnapshot.PeriodNumber == periodNumber && ExistingSnapshot.Status == TaxSnapshotStatus.Finalized ? ExistingSnapshot : null));

        public Task<HouseholdTaxSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedSnapshots.FirstOrDefault(s => s.Id == id) ?? ExistingSnapshot);

        public Task<HouseholdTaxSnapshot?> GetSnapshotForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            GetSnapshotByIdAsync(id, cancellationToken);

        public Task<List<HouseholdTaxSnapshot>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedSnapshots);

        public Task<HouseholdTaxpayerProfileDto> GetTaxpayerProfileAsync(Guid? branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TaxpayerProfile);

        public Task<HouseholdTaxSnapshot> AddSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            snapshot.Id = Guid.NewGuid();
            SavedSnapshots.Add(snapshot);
            ExistingSnapshot = snapshot;
            return Task.FromResult(snapshot);
        }

        public Task UpdateSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            var idx = SavedSnapshots.FindIndex(s => s.Id == snapshot.Id);
            if (idx >= 0)
            {
                SavedSnapshots[idx] = snapshot;
            }
            if (ExistingSnapshot?.Id == snapshot.Id)
            {
                ExistingSnapshot = snapshot;
            }
            return Task.CompletedTask;
        }

        public decimal ReducedVatRevenue { get; set; } = 0m;

        public decimal StandardVatRevenue { get; set; } = 0m;

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

    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public IsolationLevel? RequestedIsolationLevel { get; private set; }
        public int TransactionExecutionCount { get; private set; }
        public List<string>? CallOrder { get; init; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            TransactionExecutionCount++;
            CallOrder?.Add("ExecuteInTransaction");
            RequestedIsolationLevel = isolationLevel;
            return operation(cancellationToken);
        }
    }

    private sealed class RecordingPinService : IPinVerificationService
    {
        private readonly PinVerificationResult _result;
        public int VerifyCallCount { get; private set; }
        public List<string>? CallOrder { get; init; }

        public RecordingPinService(PinVerificationResult result)
        {
            _result = result;
        }

        public Task<PinVerificationResult> VerifyStoreOwnerPinAsync(string userId, string inputPin, CancellationToken ct = default)
        {
            VerifyCallCount++;
            CallOrder?.Add("VerifyPin");
            return Task.FromResult(_result);
        }

        public Task SetStoreOwnerPinAsync(string userId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
        public Task ChangeSecurityPinAsync(string userId, ChangeSecurityPinRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetSecurityPinAsync(string userId, ResetSecurityPinRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly FakeHouseholdTaxRepository _fakeRepo = new();

    private readonly HouseholdTaxService _service;

    public HouseholdTaxServiceTests()

    {

        _service = CreateService(_fakeRepo);

    }

    private static HouseholdTaxService CreateService(
        IHouseholdTaxRepository repository,
        IUnitOfWork? unitOfWork = null,
        ITenantContext? tenantContext = null,
        IPinVerificationService? pinService = null) =>
        new(
            repository,
            new MT_GRMS.Application.Tax.Strategies.TaxCalculationStrategyFactory(new MT_GRMS.Application.Tax.Strategies.ITaxCalculationStrategy[]
            {
                new MT_GRMS.Application.Tax.Strategies.RevenuePercentageTaxStrategy(),
                new MT_GRMS.Application.Tax.Strategies.DeclarationBasedTaxStrategy()
            }),
            pinService ?? new AcceptingPinService(),
            null,
            null,
            tenantContext ?? new OwnerTenantContext(),
            unitOfWork);

    private static Task<HouseholdTaxSnapshotDto> FinalizeAsync(
        HouseholdTaxService service,
        CreateTaxSnapshotRequest request,
        string actor)
    {
        request.SecurityPin = "123456";
        return service.FinalizeTaxSnapshotAsync(request, actor, "owner-user-id");
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

        // TNCN CHỈ tính trên phần vượt 1 tỷ: (1.2 tỷ - 1.0 tỷ) = 200M * 0.5% = 1.000.000 VNĐ
        // Năm 2026 doanh thu <= 10 tỷ: giảm 30% TNCN theo NQ 43/2026 & NĐ 398/2026 (-300.000đ) -> còn 700.000 VNĐ
        Assert.Equal(0.5m, result.PitRate);
        Assert.Equal(200_000_000m, result.PitTaxableRevenue);
        Assert.Equal(1_000_000m, result.PitBeforeReduction);
        Assert.Equal(300_000m, result.PitReductionAmount);
        Assert.True(result.IsPitReduced);
        Assert.Equal(700_000m, result.PitAmount);

        // Tổng thuế = 12M (GTGT thận trọng) + 700k (TNCN sau giảm 30%) = 12.700.000 VNĐ
        Assert.Equal(12_700_000m, result.TotalTaxAmount);

        Assert.Equal("S2a-HKD", result.BooksSummary.PrimaryBookCode);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_ShouldCalculatePitOnTaxableIncome()

    {

        // Arrange: Phương pháp Kê khai quyết toán năm (Year) theo NĐ 68/2026 & giảm 30% TNCN theo NĐ 398/2026 (Nhóm 2 doanh thu 1.5B > 1B)

        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;

        _fakeRepo.PriorPeriodsAccumulatedRevenue = 1_000_000_000m;

        _fakeRepo.GrossRevenue = 500_000_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        _fakeRepo.DeductibleExpenses = 1_300_000_000m; // Chi phí YTD: 1.3B -> Thu nhập YTD = 1.5B - 1.3B = 200M

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Year,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 0m

        };

        // Act

        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: 200M * 15% = 30M; giảm 30% NĐ 398 (-9M) => 21M; hạn quyết toán 31/03/2027

        Assert.Equal(500_000_000m, result.TaxableRevenueInPeriod);

        Assert.Equal(1_300_000_000m, result.DeductibleExpensesInPeriod);

        Assert.Equal(200_000_000m, result.TaxableIncomeYtd);

        Assert.Equal(30_000_000m, result.TaxDueYtdBeforeReduction);

        Assert.Equal(9_000_000m, result.PitReductionYtd);

        Assert.Equal(21_000_000m, result.TaxDueYtd);

        Assert.Equal(21_000_000m, result.DeclarationPitAmount);

        Assert.Equal(0m, result.OverpaidTaxCarry);

        Assert.Equal(new DateTime(2027, 3, 31), result.FilingDeadlineLocal.Date);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_FourQuartersYTDCycle_ShouldMatchAnnualTaxExactly()

    {

        // Kịch bản đối chiếu 4 quý tạm nộp (Khoản 2 Điều 10 NĐ 68) + Quyết toán năm (15% lợi nhuận, giảm 30% NĐ 398):
        // Q1: DT YTD=1.1B -> vượt 100M -> tạm nộp = 100M * 0.5% * 70% = 350k
        // Q2: DT YTD=1.3B -> vượt 300M -> YTD tạm nộp = 1.05M -> Q2 nộp thêm 700k
        // Q3: DT YTD=1.4B -> vượt 400M -> YTD tạm nộp = 1.4M -> Q3 nộp thêm 350k
        // Q4: DT YTD=1.6B -> vượt 600M -> YTD tạm nộp = 2.1M -> Q4 nộp thêm 700k (Tổng 4 quý = 2.1M)
        // Quyết toán năm: DT 1.6B - CP 1.47B = Lãi 130M -> 130M * 15% * 70% = 13.65M -> Nộp bổ sung 13.65M - 2.1M = 11.55M

        _fakeRepo.AnnualAccumulatedRevenue = 1_100_000_000m;
        _fakeRepo.DeductibleExpenses = 1_150_000_000m;
        var q1 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.DeclarationBased, PriorDeclaredPitTaxOverride = 0m
        });
        Assert.True(q1.IsProvisionalPitPeriod);
        Assert.Equal(0m, q1.TaxableIncomeYtd);
        Assert.Equal(350_000m, q1.PitAmount);

        _fakeRepo.AnnualAccumulatedRevenue = 1_300_000_000m;
        _fakeRepo.DeductibleExpenses = 1_250_000_000m;
        var q2 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 2,
            CalculationMethod = TaxCalculationMethod.DeclarationBased, PriorDeclaredPitTaxOverride = q1.PitAmount
        });
        Assert.Equal(50_000_000m, q2.TaxableIncomeYtd);
        Assert.Equal(700_000m, q2.PitAmount);

        _fakeRepo.AnnualAccumulatedRevenue = 1_400_000_000m;
        _fakeRepo.DeductibleExpenses = 1_370_000_000m;
        var q3 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 3,
            CalculationMethod = TaxCalculationMethod.DeclarationBased, PriorDeclaredPitTaxOverride = q1.PitAmount + q2.PitAmount
        });
        Assert.Equal(30_000_000m, q3.TaxableIncomeYtd);
        Assert.Equal(350_000m, q3.PitAmount);

        _fakeRepo.AnnualAccumulatedRevenue = 1_600_000_000m;
        _fakeRepo.DeductibleExpenses = 1_470_000_000m;
        var q4 = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 4,
            CalculationMethod = TaxCalculationMethod.DeclarationBased, PriorDeclaredPitTaxOverride = q1.PitAmount + q2.PitAmount + q3.PitAmount
        });
        Assert.Equal(130_000_000m, q4.TaxableIncomeYtd);
        Assert.Equal(700_000m, q4.PitAmount);

        var totalProvisional = q1.PitAmount + q2.PitAmount + q3.PitAmount + q4.PitAmount;
        Assert.Equal(2_100_000m, totalProvisional);

        // Quyết toán năm 2026: 130M * 15% * 70% = 13.65M -> Nộp thêm = 11.55M
        var annualSettlement = await _service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026, PeriodType = TaxPeriodType.Year, PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.DeclarationBased, PriorDeclaredPitTaxOverride = totalProvisional
        });
        Assert.False(annualSettlement.IsProvisionalPitPeriod);
        Assert.Equal(13_650_000m, annualSettlement.TaxDueYtd);
        Assert.Equal(11_550_000m, annualSettlement.PitAmount);
        Assert.Equal(13_650_000m, totalProvisional + annualSettlement.PitAmount);

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_ConsecutiveLosses_ShouldPayZeroAndAccumulateLoss()

    {

        // Kịch bản quyết toán năm bị lỗ (Doanh thu YTD 1.15B, Chi phí YTD 1.22B -> YTD lỗ -70M)

        _fakeRepo.AnnualAccumulatedRevenue = 1_150_000_000m;

        _fakeRepo.DeductibleExpenses = 1_220_000_000m; // Chi phí YTD > Doanh thu YTD 70M

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Year,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 525_000m // Đã tạm nộp quý trước

        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(0m, result.TaxableIncomeYtd);

        Assert.Equal(0m, result.TaxDueYtd);

        Assert.Equal(0m, result.DeclarationPitAmount);

        Assert.Equal(525_000m, result.OverpaidTaxCarry); // Hoàn / bù trừ theo Điều 12 NĐ 68

    }

    [Fact]

    public async Task GetTaxSummary_DeclarationMethod_ProfitableAfterOverpaid_ShouldOffsetOverpaidCorrectly()

    {

        // Kịch bản quyết toán năm: Đã tạm nộp 5M, lãi YTD = 26.666.667đ -> 15% = 4M, giảm 30% NĐ 398 = 2.8M -> nộp thêm 0, nộp thừa 2.2M

        _fakeRepo.AnnualAccumulatedRevenue = 1_400_000_000m;

        _fakeRepo.DeductibleExpenses = 1_373_333_333m; // Lãi YTD ~ 26.666.667đ * 15% = 4.000.000đ -> sau giảm 30% = 2.800.000đ

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Year,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.DeclarationBased,

            PriorDeclaredPitTaxOverride = 5_000_000m // Đã tạm nộp 5M

        };

        var result = await _service.GetTaxSummaryAsync(filter);

        Assert.Equal(4_000_000m, result.TaxDueYtdBeforeReduction);

        Assert.Equal(2_800_000m, result.TaxDueYtd);

        Assert.Equal(0m, result.DeclarationPitAmount); // Không phải nộp thêm

        Assert.Equal(2_200_000m, result.OverpaidTaxCarry); // Nộp thừa còn lại: 5M - 2.8M = 2.2M

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

            FinalizeAsync(_service, request, "accountant-user"));

        Assert.Equal("TAX_PERIOD_LOCKED", ex.ErrorCode);

    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenAllowSupersedeWithoutReason_ShouldThrowBusinessException()
    {
        // Arrange: Đã tồn tại snapshot nhưng yêu cầu supersede không cung cấp lý do
        _fakeRepo.ExistingSnapshot = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            Status = TaxSnapshotStatus.Finalized,
            RevisionNumber = 0
        };

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            AllowSupersedeExisting = true,
            RectificationReason = "" // Rỗng
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            FinalizeAsync(_service, request, "store-owner"));

        Assert.Equal("RECTIFICATION_REASON_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenAllowSupersedeWithValidReason_ShouldSupersedeAndIncrementRevision()
    {
        // Arrange: Snapshot ban đầu (Revision 0)
        var initialSnapshotId = Guid.NewGuid();
        var initialSnapshot = new HouseholdTaxSnapshot
        {
            Id = initialSnapshotId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            Status = TaxSnapshotStatus.Finalized,
            RevisionNumber = 0
        };
        _fakeRepo.SavedSnapshots.Add(initialSnapshot);
        _fakeRepo.ExistingSnapshot = initialSnapshot;

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            AllowSupersedeExisting = true,
            RectificationReason = "BBĐC-01/2026: Điều chỉnh bổ sung 2 hóa đơn đầu vào bỏ sót"
        };

        // Act
        var result = await FinalizeAsync(_service, request, "store-owner");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TaxSnapshotStatus.Finalized, result.Status);
        Assert.Equal(1, result.RevisionNumber);
        Assert.Equal(initialSnapshotId, result.SupersededSnapshotId);
        Assert.Equal("BBĐC-01/2026: Điều chỉnh bổ sung 2 hóa đơn đầu vào bỏ sót", result.RectificationReason);
        Assert.True(result.IsIntegrityValid);

        // Snapshot cũ phải được cập nhật sang Superseded
        Assert.Equal(TaxSnapshotStatus.Superseded, initialSnapshot.Status);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenSupersededTwice_ShouldIncrementRevisionToTwoAndLinkPrevious()
    {
        // Arrange: Tạo snapshot gốc (Rev 0)
        var rev0 = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            Status = TaxSnapshotStatus.Finalized,
            RevisionNumber = 0
        };
        _fakeRepo.SavedSnapshots.Add(rev0);
        _fakeRepo.ExistingSnapshot = rev0;

        // Lần 1: Supersede Rev 0 -> Rev 1
        var req1 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            AllowSupersedeExisting = true,
            RectificationReason = "Lý do lần 1"
        };
        var res1 = await FinalizeAsync(_service, req1, "owner-01");
        Assert.Equal(1, res1.RevisionNumber);

        // Lần 2: Supersede Rev 1 -> Rev 2
        var req2 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            AllowSupersedeExisting = true,
            RectificationReason = "Lý do lần 2: Điều chỉnh tiếp lần hai"
        };
        var res2 = await FinalizeAsync(_service, req2, "owner-01");

        // Assert: Rev 2 nối với Id của Rev 1
        Assert.Equal(2, res2.RevisionNumber);
        Assert.Equal(res1.Id, res2.SupersededSnapshotId);
        Assert.Equal(TaxSnapshotStatus.Finalized, res2.Status);
        Assert.Equal(TaxSnapshotStatus.Superseded, _fakeRepo.SavedSnapshots.First(s => s.Id == res1.Id).Status);
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

            FinalizeAsync(_service, request, "accountant-user"));

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

        Assert.Equal(2_500_000m, result.PitBeforeReduction); // 500M * 0.5% = 2.500.000 VNĐ
        Assert.Equal(750_000m, result.PitReductionAmount); // Giảm 30% NQ 43/2026 & NĐ 398/2026
        Assert.Equal(1_750_000m, result.PitAmount); // Sau giảm 30% = 1.750.000 VNĐ

        Assert.Equal(5_750_000m, result.TotalTaxAmount);

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

        // Arrange: Doanh thu năm đạt 3.500.000.000 VNĐ (> 3 tỷ, kỳ trước đã > 3 tỷ) — Quyết toán năm Nhóm 3 (17% lợi nhuận, giảm 30% NĐ 398)

        _fakeRepo.AnnualAccumulatedRevenue = 3_500_000_000m;

        _fakeRepo.PriorPeriodsAccumulatedRevenue = 3_100_000_000m;

        _fakeRepo.GrossRevenue = 700_000_000m;

        _fakeRepo.DiscountAmount = 0m;

        _fakeRepo.ReturnAmount = 0m;

        _fakeRepo.DeductibleExpenses = 500_000_000m;

        var filter = new HouseholdTaxFilterDto

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Year,

            PeriodNumber = 1,

            CalculationMethod = TaxCalculationMethod.RevenuePercentage // Dù người dùng chọn Tỷ lệ

        };

        // Act

        var result = await _service.GetTaxSummaryAsync(filter);

        // Assert: Tự động chuyển sang Nhóm 3 bắt buộc tính theo Thu nhập tính thuế (17%, giảm 30% NĐ 398 vì <= 10 tỷ)

        Assert.Equal(HouseholdTaxGroup.Group3_3BTo50B, result.HouseholdGroup);

        Assert.Equal(700_000_000m, result.VatTaxableRevenue);

        Assert.Equal(7_000_000m, result.VatAmount); // 700M * 1% = 7M

        // YTD = 3.5B - 500M = 3.0B; 3.0B * 17% = 510M; giảm 30% NĐ 398 (-153M) = 357M

        Assert.Equal(3_000_000_000m, result.TaxableIncomeYtd);

        Assert.Equal(17.0m, result.PitRate); // Thuế suất quyết toán Nhóm 3: 17%

        Assert.Equal(510_000_000m, result.TaxDueYtdBeforeReduction);

        Assert.Equal(357_000_000m, result.DeclarationPitAmount);

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

    public async Task FinalizeTaxSnapshot_UsesSerializableTransaction_WhenUnitOfWorkIsAvailable()

    {

        var unitOfWork = new RecordingUnitOfWork();

        var service = CreateService(_fakeRepo, unitOfWork);

        var request = new CreateTaxSnapshotRequest

        {

            Year = 2026,

            PeriodType = TaxPeriodType.Quarter,

            PeriodNumber = 1

        };



        var result = await FinalizeAsync(service, request, "store_owner");



        Assert.Equal(IsolationLevel.Serializable, unitOfWork.RequestedIsolationLevel);

        Assert.Equal(2026, result.Year);

        Assert.Single(_fakeRepo.SavedSnapshots);

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

            FinalizeAsync(_service, request, "admin_user"));

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

        var result = await FinalizeAsync(_service, request, "store_owner_nguyen");

        Assert.NotNull(result);

        Assert.True(result.IsCogsEstimated);

        Assert.NotEmpty(result.AuditSignatureHash);

        var savedSnapshot = await _fakeRepo.GetSnapshotAsync(2026, TaxPeriodType.Quarter, 1, null);

        Assert.NotNull(savedSnapshot);

        Assert.Contains("ownerConfirmation", savedSnapshot.BookDataJson);

        Assert.Contains("ExemptionThreshold", savedSnapshot.TaxPolicyJson);
        Assert.Equal(savedSnapshot.TaxPolicyJson, result.TaxPolicyJson);

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
    public void TaxSnapshotSignatureService_WhenTaxPolicyJsonTampered_VerificationFails()
    {
        var signatureService = new TaxSnapshotSignatureService();
        var snapshot = new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            TaxPolicyJson = "{\"version\":\"2026\"}",
            FinalizedAtUtc = DateTime.UtcNow,
            FinalizedBy = "owner"
        };
        snapshot.AuditSignatureHash = signatureService.ComputeSignature(snapshot);
        Assert.True(signatureService.VerifySignature(snapshot));

        snapshot.TaxPolicyJson = "{\"version\":\"tampered\"}";
        Assert.False(signatureService.VerifySignature(snapshot));
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

            PeriodType = TaxPeriodType.Year,

            PeriodNumber = 1,

            Tier2Method = "PROFIT_15"

        };

        // WHEN

        var summary = await _service.GetTaxSummaryAsync(filter);

        // THEN

        Assert.Equal("PROFIT_15", summary.Tier2Method);

        Assert.Equal(TaxCalculationMethod.DeclarationBased, summary.CalculationMethod);

        // Mô hình quyết toán năm Kê khai: Thu nhập tính thuế lũy kế = 1.500tr - 250tr = 1.250.000.000đ
        // Thuế TNCN 15% = 187.500.000đ -> Giảm 30% NĐ 398 (-56.250.000đ) = 131.250.000đ

        Assert.Equal(187_500_000m, summary.PitBeforeReduction);

        Assert.Equal(131_250_000m, summary.PitAmount);

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
        Assert.True(result.ThresholdExceededNote.Contains("12,000,000đ") || result.ThresholdExceededNote.Contains("12.000.000đ"));
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

        // Assert: Tùy chọn QUARTER_ONLY đã bị bãi bỏ vì thiếu căn cứ pháp lý; luôn áp dụng PRUDENT_YTD (12.000.000đ)
        Assert.Equal(12_000_000m, result.VatAmount);
        Assert.Equal(4_000_000m, result.QuarterlyVatAmount);
        Assert.Equal(12_000_000m, result.RetrospectiveVatAmount);
        Assert.Equal("PRUDENT_YTD", result.VatCalculationOption);
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

    [Fact]
    public void GetFilingDeadlineLocal_QuarterDeadlineOnWeekend_RollsToMonday()
    {
        var deadline = HouseholdTaxService.GetFilingDeadlineLocal(2026, TaxPeriodType.Quarter, 3);

        Assert.Equal(new DateTime(2026, 11, 2), deadline);
        Assert.Equal(DayOfWeek.Monday, deadline.DayOfWeek);
    }

    [Fact]
    public async Task GetDeclarationPreview_ReturnsTaxpayerIndicatorsAndAppendixData()
    {
        _fakeRepo.AnnualAccumulatedRevenue = 1_500_000_000m;
        _fakeRepo.GrossRevenue = 400_000_000m;
        _fakeRepo.CogsAmount = 180_000_000m;
        _fakeRepo.OpexAmount = 40_000_000m;
        _fakeRepo.TaxpayerProfile = new HouseholdTaxpayerProfileDto
        {
            TaxpayerName = "Hộ kinh doanh Minh Tâm",
            TaxCode = "0312345678",
            BusinessLocation = "Quận 1, TP.HCM",
            PhoneNumber = "0909000000",
            TaxAuthorityName = "Thuế cơ sở 1 TP.HCM"
        };

        var result = await _service.GetDeclarationPreviewAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.DeclarationBased
        });

        Assert.Equal("01/CNKD", result.FormCode);
        Assert.Equal("0312345678", result.Taxpayer.TaxCode);
        Assert.True(result.IsProfileComplete);
        Assert.True(result.RequiresBusinessActivityAppendix);
        Assert.Equal("01-2/BK-HĐKD", result.AppendixCode);
        Assert.Single(result.Lines);
        Assert.Equal(result.TotalTaxAmount, result.TotalVatAmount + result.TotalPitAmount);
    }

    [Fact]
    public async Task ExportDeclarationXml_FinalizedAndValidSnapshot_ReturnsServerXmlWithEscapedText()
    {
        var signatureService = new TaxSnapshotSignatureService();
        var snapshot = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            TaxableRevenueInPeriod = 250_000_000m,
            VatRate = 0.8m,
            VatAmount = 2_000_000m,
            PitRate = 0.5m,
            PitAmount = 1_250_000m,
            TotalTaxAmount = 3_250_000m,
            Status = TaxSnapshotStatus.Finalized,
            FinalizedAtUtc = new DateTime(2026, 4, 10, 2, 0, 0, DateTimeKind.Utc),
            FinalizedBy = "owner-a",
            BookDataJson = "{}"
        };
        snapshot.AuditSignatureHash = signatureService.ComputeSignature(snapshot);
        _fakeRepo.SavedSnapshots.Add(snapshot);
        _fakeRepo.TaxpayerProfile = new HouseholdTaxpayerProfileDto
        {
            TaxpayerName = "HKD A & B <!ENTITY xxe SYSTEM 'file:///etc/passwd'>",
            TaxCode = "0312345678"
        };

        var result = await _service.ExportDeclarationXmlAsync(snapshot.Id);
        var xml = System.Text.Encoding.UTF8.GetString(result.Content).TrimStart('\uFEFF');
        var document = System.Xml.Linq.XDocument.Parse(xml);

        Assert.StartsWith("ToKhai_01_CNKD", result.FileName);
        Assert.DoesNotContain("<!DOCTYPE", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Null(document.DocumentType);
        Assert.Contains("HKD A &amp; B &lt;!ENTITY", xml);
    }

    [Fact]
    public async Task ExportDeclarationXml_NonFinalizedSnapshot_IsRejected()
    {
        var snapshot = new HouseholdTaxSnapshot { Id = Guid.NewGuid(), Status = TaxSnapshotStatus.Superseded };
        _fakeRepo.SavedSnapshots.Add(snapshot);

        var error = await Assert.ThrowsAsync<BusinessException>(() => _service.ExportDeclarationXmlAsync(snapshot.Id));

        Assert.Equal("SNAPSHOT_NOT_ACTIVE", error.ErrorCode);
    }

    [Fact]
    public async Task ExportDeclarationXml_TamperedSnapshot_IsRejected()
    {
        var signatureService = new TaxSnapshotSignatureService();
        var snapshot = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            TaxableRevenueInPeriod = 200_000_000m,
            TotalTaxAmount = 2_000_000m,
            Status = TaxSnapshotStatus.Finalized,
            FinalizedAtUtc = DateTime.UtcNow,
            FinalizedBy = "owner-a"
        };
        snapshot.AuditSignatureHash = signatureService.ComputeSignature(snapshot);
        snapshot.TotalTaxAmount = 99_000_000m;
        _fakeRepo.SavedSnapshots.Add(snapshot);

        var error = await Assert.ThrowsAsync<BusinessException>(() => _service.ExportDeclarationXmlAsync(snapshot.Id));

        Assert.Equal("SNAPSHOT_INTEGRITY_INVALID", error.ErrorCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STAFF")]
    [InlineData("MANAGER")]
    [InlineData("CASHIER")]
    [InlineData("StoreOwner")]
    public async Task GetTaxSummary_WhenRoleIsNotCanonicalOwner_Rejects(string role)
    {
        var context = new OwnerTenantContext { CurrentRole = role };
        var service = CreateService(_fakeRepo, tenantContext: context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        }));
    }

    [Fact]
    public async Task GetTaxSummary_WhenTenantIsMissing_Rejects()
    {
        var context = new OwnerTenantContext { CurrentTenantId = string.Empty };
        var service = CreateService(_fakeRepo, tenantContext: context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        }));
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenPinIsMissing_RejectsReauthentication()
    {
        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.FinalizeTaxSnapshotAsync(request, "owner", "owner-user-id"));

        Assert.Equal("PIN_REQUIRED", error.ErrorCode);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenUserIdentityIsMissing_RejectsReauthentication()
    {
        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            SecurityPin = "123456"
        };

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.FinalizeTaxSnapshotAsync(request, "owner"));

        Assert.Equal("USER_ID_REQUIRED", error.ErrorCode);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenPinIsInvalid_VerifiesPinOutsideTransactionSoLockoutIsNotRolledBack()
    {
        // BUG-074: Wrong PIN must fail BEFORE opening Serializable transaction so FailedPinAttempts / PinLockedUntilUtc commits independently
        var callOrder = new List<string>();
        var unitOfWork = new RecordingUnitOfWork { CallOrder = callOrder };
        var pinService = new RecordingPinService(new PinVerificationResult(false, "INVALID_PIN", "Mã PIN không chính xác."))
        {
            CallOrder = callOrder
        };
        var service = CreateService(_fakeRepo, unitOfWork: unitOfWork, pinService: pinService);

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchId = Guid.NewGuid(),
            SecurityPin = "000000"
        };

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            service.FinalizeTaxSnapshotAsync(request, "owner", "owner-user-id"));

        Assert.Equal("INVALID_PIN", error.ErrorCode);
        Assert.Equal(1, pinService.VerifyCallCount);
        Assert.Equal(0, unitOfWork.TransactionExecutionCount);
        Assert.Equal(new[] { "VerifyPin" }, callOrder);
        Assert.Null(request.BranchId);
    }

    [Fact]
    public async Task FinalizeTaxSnapshot_WhenPinIsValid_VerifiesPinBeforeOpeningTransaction()
    {
        var callOrder = new List<string>();
        var unitOfWork = new RecordingUnitOfWork { CallOrder = callOrder };
        var pinService = new RecordingPinService(new PinVerificationResult(true))
        {
            CallOrder = callOrder
        };
        var service = CreateService(_fakeRepo, unitOfWork: unitOfWork, pinService: pinService);

        var request = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            SecurityPin = "123456"
        };

        var snapshot = await service.FinalizeTaxSnapshotAsync(request, "owner", "owner-user-id");

        Assert.NotNull(snapshot);
        Assert.Equal(1, pinService.VerifyCallCount);
        Assert.Equal(1, unitOfWork.TransactionExecutionCount);
        Assert.Equal(IsolationLevel.Serializable, unitOfWork.RequestedIsolationLevel);
        Assert.Equal(new[] { "VerifyPin", "ExecuteInTransaction" }, callOrder);
    }

}
