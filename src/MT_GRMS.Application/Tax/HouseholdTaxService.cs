using System.Security.Cryptography;
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
        Security.ITaxSnapshotSignatureService? signatureService = null)
    {
        _repository = repository;
        _strategyFactory = strategyFactory;
        _pinService = pinService;
        _signatureService = signatureService ?? new Security.TaxSnapshotSignatureService();
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

        // Cảnh báo tiến trình ngưỡng Nhóm 4 (40 tỷ = 80%, 50 tỷ = 100%)
        var isApproachingGroup4 = annualAccumulatedRevenue >= 40_000_000_000m && annualAccumulatedRevenue < 50_000_000_000m;
        var isExceedingGroup4 = annualAccumulatedRevenue >= 50_000_000_000m;

        // Cảnh báo vượt ngưỡng miễn thuế 1 tỷ VNĐ giữa năm (từ <= 1 tỷ lên > 1 tỷ)
        var isThresholdCrossedMidYear = priorPeriodsRevenue <= TaxExemptionThreshold && annualAccumulatedRevenue > TaxExemptionThreshold;
        string? thresholdExceededNote = isThresholdCrossedMidYear
            ? $"Doanh thu lũy kế năm đã vượt ngưỡng 1 tỷ VNĐ trong kỳ này (từ {priorPeriodsRevenue:N0}đ lên {annualAccumulatedRevenue:N0}đ). Hệ thống bắt đầu tính nghĩa vụ thuế GTGT và TNCN theo thuế suất ngành nghề đăng ký trên phần doanh thu vượt ngưỡng. Khuyến nghị chủ hộ tham vấn cơ quan thuế địa bàn về quy định kê khai và chính sách áp dụng cho trường hợp vượt ngưỡng giữa năm."
            : null;

        // 3. Doanh thu phát sinh trong kỳ
        var (gross, discount, returns) = await _repository.GetPeriodRevenueAsync(
            fromDateUtc,
            toDateUtc,
            filter.BranchId,
            cancellationToken);

        var taxableRevenueInPeriod = Math.Max(0, gross - discount - returns);

        // 4. Xác định phương pháp tính thuế
        // Nhóm 2: Hỗ trợ 2 phương pháp (Cách 1: Doanh thu vượt 1 tỷ * 0.5%, Cách 2: 15% lợi nhuận khi chọn PROFIT_15)
        bool isDeclaration;
        if (group == HouseholdTaxGroup.Group2_1BTo3B)
        {
            isDeclaration = filter.Tier2Method == "PROFIT_15" || filter.CalculationMethod == TaxCalculationMethod.DeclarationBased;
        }
        else
        {
            isDeclaration = filter.CalculationMethod == TaxCalculationMethod.DeclarationBased 
                || group == HouseholdTaxGroup.Group3_3BTo50B 
                || group == HouseholdTaxGroup.Group4_Over50B;
        }

        var calculationMethod = isDeclaration 
            ? TaxCalculationMethod.DeclarationBased 
            : TaxCalculationMethod.RevenuePercentage;

        var tier2Method = group == HouseholdTaxGroup.Group2_1BTo3B
            ? (isDeclaration ? "PROFIT_15" : "REVENUE_EXCESS")
            : "N/A";

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
        }

        // 6. Tính toán thuế qua Strategy
        var calculationContext = new TaxCalculationContext
        {
            Group = group,
            IsExempt = isExempt,
            AnnualAccumulatedRevenue = annualAccumulatedRevenue,
            TaxableRevenueInPeriod = taxableRevenueInPeriod,
            ReducedVatRevenueInPeriod = filter.ReducedVatRevenueInPeriod,
            StandardVatRevenueInPeriod = filter.StandardVatRevenueInPeriod,
            CustomVatRate = filter.CustomVatRate,
            ApplyVatReduction2026 = filter.ApplyVatReduction2026,
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
            VatAmount = calcResult.VatAmount,
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
            TotalTaxAmount = calcResult.TotalTaxAmount,
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
            CustomPitRate = request.CustomPitRate
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
                effectiveVatRate = summary.VatRate
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

    private static (DateTime FromDateUtc, DateTime ToDateUtc, DateTime? PriorPeriodToDateUtc) GetDateRangesForPeriod(int year, TaxPeriodType periodType, int periodNumber)
    {
        var vnTz = GetVietnamTimeZone();
        DateTime fromLocal;
        DateTime toLocal;
        DateTime? priorToLocal = null;

        switch (periodType)
        {
            case TaxPeriodType.Month:
                fromLocal = new DateTime(year, periodNumber, 1, 0, 0, 0, DateTimeKind.Unspecified);
                toLocal = new DateTime(year, periodNumber, DateTime.DaysInMonth(year, periodNumber), 23, 59, 59, 999, DateTimeKind.Unspecified);
                if (periodNumber > 1)
                {
                    priorToLocal = new DateTime(year, periodNumber - 1, DateTime.DaysInMonth(year, periodNumber - 1), 23, 59, 59, 999, DateTimeKind.Unspecified);
                }
                break;

            case TaxPeriodType.Quarter:
                switch (periodNumber)
                {
                    case 1:
                        fromLocal = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        priorToLocal = null;
                        break;
                    case 2:
                        fromLocal = new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 6, 30, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        priorToLocal = new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        break;
                    case 3:
                        fromLocal = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 9, 30, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        priorToLocal = new DateTime(year, 6, 30, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        break;
                    case 4:
                    default:
                        fromLocal = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Unspecified);
                        toLocal = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        priorToLocal = new DateTime(year, 9, 30, 23, 59, 59, 999, DateTimeKind.Unspecified);
                        break;
                }
                break;

            case TaxPeriodType.Year:
            default:
                fromLocal = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                toLocal = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);
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

    private static TimeZoneInfo GetVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }
}