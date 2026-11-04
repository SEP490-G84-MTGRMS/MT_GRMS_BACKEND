using Microsoft.Extensions.Configuration;
﻿using System.Security.Cryptography;

using System.Text;

using System.Text.Json;

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
    private readonly IConfiguration? _configuration;

    public const decimal TaxExemptionThreshold = RevenuePercentageTaxStrategy.TaxExemptionThreshold;

    public const decimal RetailVatRate = RevenuePercentageTaxStrategy.RetailVatRate;

    public const decimal RetailPitRate = RevenuePercentageTaxStrategy.RetailPitRate;

    public const decimal DeclarationDefaultPitRate = DeclarationBasedTaxStrategy.DeclarationDefaultPitRate;

    public HouseholdTaxService(IHouseholdTaxRepository repository)

        : this(repository, new TaxCalculationStrategyFactory(new ITaxCalculationStrategy[]

        {

            new RevenuePercentageTaxStrategy(),

            new DeclarationBasedTaxStrategy()

        }), null, null)

    {

    }

    public HouseholdTaxService(
        IHouseholdTaxRepository repository,
        ITaxCalculationStrategyFactory strategyFactory,
        Security.IPinVerificationService? pinService = null,
        Security.ITaxSnapshotSignatureService? signatureService = null,
        IConfiguration? configuration = null)
    {
        _repository = repository;
        _strategyFactory = strategyFactory;
        _pinService = pinService;
        _signatureService = signatureService ?? new Security.TaxSnapshotSignatureService();
        _configuration = configuration;
    }

    private DateTime GetVatReductionDeadlineUtc()
    {
        var configVal = _configuration?["TaxSettings:VatReductionDeadlineUtc"];
        if (!string.IsNullOrWhiteSpace(configVal) && DateTime.TryParse(configVal, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDate))
        {
            return DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var vnTz = GetVietnamTimeZone();
        // Mốc chuyển giao hết hạn giảm thuế GTGT theo giờ VN: 01/01/2027 00:00:00 VN = 31/12/2026 17:00:00 UTC
        return TimeZoneInfo.ConvertTimeToUtc(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), vnTz);
    }

    private async Task<(TaxCalculationMethod? Method, int? StartYear)> GetRequiredPitMethodAsync(int year, CancellationToken ct)
    {
        var thisYear = await _repository.GetYearSettingAsync(year, ct);
        if (thisYear != null && thisYear.LockedUntilYear >= year)
            return (thisYear.PitMethod, thisYear.Year);

        var prevYear = await _repository.GetYearSettingAsync(year - 1, ct);
        if (prevYear != null && prevYear.LockedUntilYear >= year)
            return (prevYear.PitMethod, prevYear.Year);

        // Fallback tự động khi chưa có bản ghi setting trong DB (chủ hộ chưa từng bấm chốt kỳ)
        // Duyệt tuần tự các năm trước để xác định chính xác chu kỳ ổn định 2 năm bắt đầu từ đâu
        if (year > 2020)
        {
            int historyStartYear = Math.Max(2024, year - 5);
            int? activeCycleStartYear = null;
            int? activeCycleEndYear = null;

            for (int y = historyStartYear; y < year; y++)
            {
                var settingY = await _repository.GetYearSettingAsync(y, ct);
                if (settingY != null && settingY.PitMethod == TaxCalculationMethod.DeclarationBased)
                {
                    activeCycleStartYear = settingY.Year;
                    activeCycleEndYear = settingY.LockedUntilYear;
                    continue;
                }

                var (_, yToDateUtc, _) = GetDateRangesForPeriod(y, TaxPeriodType.Year, 1);
                var yRevenue = await _repository.GetAnnualAccumulatedRevenueAsync(y, yToDateUtc, ct);

                bool isCurrentlyInCycle = activeCycleEndYear.HasValue && y <= activeCycleEndYear.Value;

                if (!isCurrentlyInCycle)
                {
                    if (yRevenue > 3_000_000_000m)
                    {
                        activeCycleStartYear = y + 1;
                        activeCycleEndYear = y + 2;
                    }
                }
                else
                {
                    // Đang trong chu kỳ: Chỉ khi ở năm cuối của chu kỳ (y == activeCycleEndYear) và doanh thu năm y tiếp tục > 3B
                    // mới kích hoạt chu kỳ 2 năm tiếp theo. Nếu năm y <= 3B, chu kỳ kết thúc đúng hạn.
                    if (activeCycleEndYear.HasValue && y == activeCycleEndYear.Value && yRevenue > 3_000_000_000m)
                    {
                        activeCycleStartYear = y + 1;
                        activeCycleEndYear = y + 2;
                    }
                }
            }

            if (activeCycleEndYear.HasValue && year <= activeCycleEndYear.Value)
            {
                return (TaxCalculationMethod.DeclarationBased, activeCycleStartYear);
            }
        }

        return (null, null);
    }

    public async Task<HouseholdTaxSummaryDto> GetTaxSummaryAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default)

    {

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

        var isGrossOver1BNetUnder1B = annualGross > TaxExemptionThreshold && annualAccumulatedRevenue <= TaxExemptionThreshold;
        var isGrossOver3BNetUnder3B = annualGross > 3_000_000_000m && annualAccumulatedRevenue <= 3_000_000_000m;
        string? netVsGrossWarning = null;
        decimal? grossBasedEstimatedTax = null;

        // Cảnh báo tiến trình ngưỡng Nhóm 4 (40 tỷ = 80%, 50 tỷ = 100%)
        var isApproachingGroup4 = annualAccumulatedRevenue >= 40_000_000_000m && annualAccumulatedRevenue < 50_000_000_000m;
        var isExceedingGroup4 = annualAccumulatedRevenue >= 50_000_000_000m;

        // Cảnh báo tiến trình ngưỡng 3 tỷ VNĐ (Điểm d Khoản 5 Điều 4 Nghị định 68/2026/NĐ-CP)
        var isApproaching3B = annualAccumulatedRevenue >= 2_700_000_000m && annualAccumulatedRevenue < 3_000_000_000m;
        var isExceeding3B = annualAccumulatedRevenue > 3_000_000_000m;
        var is3BCrossedMidYear = priorPeriodsRevenue <= 3_000_000_000m && annualAccumulatedRevenue > 3_000_000_000m;

        // Cảnh báo vượt ngưỡng miễn thuế 1 tỷ VNĐ giữa năm (từ <= 1 tỷ lên > 1 tỷ)
        var isThresholdCrossedMidYear = priorPeriodsRevenue <= TaxExemptionThreshold && annualAccumulatedRevenue > TaxExemptionThreshold;

        string? thresholdExceededNote = null;

        if (isThresholdCrossedMidYear)
        {
            // Tính số ước tính GTGT nếu cơ quan thuế truy thu trên toàn bộ doanh thu lũy kế cả năm (để chủ hộ dự phòng)
            var retrospectiveVatEstimate = Math.Round(annualAccumulatedRevenue * 0.01m, 0, MidpointRounding.AwayFromZero);
            thresholdExceededNote = $"Doanh thu lũy kế năm đã vượt ngưỡng 1 tỷ VNĐ trong kỳ này (từ {priorPeriodsRevenue:N0}đ lên {annualAccumulatedRevenue:N0}đ). Theo quy định, hộ kinh doanh bắt đầu khai và nộp thuế kể từ quý phát sinh doanh thu vượt (Điều 10 NĐ 68/2026/NĐ-CP). Thuế TNCN tính trên phần vượt ngưỡng ({Math.Max(0, annualAccumulatedRevenue - TaxExemptionThreshold):N0}đ). Về thuế GTGT: Hệ thống đang ước tính trên doanh thu phát sinh trong kỳ; trường hợp cơ quan thuế yêu cầu nộp GTGT trên toàn bộ doanh thu cả năm, số thuế GTGT dự kiến khoảng {retrospectiveVatEstimate:N0}đ. Khuyến nghị chủ hộ tham vấn cơ quan thuế địa bàn để xác định chính xác phương án kê khai.";
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
                bool crossedMidYear = priorPeriodsRevenue <= 3_000_000_000m;
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

        var nextYearSetting = await _repository.GetYearSettingAsync(filter.Year + 1, cancellationToken);

        declarationMethodStartYear ??= nextYearSetting?.PitMethod == TaxCalculationMethod.DeclarationBased

            ? nextYearSetting.Year

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

                    .Where(s => s.Year == filter.Year && s.PeriodType == filter.PeriodType && s.PeriodNumber < filter.PeriodNumber)

                    .Sum(s => s.PitAmount);

            }

            if (filter.PriorDeclaredVatTaxOverride.HasValue)

            {

                priorDeclaredVatTax = filter.PriorDeclaredVatTaxOverride.Value;

            }

            else

            {

                var priorSnapshots = await _repository.GetSnapshotsAsync(filter.Year, null, cancellationToken);

                priorDeclaredVatTax = priorSnapshots

                    .Where(s => s.Year == filter.Year && s.PeriodType == filter.PeriodType && s.PeriodNumber < filter.PeriodNumber)

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
            thresholdExceededNote = $"Doanh thu lũy kế năm đã vượt ngưỡng 1 tỷ VNĐ trong kỳ này (từ {priorPeriodsRevenue:N0}đ lên {annualAccumulatedRevenue:N0}đ). Theo quy định, hộ kinh doanh bắt đầu khai và nộp thuế kể từ quý phát sinh vượt (Điều 10 NĐ 68/2026/NĐ-CP). Thuế TNCN tính trên phần vượt ngưỡng ({Math.Max(0, annualAccumulatedRevenue - TaxExemptionThreshold):N0}đ). Về thuế GTGT: Mặc định áp dụng phương án thận trọng tính trên toàn bộ doanh thu lũy kế cả năm ({retrospectiveVatAmount:N0}đ, đã tách nhóm 0.8% và 1.0%); trường hợp cơ quan thuế chỉ yêu cầu nộp trên doanh thu phát sinh trong quý vượt, số thuế GTGT dự kiến là {quarterlyVatAmount:N0}đ. Khuyến nghị chủ hộ tham vấn cơ quan thuế địa bàn để xác định chính xác phương án kê khai.";
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

        var booksSummary = new TaxBooksSummaryDto

        {

            PrimaryBookCode = primaryBook,

            TotalRecordedRevenue = taxableRevenueInPeriod,

            TotalRecordedExpenses = deductibleExpenses,

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
            var grossEstPit = Math.Round(Math.Max(0m, annualGross - TaxExemptionThreshold) * 0.005m, 0, MidpointRounding.AwayFromZero);
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

            AnnualAccumulatedRevenue = annualAccumulatedRevenue,

            PriorPeriodsAccumulatedRevenue = priorPeriodsRevenue,

            HouseholdGroup = group,

            HouseholdGroupName = groupName,

            CalculationMethod = calculationMethod,

            Tier2Method = tier2Method,

            IsTaxExempt = isExempt,

            TaxExemptionThreshold = TaxExemptionThreshold,

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

    public async Task<List<AccountingBookEntryDto>> GetBookDetailsAsync(

        int year,

        TaxPeriodType periodType,

        int periodNumber,

        Guid? branchId,

        AccountingBookType? bookType,

        CancellationToken cancellationToken = default)

    {

        var (fromDateUtc, toDateUtc, _) = GetDateRangesForPeriod(year, periodType, periodNumber);

        var annualAccumulatedRevenue = await _repository.GetAnnualAccumulatedRevenueAsync(year, toDateUtc, cancellationToken);

        var (group, _, _) = DetermineHouseholdGroup(annualAccumulatedRevenue);

        return await _repository.GetBookEntriesAsync(

            fromDateUtc,

            toDateUtc,

            branchId,

            group,

            TaxCalculationMethod.RevenuePercentage,

            bookType,

            cancellationToken);

    }

    public async Task<HouseholdTaxSnapshotDto> FinalizeTaxSnapshotAsync(

        CreateTaxSnapshotRequest request,

        string finalizedBy,

        string? userId = null,

        CancellationToken cancellationToken = default)

    {

        // 1. Xác thực bảo mật mã PIN chủ hộ

        if (_pinService != null && !string.IsNullOrEmpty(userId))

        {

            if (string.IsNullOrWhiteSpace(request.SecurityPin))

            {

                throw new BusinessException("PIN_REQUIRED", "Yêu cầu mã PIN bảo mật của chủ hộ để chốt kỳ tính thuế.");

            }

            var pinResult = await _pinService.VerifyStoreOwnerPinAsync(userId, request.SecurityPin, cancellationToken);

            if (!pinResult.IsSuccess)

            {

                throw new BusinessException(pinResult.ErrorCode ?? "INVALID_PIN", pinResult.ErrorMessage ?? "Mã PIN xác thực chốt kỳ không chính xác.");

            }

        }

        var existing = await _repository.GetSnapshotAsync(

            request.Year,

            request.PeriodType,

            request.PeriodNumber,

            request.BranchId,

            cancellationToken);

        if (existing != null)

        {

            throw new BusinessException("TAX_PERIOD_LOCKED", $"Kỳ tính thuế {request.PeriodType} {request.PeriodNumber}/{request.Year} đã được chốt và khóa sổ.");

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

                exemptionThreshold = TaxExemptionThreshold

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

        var snapshot = new HouseholdTaxSnapshot

        {

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

            FinalizedBy = finalizedBy,

            FinalizedAtUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, DateTime.UtcNow.Second, DateTimeKind.Utc),

            Notes = request.Notes

        };

        // Ký số HMAC-SHA256 trên Canonical JSON snapshot (Bao gồm cả mã băm BookDataHash)

        snapshot.AuditSignatureHash = _signatureService.ComputeSignature(snapshot);

        var created = await _repository.AddSnapshotAsync(snapshot, cancellationToken);

        // 1. Khoá phương pháp năm hiện tại nếu chưa có và không miễn thuế
        if (!summary.IsTaxExempt)
        {
            var existingCurrentYear = await _repository.GetYearSettingAsync(request.Year, cancellationToken);
            if (existingCurrentYear == null || existingCurrentYear.Year != request.Year)
            {
                // Nếu Nhóm 2 tự nguyện chọn Kê khai -> Khóa ổn định 2 năm liên tiếp theo Điểm d Khoản 5 Điều 4 NĐ 68
                var isVoluntaryDeclaration = summary.HouseholdGroup == HouseholdTaxGroup.Group2_1BTo3B && summary.CalculationMethod == TaxCalculationMethod.DeclarationBased;
                var lockedUntil = isVoluntaryDeclaration ? request.Year + 1 : request.Year;
                var lockReason = isVoluntaryDeclaration ? "VOLUNTARY_DECLARATION_GROUP2" : "FIRST_FINALIZED_PERIOD";

                await _repository.UpsertYearSettingAsync(new HouseholdTaxYearSetting
                {
                    Year = request.Year,
                    PitMethod = summary.CalculationMethod,
                    LockedUntilYear = lockedUntil,
                    LockReason = lockReason,
                    LockedBySystem = finalizedBy
                }, cancellationToken);
            }
        }

        // 2. Nếu vượt 3 tỷ giữa năm -> khoá phương pháp kê khai cho năm sau (ổn định 2 năm theo Điểm d Khoản 5 Điều 4 NĐ 68/2026/NĐ-CP)
        if (summary.Is3BCrossedMidYear && summary.CalculationMethod == TaxCalculationMethod.RevenuePercentage)
        {
            var nextYear = request.Year + 1;
            var existingNextYear = await _repository.GetYearSettingAsync(nextYear, cancellationToken);
            if (existingNextYear == null)
            {
                await _repository.UpsertYearSettingAsync(new HouseholdTaxYearSetting
                {
                    Year = nextYear,
                    PitMethod = TaxCalculationMethod.DeclarationBased,
                    LockedUntilYear = nextYear + 1,
                    LockReason = "REVENUE_EXCEEDED_3B",
                    LockedBySystem = "AUTO"
                }, cancellationToken);
            }
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

            IsIntegrityValid = true,

            FinalizedBy = created.FinalizedBy,

            FinalizedAtUtc = created.FinalizedAtUtc,

            Notes = created.Notes

        };

    }

    public async Task<HouseholdTaxSnapshotDto?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default)

    {

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

            CalculationMethod = s.HouseholdGroup == HouseholdTaxGroup.Group1_Under1B

                ? TaxCalculationMethod.Exempt

                : (s.HouseholdGroup == HouseholdTaxGroup.Group3_3BTo50B || s.HouseholdGroup == HouseholdTaxGroup.Group4_Over50B

                    ? TaxCalculationMethod.DeclarationBased

                    : TaxCalculationMethod.RevenuePercentage),

            IsTaxExempt = s.IsTaxExempt,

            TaxableRevenueInPeriod = s.TaxableRevenueInPeriod,

            VatRate = s.VatRate,

            VatAmount = s.VatAmount,

            PitRate = s.PitRate,

            PitAmount = s.PitAmount,

            TotalTaxAmount = s.TotalTaxAmount,

            AuditSignatureHash = s.AuditSignatureHash,

            IsIntegrityValid = _signatureService.VerifySignature(s),

            FinalizedBy = s.FinalizedBy,

            FinalizedAtUtc = s.FinalizedAtUtc,

            Notes = s.Notes

        };

    }

    public async Task<bool> VerifySnapshotIntegrityAsync(Guid snapshotId, CancellationToken cancellationToken = default)

    {

        var s = await _repository.GetSnapshotByIdAsync(snapshotId, cancellationToken);

        if (s == null) return false;

        return _signatureService.VerifySignature(s);

    }

    public async Task<List<HouseholdTaxSnapshotDto>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default)

    {

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

            CalculationMethod = s.HouseholdGroup == HouseholdTaxGroup.Group1_Under1B

                ? TaxCalculationMethod.Exempt

                : (s.HouseholdGroup == HouseholdTaxGroup.Group3_3BTo50B || s.HouseholdGroup == HouseholdTaxGroup.Group4_Over50B

                    ? TaxCalculationMethod.DeclarationBased

                    : TaxCalculationMethod.RevenuePercentage),

            IsTaxExempt = s.IsTaxExempt,

            TaxableRevenueInPeriod = s.TaxableRevenueInPeriod,

            VatRate = s.VatRate,

            VatAmount = s.VatAmount,

            PitRate = s.PitRate,

            PitAmount = s.PitAmount,

            TotalTaxAmount = s.TotalTaxAmount,

            AuditSignatureHash = s.AuditSignatureHash,

            IsIntegrityValid = _signatureService.VerifySignature(s),

            FinalizedBy = s.FinalizedBy,

            FinalizedAtUtc = s.FinalizedAtUtc,

            Notes = s.Notes

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

    private static (HouseholdTaxGroup Group, string GroupName, bool IsExempt) DetermineHouseholdGroup(decimal annualAccumulatedRevenue)

    {

        if (annualAccumulatedRevenue <= TaxExemptionThreshold)

        {

            return (

                HouseholdTaxGroup.Group1_Under1B,

                "Nhóm 1 (Doanh thu năm <= 1 tỷ đồng - Miễn thuế)",

                true

            );

        }

        if (annualAccumulatedRevenue <= 3_000_000_000m)

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

}