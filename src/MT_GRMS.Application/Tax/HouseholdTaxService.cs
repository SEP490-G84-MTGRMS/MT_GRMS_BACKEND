using Microsoft.Extensions.Configuration;
﻿using System.Security.Cryptography;

using System.Data;

using System.Text;

using System.Text.Json;
using System.Xml.Linq;
using MT_GRMS.Application.Notifications;

using MT_GRMS.Application.Tax.Dtos;

using MT_GRMS.Application.Tax.Strategies;

using MT_GRMS.Domain.Entities.Finance;

using MT_GRMS.Domain.Enums;

using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Tax;

public class HouseholdTaxService : IHouseholdTaxService

{

    private readonly IHouseholdTaxRepository _repository;

    private readonly ITaxCalculationStrategyFactory _strategyFactory;

    private readonly Security.IPinVerificationService? _pinService;

    private readonly Security.ITaxSnapshotSignatureService _signatureService;
    private readonly TaxPolicy _taxPolicy;
    private readonly Common.ITenantContext _tenantContext;
    private readonly Common.IUnitOfWork? _unitOfWork;
    private readonly NotificationOutbox? _notificationOutbox;

    public const decimal RetailVatRate = RevenuePercentageTaxStrategy.RetailVatRate;

    public const decimal RetailPitRate = RevenuePercentageTaxStrategy.RetailPitRate;

    public const decimal DeclarationDefaultPitRate = DeclarationBasedTaxStrategy.DeclarationDefaultPitRate;

    public HouseholdTaxService(IHouseholdTaxRepository repository, Common.ITenantContext tenantContext, Common.IUnitOfWork? unitOfWork = null)

        : this(repository, new TaxCalculationStrategyFactory(new ITaxCalculationStrategy[]

        {

            new RevenuePercentageTaxStrategy(),

            new DeclarationBasedTaxStrategy()

        }), null, null, null, tenantContext, unitOfWork)

    {

    }

    public HouseholdTaxService(
        IHouseholdTaxRepository repository,
        ITaxCalculationStrategyFactory strategyFactory,
        Security.IPinVerificationService? pinService = null,
        Security.ITaxSnapshotSignatureService? signatureService = null,
        IConfiguration? configuration = null,
        Common.ITenantContext? tenantContext = null,
        Common.IUnitOfWork? unitOfWork = null,
        NotificationOutbox? notificationOutbox = null)
    {
        _repository = repository;
        _strategyFactory = strategyFactory;
        _pinService = pinService;
        _signatureService = signatureService ?? new Security.TaxSnapshotSignatureService();
        _taxPolicy = TaxPolicy.Load(configuration);
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _unitOfWork = unitOfWork;
        _notificationOutbox = notificationOutbox;
    }

    private DateTime GetVatReductionDeadlineUtc() => _taxPolicy.VatReductionDeadlineUtc;

    public static DateTime GetFilingDeadlineLocal(int year, TaxPeriodType periodType, int periodNumber)
    {
        DateTime deadline = periodType switch
        {
            TaxPeriodType.Month => new DateTime(year, Math.Clamp(periodNumber, 1, 12), 1).AddMonths(1).AddDays(19),
            TaxPeriodType.Quarter => new DateTime(year, (Math.Clamp(periodNumber, 1, 4) - 1) * 3 + 1, 1).AddMonths(4).AddDays(-1),
            TaxPeriodType.Year => new DateTime(year + 1, 1, 31),
            _ => new DateTime(year, 1, 31)
        };

        while (deadline.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            deadline = deadline.AddDays(1);

        return DateTime.SpecifyKind(deadline, DateTimeKind.Unspecified);
    }

    private async Task<(TaxCalculationMethod? Method, int? StartYear)> GetRequiredPitMethodAsync(int year, CancellationToken ct)
    {
        var snapshots = await _repository.GetSnapshotsAsync(null, null, ct);
        foreach (var snapshot in snapshots.Where(s => s.Status == TaxSnapshotStatus.Finalized && s.Year <= year)
                     .OrderByDescending(s => s.Year).ThenByDescending(s => s.PeriodNumber))
        {
            if (string.IsNullOrWhiteSpace(snapshot.TaxPolicyJson) || snapshot.TaxPolicyJson == "{}") continue;
            var policy = JsonSerializer.Deserialize<TaxSnapshotPolicy>(snapshot.TaxPolicyJson);
            var methodLock = policy?.MethodLock;
            if (methodLock != null && methodLock.StartYear <= year && methodLock.EndYear >= year)
                return (methodLock.Method, methodLock.StartYear);
        }
        return (null, null);
    }

    public async Task<HouseholdTaxSummaryDto> GetTaxSummaryAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default)

    {
        RequireOwner();

        var (fromDateUtc, toDateUtc, priorPeriodToDateUtc) = GetDateRangesForPeriod(filter.Year, filter.PeriodType, filter.PeriodNumber);

        // 1. Kiểm tra snapshot đã chốt

        var snapshot = await _repository.GetSnapshotAsync(

            filter.Year,

            filter.PeriodType,

            filter.PeriodNumber,

            filter.BranchId,

            cancellationToken);

        // 2. Tính doanh thu lũy kế đến hết kỳ trước và đến cuối kỳ hiện tại (Tính trên toàn chuỗi Tenant)

        var priorPeriodsRevenue = priorPeriodToDateUtc.HasValue

            ? await _repository.GetAnnualAccumulatedRevenueAsync(filter.Year, priorPeriodToDateUtc.Value, cancellationToken)

            : 0m;

        var annualAccumulatedRevenue = await _repository.GetAnnualAccumulatedRevenueAsync(

            filter.Year,

            toDateUtc,

            cancellationToken);

        var (group, groupName, isExempt) = DetermineHouseholdGroup(annualAccumulatedRevenue);

        // Kiểm tra Doanh thu gộp (Gross) vs Doanh thu thuần (Net) cho các ngưỡng 1 tỷ và 3 tỷ
        var vnTzInit = GetVietnamTimeZone();
        var startOfYearVnInit = new DateTime(filter.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var startOfYearUtcInit = TimeZoneInfo.ConvertTimeToUtc(startOfYearVnInit, vnTzInit);
        var (annualGross, _, _) = await _repository.GetPeriodRevenueAsync(startOfYearUtcInit, toDateUtc, null, cancellationToken);

        var isGrossOver1BNetUnder1B = annualGross > _taxPolicy.ExemptionThreshold && annualAccumulatedRevenue <= _taxPolicy.ExemptionThreshold;
        var isGrossOver3BNetUnder3B = annualGross > _taxPolicy.MethodSwitchRevenueThreshold && annualAccumulatedRevenue <= _taxPolicy.MethodSwitchRevenueThreshold;
        string? netVsGrossWarning = null;
        decimal? grossBasedEstimatedTax = null;

        // Cảnh báo tiến trình ngưỡng Nhóm 4 (40 tỷ = 80%, 50 tỷ = 100%)
        var isApproachingGroup4 = annualAccumulatedRevenue >= 40_000_000_000m && annualAccumulatedRevenue < 50_000_000_000m;
        var isExceedingGroup4 = annualAccumulatedRevenue >= 50_000_000_000m;

        // Cảnh báo tiến trình ngưỡng 3 tỷ VNĐ (Điểm d Khoản 5 Điều 4 Nghị định 68/2026/NĐ-CP)
        var isApproaching3B = annualAccumulatedRevenue >= _taxPolicy.MethodSwitchRevenueThreshold * 0.9m && annualAccumulatedRevenue < _taxPolicy.MethodSwitchRevenueThreshold;
        var isExceeding3B = annualAccumulatedRevenue > _taxPolicy.MethodSwitchRevenueThreshold;
        var is3BCrossedMidYear = priorPeriodsRevenue <= _taxPolicy.MethodSwitchRevenueThreshold && annualAccumulatedRevenue > _taxPolicy.MethodSwitchRevenueThreshold;

        // Cảnh báo vượt ngưỡng miễn thuế 1 tỷ VNĐ giữa năm (từ <= 1 tỷ lên > 1 tỷ)
        var isThresholdCrossedMidYear = priorPeriodsRevenue <= _taxPolicy.ExemptionThreshold && annualAccumulatedRevenue > _taxPolicy.ExemptionThreshold;

        string? thresholdExceededNote = null;

        if (isThresholdCrossedMidYear)
        {
            // Tính số ước tính GTGT nếu cơ quan thuế truy thu trên toàn bộ doanh thu lũy kế cả năm (để chủ hộ dự phòng)
            var retrospectiveVatEstimate = Math.Round(annualAccumulatedRevenue * 0.01m, 0, MidpointRounding.AwayFromZero);
            thresholdExceededNote = $"Doanh thu lũy kế năm đã vượt ngưỡng 1 tỷ VNĐ trong kỳ này (từ {priorPeriodsRevenue:N0}đ lên {annualAccumulatedRevenue:N0}đ). Theo quy định, hộ kinh doanh bắt đầu khai và nộp thuế kể từ quý phát sinh doanh thu vượt (Điều 10 NĐ 68/2026/NĐ-CP). Thuế TNCN tính trên phần vượt ngưỡng ({Math.Max(0, annualAccumulatedRevenue - _taxPolicy.ExemptionThreshold):N0}đ). Về thuế GTGT: Hệ thống đang ước tính trên doanh thu phát sinh trong kỳ; trường hợp cơ quan thuế yêu cầu nộp GTGT trên toàn bộ doanh thu cả năm, số thuế GTGT dự kiến khoảng {retrospectiveVatEstimate:N0}đ. Khuyến nghị chủ hộ tham vấn cơ quan thuế địa bàn để xác định chính xác phương án kê khai.";
        }

        else if (is3BCrossedMidYear)

        {

            thresholdExceededNote = $"Doanh thu lũy kế năm đã vượt ngưỡng 3 tỷ VNĐ trong kỳ này (từ {priorPeriodsRevenue:N0}đ lên {annualAccumulatedRevenue:N0}đ). Theo Điểm d Khoản 5 Điều 4 Nghị định 68/2026/NĐ-CP, hộ kinh doanh tiếp tục thực hiện phương pháp tính thuế hiện tại cho đến hết năm {filter.Year}, và bắt buộc chuyển đổi sang phương pháp kê khai từ năm {filter.Year + 1} (ổn định trong 2 năm liên tiếp).";

        }

        // 3. Doanh thu phát sinh trong kỳ

        var (gross, discount, returns) = await _repository.GetPeriodRevenueAsync(
            fromDateUtc,
            toDateUtc,
            filter.BranchId,
            cancellationToken);

        var periodNetRevenue = gross - discount - returns;
        var taxableRevenueInPeriod = Math.Max(0, periodNetRevenue);

        // 4. Xác định phương pháp tính thuế theo khóa năm và quy tắc chuyển đổi ngưỡng 3 tỷ
        int? declarationMethodStartYear = null;

        var (requiredMethod, lockStartYear) = await GetRequiredPitMethodAsync(filter.Year, cancellationToken);

        if (requiredMethod == TaxCalculationMethod.RevenuePercentage &&
            (filter.Tier2Method == "PROFIT_15" || filter.CalculationMethod == TaxCalculationMethod.DeclarationBased))
            throw new BusinessException("TAX_METHOD_LOCKED", $"Phương pháp TNCN của năm {filter.Year} đã được chốt theo tỷ lệ doanh thu.");

        bool isDeclaration;

        if (filter.Tier2Method == "PROFIT_15" || filter.CalculationMethod == TaxCalculationMethod.DeclarationBased)
        {
            // Cho phép người dùng mô phỏng / chọn phương án kê khai lợi nhuận 15%
            isDeclaration = true;
            declarationMethodStartYear = lockStartYear ?? filter.Year;
        }
        else if (requiredMethod.HasValue)
        {
            // Đã có khóa năm thuế từ thiết lập năm hiện tại hoặc thừa kế năm trước / fallback (ổn định 2 năm)
            isDeclaration = requiredMethod.Value == TaxCalculationMethod.DeclarationBased;
            if (isDeclaration)
            {
                declarationMethodStartYear = lockStartYear ?? filter.Year;
            }
        }
        else if (group == HouseholdTaxGroup.Group2_1BTo3B)
        {
            // Nhóm 2: Hỗ trợ 2 phương pháp (Cách 1: Doanh thu vượt 1 tỷ * 0.5%, Cách 2: 15% lợi nhuận khi chọn PROFIT_15)
            isDeclaration = filter.Tier2Method == "PROFIT_15" || filter.CalculationMethod == TaxCalculationMethod.DeclarationBased;
        }

        else if (group is HouseholdTaxGroup.Group3_3BTo50B or HouseholdTaxGroup.Group4_Over50B)
        {
            if (filter.CalculationMethod == TaxCalculationMethod.DeclarationBased)
            {
                isDeclaration = true;
            }
            else
            {
                // Vượt ngưỡng 3 tỷ: Nếu vừa vượt giữa năm thì tiếp tục phương pháp tỷ lệ % đến hết năm, chưa bị ép chuyển
                bool crossedMidYear = priorPeriodsRevenue <= _taxPolicy.MethodSwitchRevenueThreshold;
                isDeclaration = !crossedMidYear;
            }
        }

        else

        {

            isDeclaration = false;

        }

        var calculationMethod = isDeclaration 

            ? TaxCalculationMethod.DeclarationBased 

            : TaxCalculationMethod.RevenuePercentage;

        var tier2Method = group == HouseholdTaxGroup.Group2_1BTo3B

            ? (isDeclaration ? "PROFIT_15" : "REVENUE_EXCESS")

            : "N/A";

        var (nextYearMethod, nextYearStart) = await GetRequiredPitMethodAsync(filter.Year + 1, cancellationToken);

        declarationMethodStartYear ??= nextYearMethod == TaxCalculationMethod.DeclarationBased

            ? nextYearStart

            : (is3BCrossedMidYear ? filter.Year + 1 : null);

        // 5. Chuẩn bị dữ liệu chi phí và thuế kỳ trước cho Strategy (Pure Logic)

        decimal periodCogs = 0m;

        decimal periodOpex = 0m;

        bool isCogsEstimated = false;

        decimal deductibleExpenses = 0m;

        decimal ytdExpenses = 0m;

        if (isDeclaration && !isExempt)

        {

            var (cogs, estimated) = await _repository.GetPeriodCogsAsync(fromDateUtc, toDateUtc, filter.BranchId, cancellationToken);

            var opex = await _repository.GetPeriodOpexAsync(fromDateUtc, toDateUtc, filter.BranchId, cancellationToken);

            periodCogs = cogs;

            periodOpex = opex;

            isCogsEstimated = estimated;

            var directExpenses = await _repository.GetPeriodDeductibleExpensesAsync(fromDateUtc, toDateUtc, filter.BranchId, cancellationToken);

            deductibleExpenses = (periodCogs + periodOpex) > 0 ? (periodCogs + periodOpex) : directExpenses;

            var startOfYearVn = new DateTime(filter.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

            var startOfYearUtc = TimeZoneInfo.ConvertTimeToUtc(startOfYearVn, GetVietnamTimeZone());

            var (ytdCogs, _) = await _repository.GetPeriodCogsAsync(startOfYearUtc, toDateUtc, null, cancellationToken);

            var ytdOpex = await _repository.GetPeriodOpexAsync(startOfYearUtc, toDateUtc, null, cancellationToken);

            var directYtdExpenses = await _repository.GetPeriodDeductibleExpensesAsync(startOfYearUtc, toDateUtc, null, cancellationToken);

            ytdExpenses = (ytdCogs + ytdOpex) > 0 ? (ytdCogs + ytdOpex) : directYtdExpenses;

        }

        decimal priorDeclaredPitTax = 0;
        decimal priorDeclaredVatTax = 0;

        if (!isExempt)

        {

            if (filter.PriorDeclaredPitTaxOverride.HasValue)

            {

                priorDeclaredPitTax = filter.PriorDeclaredPitTaxOverride.Value;

            }
            else
            {
                var priorSnapshots = await _repository.GetSnapshotsAsync(filter.Year, null, cancellationToken);
                priorDeclaredPitTax = priorSnapshots
                    .Where(s => s.Status == TaxSnapshotStatus.Finalized && s.Year == filter.Year && s.PeriodType == filter.PeriodType && s.PeriodNumber < filter.PeriodNumber)
                    .Sum(s => s.PitAmount);

                if (filter.PriorDeclaredVatTaxOverride.HasValue)
                {
                    priorDeclaredVatTax = filter.PriorDeclaredVatTaxOverride.Value;
                }
                else
                {
                    priorDeclaredVatTax = priorSnapshots
                        .Where(s => s.Status == TaxSnapshotStatus.Finalized && s.Year == filter.Year && s.PeriodType == filter.PeriodType && s.PeriodNumber < filter.PeriodNumber)
                        .Sum(s => s.VatAmount);
                }
            }

            if (filter.PriorDeclaredPitTaxOverride.HasValue && !filter.PriorDeclaredVatTaxOverride.HasValue)
            {
                var priorSnapshots = await _repository.GetSnapshotsAsync(filter.Year, null, cancellationToken);
                priorDeclaredVatTax = priorSnapshots
                    .Where(s => s.Status == TaxSnapshotStatus.Finalized && s.Year == filter.Year && s.PeriodType == filter.PeriodType && s.PeriodNumber < filter.PeriodNumber)
                    .Sum(s => s.VatAmount);
            }

        }

        // 6. Tách doanh thu giảm thuế GTGT & Tính toán thuế qua Strategy

        var vatReductionDeadlineUtc = GetVatReductionDeadlineUtc();

        // Kỳ có phát sinh trước hạn 31/12/2026 17:00:00 UTC (kể cả kỳ vắt qua ranh giới) đều được hưởng 0.8% cho các dòng hàng trước hạn
        var isWithinReductionPeriod = fromDateUtc < vatReductionDeadlineUtc;
        var applyVatReduction = filter.ApplyVatReduction2026 && isWithinReductionPeriod;

        decimal? reducedVatRevenue = null;

        decimal? standardVatRevenue = null;

        if (applyVatReduction && !isExempt)

        {

            if (filter.ReducedVatRevenueInPeriod.HasValue || filter.StandardVatRevenueInPeriod.HasValue)

            {

                reducedVatRevenue = filter.ReducedVatRevenueInPeriod;

                standardVatRevenue = filter.StandardVatRevenueInPeriod;

            }

            else

            {

                var (splitReduced, splitStandard) = await _repository.GetPeriodRevenueSplitAsync(

                    fromDateUtc,

                    toDateUtc,

                    filter.BranchId,

                    cancellationToken);

                reducedVatRevenue = splitReduced;

                standardVatRevenue = splitStandard;

            }

        }

        var calculationContext = new TaxCalculationContext

        {

            Group = group,
            ExemptionThreshold = _taxPolicy.ExemptionThreshold,

            IsExempt = isExempt,

            AnnualAccumulatedRevenue = annualAccumulatedRevenue,

            TaxableRevenueInPeriod = taxableRevenueInPeriod,

            ReducedVatRevenueInPeriod = reducedVatRevenue,

            StandardVatRevenueInPeriod = standardVatRevenue,

            CustomVatRate = filter.CustomVatRate,

            ApplyVatReduction2026 = applyVatReduction,

            VatReductionPercentage = filter.VatReductionPercentage,

            CustomPitRate = filter.CustomPitRate,

            PriorDeclaredPitTax = priorDeclaredPitTax,

            PeriodCogs = periodCogs,

            PeriodOpex = periodOpex,

            IsCogsEstimated = isCogsEstimated,

            PeriodDeductibleExpenses = deductibleExpenses,

            YtdDeductibleExpenses = ytdExpenses

        };

        var strategy = _strategyFactory.Resolve(calculationMethod);

        var calcResult = strategy.Calculate(calculationContext);

        // Tính toán các phương án thuế GTGT khi vượt 1 tỷ giữa năm (Thận trọng YTD vs Tối thiểu theo kỳ)
        decimal retrospectiveVatAmount = 0m;
        decimal quarterlyVatAmount = calcResult.VatAmount;

        if (!isExempt)
        {
            var (ytdReducedRev, ytdStandardRev) = await _repository.GetPeriodRevenueSplitAsync(
                startOfYearUtcInit,
                toDateUtc,
                filter.BranchId,
                cancellationToken);

            var vatRateReduced = applyVatReduction ? 0.008m : 0.010m;
            var totalYtdVat = Math.Round(Math.Max(0, ytdReducedRev) * vatRateReduced, 0, MidpointRounding.AwayFromZero)
                + Math.Round(Math.Max(0, ytdStandardRev) * 0.010m, 0, MidpointRounding.AwayFromZero);
            // Trừ số thuế GTGT đã kê khai các kỳ trước để không bị tính trùng hoặc bỏ sót
            retrospectiveVatAmount = Math.Max(0m, totalYtdVat - priorDeclaredVatTax);
        }

        var vatOption = filter.VatOption ?? "PRUDENT_YTD";
        decimal finalVatAmount = calcResult.VatAmount;
        if (!isExempt)
        {
            if (isThresholdCrossedMidYear)
            {
                finalVatAmount = (vatOption == "QUARTER_ONLY") ? quarterlyVatAmount : retrospectiveVatAmount;
            }
            else if (filter.PriorDeclaredVatTaxOverride.HasValue && vatOption == "PRUDENT_YTD")
            {
                // Bù trừ chênh lệch YTD khi kỳ trước chốt thiếu (QUARTER_ONLY)
                finalVatAmount = Math.Max(quarterlyVatAmount, retrospectiveVatAmount);
            }
            else
            {
                finalVatAmount = quarterlyVatAmount;
            }
        }
        var totalTaxAmount = finalVatAmount + calcResult.PitAmount;

        if (isThresholdCrossedMidYear)
        {
            thresholdExceededNote = $"Doanh thu lũy kế năm đã vượt ngưỡng 1 tỷ VNĐ trong kỳ này (từ {priorPeriodsRevenue:N0}đ lên {annualAccumulatedRevenue:N0}đ). Theo quy định, hộ kinh doanh bắt đầu khai và nộp thuế kể từ quý phát sinh vượt (Điều 10 NĐ 68/2026/NĐ-CP). Thuế TNCN tính trên phần vượt ngưỡng ({Math.Max(0, annualAccumulatedRevenue - _taxPolicy.ExemptionThreshold):N0}đ). Về thuế GTGT: Mặc định áp dụng phương án thận trọng tính trên toàn bộ doanh thu lũy kế cả năm ({retrospectiveVatAmount:N0}đ, đã tách nhóm 0.8% và 1.0%); trường hợp cơ quan thuế chỉ yêu cầu nộp trên doanh thu phát sinh trong quý vượt, số thuế GTGT dự kiến là {quarterlyVatAmount:N0}đ. Khuyến nghị chủ hộ tham vấn cơ quan thuế địa bàn để xác định chính xác phương án kê khai.";
        }

        // 7. Lấy chứng từ sổ kế toán Thông tư 152/2025/TT-BTC

        var bookEntries = await _repository.GetBookEntriesAsync(

            fromDateUtc,

            toDateUtc,

            filter.BranchId,

            group,

            calculationMethod,

            null,

            cancellationToken);

        var primaryBook = group switch

        {

            HouseholdTaxGroup.Group1_Under1B => "S1a-HKD",

            HouseholdTaxGroup.Group2_1BTo3B => isDeclaration 

                ? "Bộ 5 sổ: S2b, S2c, S2d, S2e, S3a-HKD" 

                : "S2a-HKD",

            HouseholdTaxGroup.Group3_3BTo50B => "Bộ 5 sổ: S2b, S2c, S2d, S2e, S3a-HKD",

            _ => "Bộ 5 sổ: S2b, S2c, S2d, S2e, S3a-HKD (Kèm chế độ kế toán doanh nghiệp cho Nhóm 4)"

        };

        var s2eEntries = bookEntries.Where(e => e.BookCode == "S2e-HKD" && !e.IsSummaryRow).ToList();
        var totalCashBankReceipts = s2eEntries.Sum(e => e.InAmount ?? 0m);
        var totalCashBankDisbursements = s2eEntries.Sum(e => e.OutAmount ?? 0m);

        var booksSummary = new TaxBooksSummaryDto

        {

            PrimaryBookCode = primaryBook,

            TotalRecordedRevenue = taxableRevenueInPeriod,

            TotalRecordedExpenses = deductibleExpenses,

            TotalCashAndBankReceipts = totalCashBankReceipts,

            TotalCashAndBankDisbursements = totalCashBankDisbursements,

            TotalTaxObligationsAccrued = calcResult.TotalTaxAmount,

            TotalTaxPaidToState = snapshot != null ? snapshot.TotalTaxAmount : 0m

        };

        if (isGrossOver1BNetUnder1B)
        {
            var (ytdGrossReduced, ytdGrossStandard) = await _repository.GetPeriodRevenueSplitAsync(
                startOfYearUtcInit,
                toDateUtc,
                filter.BranchId,
                cancellationToken);

            var grossEstVat = Math.Round(Math.Max(0, ytdGrossReduced) * 0.008m, 0, MidpointRounding.AwayFromZero)
                + Math.Round(Math.Max(0, ytdGrossStandard) * 0.010m, 0, MidpointRounding.AwayFromZero);
            var grossEstPit = Math.Round(Math.Max(0m, annualGross - _taxPolicy.ExemptionThreshold) * 0.005m, 0, MidpointRounding.AwayFromZero);
            grossBasedEstimatedTax = grossEstVat + grossEstPit;

            netVsGrossWarning = $"Doanh thu gộp (Gross) lũy kế đã đạt {annualGross:N0}đ (vượt ngưỡng 1 tỷ VNĐ) nhưng Doanh thu thuần (Net sau chiết khấu/trả hàng) là {annualAccumulatedRevenue:N0}đ (vẫn dưới 1 tỷ VNĐ). Theo giả định thiết kế hệ thống, hộ kinh doanh vẫn thuộc diện chưa chịu thuế (Nhóm 1). Tuy nhiên, để dự phòng rủi ro nếu cơ quan thuế không chấp nhận trừ chiết khấu đối với ngưỡng miễn thuế hộ kinh doanh, số thuế ước tính nếu tính theo Doanh thu gộp (Gross) lũy kế là {grossBasedEstimatedTax:N0}đ (gồm GTGT ước tính {grossEstVat:N0}đ và TNCN ước tính {grossEstPit:N0}đ). Chủ hộ cần chủ động đối chiếu với cơ quan thuế địa phương.";
        }
        else if (isGrossOver3BNetUnder3B)
        {
            netVsGrossWarning = $"Doanh thu gộp (Gross) lũy kế đã đạt {annualGross:N0}đ (vượt ngưỡng 3 tỷ VNĐ) nhưng Doanh thu thuần (Net) là {annualAccumulatedRevenue:N0}đ (dưới 3 tỷ VNĐ). Theo giả định thiết kế hệ thống, hộ kinh doanh chưa bị bắt buộc chuyển sang phương pháp Kê khai. Khuyến nghị chủ hộ tham vấn cơ quan thuế về việc xác định ngưỡng 3 tỷ theo doanh thu gộp hay doanh thu thuần.";
        }

        return new HouseholdTaxSummaryDto

        {

            Year = filter.Year,

            PeriodType = filter.PeriodType,

            PeriodNumber = filter.PeriodNumber,

            BranchId = filter.BranchId,

            BranchName = filter.BranchId.HasValue ? $"Chi nhánh {filter.BranchId.Value.ToString()[..8]}" : "Toàn bộ hộ kinh doanh (Tenant)",

            FilingDeadlineLocal = GetFilingDeadlineLocal(filter.Year, filter.PeriodType, filter.PeriodNumber),

            FilingDeadlineNote = "Thời hạn dự kiến theo loại kỳ; đã điều chỉnh nếu rơi vào cuối tuần.",

            AnnualAccumulatedRevenue = annualAccumulatedRevenue,

            PriorPeriodsAccumulatedRevenue = priorPeriodsRevenue,

            HouseholdGroup = group,

            HouseholdGroupName = groupName,

            CalculationMethod = calculationMethod,

            Tier2Method = tier2Method,

            IsTaxExempt = isExempt,

            TaxExemptionThreshold = _taxPolicy.ExemptionThreshold,

            IsApproachingGroup4Threshold = isApproachingGroup4,

            IsExceedingGroup4Threshold = isExceedingGroup4,

            IsApproaching3BThreshold = isApproaching3B,

            IsExceeding3BThreshold = isExceeding3B,

            Is3BCrossedMidYear = is3BCrossedMidYear,

            DeclarationMethodStartYear = declarationMethodStartYear,

            IsThresholdCrossedMidYear = isThresholdCrossedMidYear,

            ThresholdExceededNote = thresholdExceededNote,

            GrossRevenueInPeriod = gross,

            DiscountAmountInPeriod = discount,

            ReturnAmountInPeriod = returns,

            TaxableRevenueInPeriod = taxableRevenueInPeriod,

            OriginalVatRate = calcResult.OriginalVatRate,

            VatReductionPercentage = calcResult.VatReductionPercentage,

            VatRate = calcResult.VatRate,

            IsVatReduced = calcResult.IsVatReduced,

            VatTaxableRevenue = calcResult.VatTaxableRevenue,

            VatAmount = finalVatAmount,
            QuarterlyVatAmount = quarterlyVatAmount,
            RetrospectiveVatAmount = retrospectiveVatAmount,
            VatCalculationOption = vatOption,
            PriorDeclaredVatTax = priorDeclaredVatTax,
            PeriodNetRevenue = periodNetRevenue,
            AnnualAccumulatedGrossRevenue = annualGross,
            IsGrossOver1BNetUnder1B = isGrossOver1BNetUnder1B,
            IsGrossOver3BNetUnder3B = isGrossOver3BNetUnder3B,
            NetVsGrossThresholdWarning = netVsGrossWarning,
            GrossBasedEstimatedTaxAmount = grossBasedEstimatedTax,

            VatBreakdown = new VatBreakdownDto

            {

                RevenueStandardRate10 = calcResult.VatBreakdown.RevenueStandardRate10,

                TaxAmountStandardRate10 = calcResult.VatBreakdown.TaxAmountStandardRate10,

                RevenueReducedRate08 = calcResult.VatBreakdown.RevenueReducedRate08,

                TaxAmountReducedRate08 = calcResult.VatBreakdown.TaxAmountReducedRate08,

                EffectiveVatRate = calcResult.VatBreakdown.EffectiveVatRate

            },

            PitRate = calcResult.PitRate,

            PitTaxableRevenue = calcResult.PitTaxableRevenue,

            PitAmount = calcResult.PitAmount,

            CogsAmount = calcResult.CogsAmount,

            OpexAmount = calcResult.OpexAmount,

            IsCogsEstimated = calcResult.IsCogsEstimated,

            DeductibleExpensesInPeriod = calcResult.DeductibleExpenses,

            YtdRevenue = calcResult.YtdRevenue,

            YtdExpenses = calcResult.YtdExpenses,

            TaxableIncomeYtd = calcResult.TaxableIncomeYtd,

            TaxDueYtd = calcResult.TaxDueYtd,

            PriorDeclaredPitTax = calcResult.PriorDeclaredPitTax,

            OverpaidTaxCarry = calcResult.OverpaidTaxCarry,

            TaxableIncomeDeclaration = calcResult.TaxableIncomeDeclaration,

            DeclarationPitAmount = calcResult.DeclarationPitAmount,

            TotalTaxAmount = totalTaxAmount,

            BooksSummary = booksSummary,

            IsFinalized = snapshot != null,

            SnapshotId = snapshot?.Id,

            FinalizedAtUtc = snapshot?.FinalizedAtUtc,

            FinalizedBy = snapshot?.FinalizedBy

        };

    }

    public async Task<HouseholdTaxDeclarationPreviewDto> GetDeclarationPreviewAsync(
        HouseholdTaxFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var summary = await GetTaxSummaryAsync(filter, cancellationToken);
        var profile = await _repository.GetTaxpayerProfileAsync(filter.BranchId, cancellationToken);
        var missingFields = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.TaxpayerName)) missingFields.Add("Tên người nộp thuế");
        if (string.IsNullOrWhiteSpace(profile.TaxCode)) missingFields.Add("Mã số thuế");
        if (string.IsNullOrWhiteSpace(profile.BusinessLocation)) missingFields.Add("Địa điểm kinh doanh");
        if (string.IsNullOrWhiteSpace(profile.TaxAuthorityName)) missingFields.Add("Cơ quan thuế quản lý");

        var periodLabel = filter.PeriodType switch
        {
            TaxPeriodType.Month => $"Tháng {filter.PeriodNumber}/{filter.Year}",
            TaxPeriodType.Quarter => $"Quý {filter.PeriodNumber}/{filter.Year}",
            TaxPeriodType.Year => $"Năm {filter.Year}",
            _ => $"Kỳ {filter.PeriodNumber}/{filter.Year}"
        };

        return new HouseholdTaxDeclarationPreviewDto
        {
            IsFinalized = summary.IsFinalized,
            SnapshotId = summary.SnapshotId,
            GeneratedAtUtc = DateTime.UtcNow,
            Year = filter.Year,
            PeriodType = filter.PeriodType,
            PeriodNumber = filter.PeriodNumber,
            PeriodLabel = periodLabel,
            FilingDeadlineLocal = summary.FilingDeadlineLocal,
            Taxpayer = profile,
            Lines = new List<TaxDeclarationLineDto>
            {
                new()
                {
                    Sequence = 1,
                    BusinessSector = "Hoạt động sản xuất, kinh doanh hàng hóa, cung cấp dịch vụ",
                    TaxCode = "HKD-BAN-LE",
                    TotalRevenue = summary.TaxableRevenueInPeriod,
                    VatNonTaxableRevenue = Math.Max(0, summary.TaxableRevenueInPeriod - summary.VatTaxableRevenue),
                    VatTaxableRevenue = summary.VatTaxableRevenue,
                    VatZeroRateRevenue = 0,
                    VatRate = summary.VatRate,
                    VatAmount = summary.VatAmount,
                    PitTaxableRevenue = summary.PitTaxableRevenue,
                    PitRevenueDeduction = 0,
                    PitRate = summary.PitRate,
                    PitAmount = summary.PitAmount
                }
            },
            TotalVatAmount = summary.VatAmount,
            TotalPitAmount = summary.PitAmount,
            TotalTaxAmount = summary.TotalTaxAmount,
            RequiresBusinessActivityAppendix = summary.CalculationMethod == TaxCalculationMethod.DeclarationBased,
            AppendixRevenue = summary.TaxableRevenueInPeriod,
            AppendixCogs = summary.CogsAmount,
            AppendixOpex = summary.OpexAmount,
            AppendixTaxableIncome = summary.TaxableIncomeDeclaration,
            IsProfileComplete = missingFields.Count == 0,
            MissingProfileFields = missingFields,
            DataNotice = summary.IsFinalized
                ? "Bản xem trước được lập từ kỳ đã chốt."
                : "Bản xem trước tạm tính; chưa phải hồ sơ đã chốt để xuất XML."
        };
    }

    public async Task<TaxDeclarationXmlResult> ExportDeclarationXmlAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var snapshot = await _repository.GetSnapshotByIdAsync(snapshotId, cancellationToken)
            ?? throw new BusinessException("SNAPSHOT_NOT_FOUND", "Không tìm thấy bản chốt kỳ tính thuế.");

        if (snapshot.Status != TaxSnapshotStatus.Finalized)
            throw new BusinessException("SNAPSHOT_NOT_ACTIVE", "Chỉ được xuất XML từ bản chốt đang có hiệu lực.");

        if (!_signatureService.VerifySignature(snapshot))
            throw new BusinessException("SNAPSHOT_INTEGRITY_INVALID", "Không thể xuất XML vì chữ ký toàn vẹn của bản chốt không hợp lệ.");

        var profile = await _repository.GetTaxpayerProfileAsync(snapshot.BranchId, cancellationToken);
        var filingDeadline = GetFilingDeadlineLocal(snapshot.Year, snapshot.PeriodType, snapshot.PeriodNumber);
        XNamespace ns = "urn:mt-grms:tax:01-cnkd:1.0";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "HouseholdTaxDeclaration",
                new XAttribute("formCode", "01/CNKD"),
                new XAttribute("schemaVersion", "MT-GRMS-1.0"),
                new XElement(ns + "DocumentStatus", "FINALIZED"),
                new XElement(ns + "SnapshotId", snapshot.Id),
                new XElement(ns + "RevisionNumber", snapshot.RevisionNumber),
                new XElement(ns + "Taxpayer",
                    new XElement(ns + "Name", profile.TaxpayerName),
                    new XElement(ns + "TaxCode", profile.TaxCode),
                    new XElement(ns + "BusinessLocation", profile.BusinessLocation),
                    new XElement(ns + "PhoneNumber", profile.PhoneNumber),
                    new XElement(ns + "TaxAuthority", profile.TaxAuthorityName)),
                new XElement(ns + "Period",
                    new XElement(ns + "Year", snapshot.Year),
                    new XElement(ns + "PeriodType", snapshot.PeriodType.ToString()),
                    new XElement(ns + "PeriodNumber", snapshot.PeriodNumber),
                    new XElement(ns + "FilingDeadline", filingDeadline.ToString("yyyy-MM-dd"))),
                new XElement(ns + "TaxAmounts",
                    new XElement(ns + "TaxableRevenue", snapshot.TaxableRevenueInPeriod),
                    new XElement(ns + "VatRate", snapshot.VatRate),
                    new XElement(ns + "VatAmount", snapshot.VatAmount),
                    new XElement(ns + "PitRate", snapshot.PitRate),
                    new XElement(ns + "PitAmount", snapshot.PitAmount),
                    new XElement(ns + "TotalTaxAmount", snapshot.TotalTaxAmount)),
                new XElement(ns + "Integrity",
                    new XElement(ns + "FinalizedAtUtc", snapshot.FinalizedAtUtc.ToString("O")),
                    new XElement(ns + "FinalizedBy", snapshot.FinalizedBy),
                    new XElement(ns + "AuditSignatureHash", snapshot.AuditSignatureHash)),
                new XElement(ns + "CompatibilityNotice",
                    "XML do MT-GRMS sinh từ snapshot đã chốt; cần đối chiếu XSD của cổng thuế trước khi nộp điện tử.")));

        using var stream = new MemoryStream();
        document.Save(stream);
        var periodCode = snapshot.PeriodType == TaxPeriodType.Quarter
            ? $"Q{snapshot.PeriodNumber}"
            : snapshot.PeriodType == TaxPeriodType.Month ? $"T{snapshot.PeriodNumber}" : "NAM";

        return new TaxDeclarationXmlResult
        {
            Content = stream.ToArray(),
            FileName = $"ToKhai_01_CNKD_{snapshot.Year}_{periodCode}_R{snapshot.RevisionNumber}.xml"
        };
    }

    public async Task<List<AccountingBookEntryDto>> GetBookDetailsAsync(

        int year,

        TaxPeriodType periodType,

        int periodNumber,

        Guid? branchId,

        AccountingBookType? bookType,

        CancellationToken cancellationToken = default)

    {
        RequireOwner();

        var (fromDateUtc, toDateUtc, _) = GetDateRangesForPeriod(year, periodType, periodNumber);

        var annualAccumulatedRevenue = await _repository.GetAnnualAccumulatedRevenueAsync(year, toDateUtc, cancellationToken);

        var (group, _, _) = DetermineHouseholdGroup(annualAccumulatedRevenue);

        var calcMethod = (group == HouseholdTaxGroup.Group3_3BTo50B ||
                          group == HouseholdTaxGroup.Group4_Over50B ||
                          bookType == AccountingBookType.S2b_RevenueDeclaration ||
                          bookType == AccountingBookType.S2c_RevenueAndExpense ||
                          bookType == AccountingBookType.S2d_MaterialsAndGoods ||
                          bookType == AccountingBookType.S2e_CashAndBank ||
                          bookType == AccountingBookType.S3a_TaxObligation)
            ? TaxCalculationMethod.DeclarationBased
            : TaxCalculationMethod.RevenuePercentage;

        return await _repository.GetBookEntriesAsync(

            fromDateUtc,

            toDateUtc,

            branchId,

            group,

            calcMethod,

            bookType,

            cancellationToken);

    }

    public async Task<HouseholdTaxSnapshotDto> FinalizeTaxSnapshotAsync(

        CreateTaxSnapshotRequest request,

        string finalizedBy,

        string? userId = null,

        CancellationToken cancellationToken = default)

    {
        RequireOwner();
        if (_unitOfWork == null)
        {
            return await FinalizeTaxSnapshotCoreAsync(request, finalizedBy, userId, cancellationToken);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            token => FinalizeTaxSnapshotCoreAsync(request, finalizedBy, userId, token),
            cancellationToken,
            IsolationLevel.Serializable);
    }

    private async Task<HouseholdTaxSnapshotDto> FinalizeTaxSnapshotCoreAsync(

        CreateTaxSnapshotRequest request,

        string finalizedBy,

        string? userId = null,

        CancellationToken cancellationToken = default)

    {

        // Chốt kỳ tính thuế luôn ở cấp toàn bộ hộ kinh doanh (Tenant-wide)
        request.BranchId = null;

        // 1. Xác thực bảo mật mã PIN chủ hộ

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BusinessException("USER_ID_REQUIRED", "Không xác định được chủ cửa hàng thực hiện chốt kỳ tính thuế.");
        }

        if (_pinService == null)
        {
            throw new BusinessException("PIN_SERVICE_UNAVAILABLE", "Không thể chốt kỳ khi dịch vụ xác thực lại không khả dụng.");
        }

        if (string.IsNullOrWhiteSpace(request.SecurityPin))
        {
            throw new BusinessException("PIN_REQUIRED", "Yêu cầu mã PIN bảo mật của chủ cửa hàng để chốt kỳ tính thuế.");
        }

        var pinResult = await _pinService.VerifyStoreOwnerPinAsync(userId, request.SecurityPin, cancellationToken);
        if (!pinResult.IsSuccess)
        {
            throw new BusinessException(pinResult.ErrorCode ?? "INVALID_PIN", pinResult.ErrorMessage ?? "Mã PIN xác thực chốt kỳ không chính xác.");
        }

        // Tìm toàn bộ các bản ghi snapshot hiện hành của kỳ này (bao gồm cả tenant-wide và chi nhánh cũ nếu có)
        var existingList = await _repository.GetSnapshotsAsync(
            request.Year,
            request.BranchId,
            cancellationToken);

        var activeSnapshots = existingList
            .Where(s => s.PeriodType == request.PeriodType &&
                        s.PeriodNumber == request.PeriodNumber &&
                        s.Status != TaxSnapshotStatus.Superseded)
            .OrderByDescending(s => s.RevisionNumber)
            .ToList();

        var existing = activeSnapshots.FirstOrDefault();

        if (existing == null)
        {
            // Fallback phòng trường hợp GetSnapshotsAsync có filter ngặt nghèo
            existing = await _repository.GetSnapshotAsync(
                request.Year,
                request.PeriodType,
                request.PeriodNumber,
                request.BranchId,
                cancellationToken);
            if (existing != null)
            {
                activeSnapshots.Add(existing);
            }
        }

        if (existing != null)
        {
            if (!request.AllowSupersedeExisting)
            {
                throw new BusinessException("TAX_PERIOD_LOCKED", $"Kỳ tính thuế {request.PeriodType} {request.PeriodNumber}/{request.Year} đã được chốt và khóa sổ.");
            }

            if (string.IsNullOrWhiteSpace(request.RectificationReason))
            {
                throw new BusinessException("RECTIFICATION_REASON_REQUIRED", "Bắt buộc phải nhập lý do thay thế / số biên bản điều chỉnh khi chốt lại kỳ tính thuế đã khóa.");
            }

            foreach (var activeSnap in activeSnapshots)
            {
                activeSnap.Status = TaxSnapshotStatus.Superseded;
                await _repository.UpdateSnapshotAsync(activeSnap, cancellationToken);
            }
        }

        // Kiểm tra tính tuần tự: Kỳ k-1 cùng năm phải được chốt trước khi chốt kỳ k

        if (request.PeriodType == TaxPeriodType.Quarter && request.PeriodNumber > 1)

        {

            var priorPeriodSnapshot = await _repository.GetSnapshotAsync(

                request.Year,

                request.PeriodType,

                request.PeriodNumber - 1,

                request.BranchId,

                cancellationToken);

            if (priorPeriodSnapshot == null)

            {

                throw new BusinessException("PRIOR_PERIOD_NOT_FINALIZED", 

                    $"Không thể chốt kỳ {request.PeriodType} {request.PeriodNumber}/{request.Year} khi kỳ {request.PeriodNumber - 1}/{request.Year} chưa được chốt và khóa sổ.");

            }

        }

        var filter = new HouseholdTaxFilterDto

        {

            Year = request.Year,

            PeriodType = request.PeriodType,

            PeriodNumber = request.PeriodNumber,

            BranchId = request.BranchId,

            CalculationMethod = request.CalculationMethod,

            Tier2Method = request.Tier2Method,

            ApplyVatReduction2026 = request.ApplyVatReduction2026,

            VatReductionPercentage = request.VatReductionPercentage,

            ReducedVatRevenueInPeriod = request.ReducedVatRevenueInPeriod,

            StandardVatRevenueInPeriod = request.StandardVatRevenueInPeriod,

            CustomVatRate = request.CustomVatRate,

            CustomPitRate = request.CustomPitRate,

            VatOption = request.VatOption

        };

        var summary = await GetTaxSummaryAsync(filter, cancellationToken);

        // Ràng buộc kiểm toán: Chặn chốt kỳ nếu dùng giá vốn ước tính mà thiếu cam kết chịu trách nhiệm của chủ hộ

        if (summary.IsCogsEstimated && !request.ConfirmEstimatedCogsResponsible)

        {

            throw new BusinessException("ESTIMATED_COGS_CONFIRMATION_REQUIRED",

                "Kỳ tính thuế đang sử dụng giá vốn ước tính (do thiếu hóa đơn chứng từ mua hàng). Chủ hộ bắt buộc phải xác nhận cam kết tự chịu trách nhiệm pháp lý trước khi chốt kỳ.");

        }

        var (fromDateUtc, toDateUtc, _) = GetDateRangesForPeriod(request.Year, request.PeriodType, request.PeriodNumber);

        var bookEntries = await _repository.GetBookEntriesAsync(fromDateUtc, toDateUtc, request.BranchId, summary.HouseholdGroup, summary.CalculationMethod, null, cancellationToken);

        // Tính mã băm COGS Calculation Hash

        var cogsHashInput = $"YEAR:{request.Year}_PERIOD:{request.PeriodNumber}_COGS:{summary.CogsAmount:F2}_EST:{summary.IsCogsEstimated}";

        var cogsCalculationHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cogsHashInput))).ToLowerInvariant();

        // Xây dựng BookDataJson có cấu trúc chuẩn gồm metadata kiểm toán và danh mục chứng từ sổ sách

        var auditMetadata = new

        {

            policy = new

            {

                vatResolution = "NQ 204/2025/QH15",

                vatReductionEffectiveTo = "2026-12-31",

                vatRoundingRule = "ROUND_PER_LINE",

                vatOption = summary.VatCalculationOption,

                lossCarryForwardEnabled = false

            },

            taxGroupMethod = new

            {

                householdGroup = (int)summary.HouseholdGroup,

                householdGroupName = summary.HouseholdGroupName,

                calculationMethod = summary.CalculationMethod.ToString(),

                tier2Method = summary.Tier2Method,

                exemptionThreshold = _taxPolicy.ExemptionThreshold

            },

            cogsAudit = new

            {

                method = "PERIODIC_WEIGHTED_AVERAGE",

                isCogsEstimated = summary.IsCogsEstimated,

                cogsCalculationHash = cogsCalculationHash,

                ownerConfirmation = new

                {

                    isConfirmed = summary.IsCogsEstimated ? request.ConfirmEstimatedCogsResponsible : (bool?)null,

                    confirmedBy = summary.IsCogsEstimated ? finalizedBy : null,

                    confirmedAtUtc = summary.IsCogsEstimated ? DateTime.UtcNow : (DateTime?)null,

                    statement = summary.IsCogsEstimated

                        ? (request.EstimatedCogsConfirmationStatement ?? "Tôi cam kết tự chịu trách nhiệm pháp lý về số liệu giá vốn ước tính khi chốt kỳ thuế.")

                        : null

                }

            },

            vatBreakdown = new

            {

                revenueStandardRate10 = summary.VatBreakdown.RevenueStandardRate10,

                taxAmountStandardRate10 = summary.VatBreakdown.TaxAmountStandardRate10,

                revenueReducedRate08 = summary.VatBreakdown.RevenueReducedRate08,

                taxAmountReducedRate08 = summary.VatBreakdown.TaxAmountReducedRate08,

                effectiveVatRate = summary.VatRate,

                vatRateSnapshotSource = "Snapshot from OrderDetail.AppliedVatRate at point of sale with product flag fallback"

            }

        };

        var fullBookData = new

        {

            metadata = auditMetadata,

            bookEntries = bookEntries

        };

                var jsonOptions = new JsonSerializerOptions

        {

            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            WriteIndented = false

        };

        var bookDataJson = JsonSerializer.Serialize(fullBookData, jsonOptions);

        var rules = _taxPolicy;
        var (_, existingLockStart) = await GetRequiredPitMethodAsync(request.Year, cancellationToken);
        TaxMethodLock? methodLock = null;
        if (!summary.IsTaxExempt)
        {
            methodLock = summary.Is3BCrossedMidYear && summary.CalculationMethod == TaxCalculationMethod.RevenuePercentage
                ? new TaxMethodLock(request.Year + 1, request.Year + rules.MethodLockYears,
                    TaxCalculationMethod.DeclarationBased, "REVENUE_THRESHOLD_CROSSED")
                : new TaxMethodLock(existingLockStart ?? request.Year,
                    summary.CalculationMethod == TaxCalculationMethod.DeclarationBased
                        ? (existingLockStart ?? request.Year) + rules.MethodLockYears - 1
                        : request.Year,
                    summary.CalculationMethod, "FINALIZED_METHOD");
        }

        var snapshot = new HouseholdTaxSnapshot

        {

            TenantId = _tenantContext?.CurrentTenantId ?? string.Empty,

            BranchId = request.BranchId,

            PeriodType = request.PeriodType,

            Year = request.Year,

            PeriodNumber = request.PeriodNumber,

            AnnualAccumulatedRevenue = summary.AnnualAccumulatedRevenue,

            HouseholdGroup = summary.HouseholdGroup,

            IsTaxExempt = summary.IsTaxExempt,

            AppliedPitMethod = summary.IsTaxExempt ? null : summary.CalculationMethod,

            TaxableRevenueInPeriod = summary.TaxableRevenueInPeriod,

            VatRate = summary.VatRate, // Effective VAT rate

            VatAmount = summary.VatAmount,

            PitRate = summary.PitRate,

            PitAmount = summary.PitAmount,

            TotalTaxAmount = summary.TotalTaxAmount,

            BookDataJson = bookDataJson,

            TaxPolicyJson = JsonSerializer.Serialize(new TaxSnapshotPolicy(rules, methodLock)),

            FinalizedBy = finalizedBy,
            FinalizedAtUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, DateTime.UtcNow.Second, DateTimeKind.Utc),
            Notes = request.Notes,
            Status = TaxSnapshotStatus.Finalized,
            RevisionNumber = existing != null ? existing.RevisionNumber + 1 : 0,
            SupersededSnapshotId = existing?.Id,
            RectificationReason = existing != null ? request.RectificationReason : null
        };

        // Ký số HMAC-SHA256 trên Canonical JSON snapshot (Bao gồm cả mã băm BookDataHash)
        snapshot.AuditSignatureHash = _signatureService.ComputeSignature(snapshot);
        var created = await _repository.AddSnapshotAsync(snapshot, cancellationToken);
        if (_notificationOutbox != null && _unitOfWork != null)
        {
            await _notificationOutbox.EnqueueAsync(created.TenantId, created.Id, new BusinessNotification(
                BusinessNotificationTypes.TaxSnapshotFinalized, "Đã chốt kỳ tính thuế",
                $"Kỳ {created.PeriodNumber}/{created.Year}, phiên bản {created.RevisionNumber}.",
                $"/store-owner/tax-snapshots?snapshotId={created.Id}"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new HouseholdTaxSnapshotDto

        {

            Id = created.Id,

            BranchId = created.BranchId,

            PeriodType = created.PeriodType,

            Year = created.Year,

            PeriodNumber = created.PeriodNumber,

            AnnualAccumulatedRevenue = created.AnnualAccumulatedRevenue,

            HouseholdGroup = created.HouseholdGroup,

            CalculationMethod = summary.CalculationMethod,

            AppliedPitMethod = created.AppliedPitMethod,

            IsTaxExempt = created.IsTaxExempt,

            TaxableRevenueInPeriod = created.TaxableRevenueInPeriod,

            VatRate = created.VatRate,

            VatAmount = created.VatAmount,

            PitRate = created.PitRate,

            PitAmount = created.PitAmount,

            TotalTaxAmount = created.TotalTaxAmount,

            VatBreakdown = summary.VatBreakdown,

            IsCogsEstimated = summary.IsCogsEstimated,

            AuditSignatureHash = created.AuditSignatureHash,
            TaxPolicyJson = created.TaxPolicyJson,

            IsIntegrityValid = true,

            FinalizedBy = created.FinalizedBy,
            FinalizedAtUtc = created.FinalizedAtUtc,
            Notes = created.Notes,
            Status = created.Status,
            RevisionNumber = created.RevisionNumber,
            SupersededSnapshotId = created.SupersededSnapshotId,
            RectificationReason = created.RectificationReason
        };

    }

    public async Task<HouseholdTaxSnapshotDto?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default)

    {
        RequireOwner();

        var s = await _repository.GetSnapshotByIdAsync(id, cancellationToken);

        if (s == null) return null;

        return new HouseholdTaxSnapshotDto

        {

            Id = s.Id,

            BranchId = s.BranchId,

            PeriodType = s.PeriodType,

            Year = s.Year,

            PeriodNumber = s.PeriodNumber,

            AnnualAccumulatedRevenue = s.AnnualAccumulatedRevenue,

            HouseholdGroup = s.HouseholdGroup,

            AppliedPitMethod = s.AppliedPitMethod,

            CalculationMethod = s.AppliedPitMethod ?? (s.HouseholdGroup == HouseholdTaxGroup.Group1_Under1B

                ? TaxCalculationMethod.Exempt

                : (s.HouseholdGroup == HouseholdTaxGroup.Group3_3BTo50B || s.HouseholdGroup == HouseholdTaxGroup.Group4_Over50B

                    ? TaxCalculationMethod.DeclarationBased

                    : TaxCalculationMethod.RevenuePercentage)),

            IsTaxExempt = s.IsTaxExempt,

            TaxableRevenueInPeriod = s.TaxableRevenueInPeriod,

            VatRate = s.VatRate,

            VatAmount = s.VatAmount,

            PitRate = s.PitRate,

            PitAmount = s.PitAmount,

            TotalTaxAmount = s.TotalTaxAmount,

            AuditSignatureHash = s.AuditSignatureHash,
            TaxPolicyJson = s.TaxPolicyJson,
            IsIntegrityValid = _signatureService.VerifySignature(s),
            FinalizedBy = s.FinalizedBy,
            FinalizedAtUtc = s.FinalizedAtUtc,
            Notes = s.Notes,
            Status = s.Status,
            RevisionNumber = s.RevisionNumber,
            SupersededSnapshotId = s.SupersededSnapshotId,
            RectificationReason = s.RectificationReason
        };

    }

    public async Task<bool> VerifySnapshotIntegrityAsync(Guid snapshotId, CancellationToken cancellationToken = default)

    {
        RequireOwner();

        var s = await _repository.GetSnapshotByIdAsync(snapshotId, cancellationToken);

        if (s == null) return false;

        return _signatureService.VerifySignature(s);

    }

    public async Task<List<HouseholdTaxSnapshotDto>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default)

    {
        RequireOwner();

        var snapshots = await _repository.GetSnapshotsAsync(year, branchId, cancellationToken);

        return snapshots.Select(s => new HouseholdTaxSnapshotDto

        {

            Id = s.Id,

            BranchId = s.BranchId,

            PeriodType = s.PeriodType,

            Year = s.Year,

            PeriodNumber = s.PeriodNumber,

            AnnualAccumulatedRevenue = s.AnnualAccumulatedRevenue,

            HouseholdGroup = s.HouseholdGroup,

            AppliedPitMethod = s.AppliedPitMethod,

            CalculationMethod = s.AppliedPitMethod ?? (s.HouseholdGroup == HouseholdTaxGroup.Group1_Under1B

                ? TaxCalculationMethod.Exempt

                : (s.HouseholdGroup == HouseholdTaxGroup.Group3_3BTo50B || s.HouseholdGroup == HouseholdTaxGroup.Group4_Over50B

                    ? TaxCalculationMethod.DeclarationBased

                    : TaxCalculationMethod.RevenuePercentage)),

            IsTaxExempt = s.IsTaxExempt,

            TaxableRevenueInPeriod = s.TaxableRevenueInPeriod,

            VatRate = s.VatRate,

            VatAmount = s.VatAmount,

            PitRate = s.PitRate,

            PitAmount = s.PitAmount,

            TotalTaxAmount = s.TotalTaxAmount,

            AuditSignatureHash = s.AuditSignatureHash,
            TaxPolicyJson = s.TaxPolicyJson,
            IsIntegrityValid = _signatureService.VerifySignature(s),
            FinalizedBy = s.FinalizedBy,
            FinalizedAtUtc = s.FinalizedAtUtc,
            Notes = s.Notes,
            Status = s.Status,
            RevisionNumber = s.RevisionNumber,
            SupersededSnapshotId = s.SupersededSnapshotId,
            RectificationReason = s.RectificationReason
        }).ToList();

    }

    public static (DateTime FromDateUtc, DateTime ToDateUtc, DateTime? PriorPeriodToDateUtc) GetDateRangesForPeriod(int year, TaxPeriodType periodType, int periodNumber)

    {

        var vnTz = GetVietnamTimeZone();

        DateTime fromLocal;

        DateTime toLocal;

        DateTime? priorToLocal = null;

        switch (periodType)

        {

            case TaxPeriodType.Month:
                fromLocal = new DateTime(year, periodNumber, 1, 0, 0, 0, DateTimeKind.Unspecified);
                toLocal = fromLocal.AddMonths(1);
                if (periodNumber > 1)
                {
                    priorToLocal = fromLocal;
                }
                break;

            case TaxPeriodType.Quarter:
                switch (periodNumber)
                {
                    case 1:
                        fromLocal = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        priorToLocal = null;
                        break;
                    case 2:
                        fromLocal = new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        priorToLocal = new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        break;
                    case 3:
                        fromLocal = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        priorToLocal = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        break;
                    case 4:
                    default:
                        fromLocal = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        priorToLocal = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        break;
                }
                break;

            case TaxPeriodType.Year:
            default:
                fromLocal = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                toLocal = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                priorToLocal = null;
                break;
        }

        var fromDateUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal, vnTz);
        var toDateUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal, vnTz);
        var priorPeriodToDateUtc = priorToLocal.HasValue ? TimeZoneInfo.ConvertTimeToUtc(priorToLocal.Value, vnTz) : (DateTime?)null;

        return (fromDateUtc, toDateUtc, priorPeriodToDateUtc);

    }

    private (HouseholdTaxGroup Group, string GroupName, bool IsExempt) DetermineHouseholdGroup(decimal annualAccumulatedRevenue)

    {

        if (annualAccumulatedRevenue <= _taxPolicy.ExemptionThreshold)

        {

            return (

                HouseholdTaxGroup.Group1_Under1B,

                "Nhóm 1 (Doanh thu năm <= 1 tỷ đồng - Miễn thuế)",

                true

            );

        }

        if (annualAccumulatedRevenue <= _taxPolicy.MethodSwitchRevenueThreshold)

        {

            return (

                HouseholdTaxGroup.Group2_1BTo3B,

                "Nhóm 2 (Doanh thu năm từ 1 tỷ - 3 tỷ đồng - Thuế tỷ lệ)",

                false

            );

        }

        if (annualAccumulatedRevenue <= 50_000_000_000m)

        {

            return (

                HouseholdTaxGroup.Group3_3BTo50B,

                "Nhóm 3 (Doanh thu năm từ 3 tỷ - 50 tỷ đồng - Tỷ lệ hoặc Kê khai)",

                false

            );

        }

        return (

            HouseholdTaxGroup.Group4_Over50B,

            "Nhóm 4 (Doanh thu năm trên 50 tỷ đồng - Bắt buộc Kê khai)",

            false

        );

    }

    public async Task ChangeSecurityPinAsync(string userId, ChangeSecurityPinRequest request, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        if (_pinService == null)
        {
            throw new BusinessException("PIN_SERVICE_UNAVAILABLE", "Dịch vụ xác thực mã PIN không khả dụng.");
        }

        await _pinService.ChangeSecurityPinAsync(userId, request, cancellationToken);
    }

    public async Task ResetSecurityPinAsync(string userId, ResetSecurityPinRequest request, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        if (_pinService == null)
        {
            throw new BusinessException("PIN_SERVICE_UNAVAILABLE", "Dịch vụ xác thực mã PIN không khả dụng.");
        }

        await _pinService.ResetSecurityPinAsync(userId, request, cancellationToken);
    }

    public static TimeZoneInfo GetVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("VN_TZ", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
            }
        }
    }

    private void RequireOwner() => Common.TenantRoleGuard.RequireStoreOwner(_tenantContext, "thuế hộ kinh doanh");

}
