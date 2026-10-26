using System.Text.Json;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Tax;

public class HouseholdTaxService : IHouseholdTaxService
{
    private readonly IHouseholdTaxRepository _repository;
    public const decimal TaxExemptionThreshold = 1_000_000_000m; // 1 tỷ VNĐ theo Nghị định 141/2026/NĐ-CP (sửa đổi NĐ 68/2026/NĐ-CP)
    public const decimal RetailVatRate = 1.0m; // 1.0% GTGT ngành thương mại / bán lẻ
    public const decimal RetailPitRate = 0.5m; // 0.5% TNCN ngành thương mại / bán lẻ (phần vượt 1 tỷ)
    public const decimal DeclarationDefaultPitRate = 15.0m; // 15% thuế suất TNCN kê khai tham khảo theo quy mô

    public HouseholdTaxService(IHouseholdTaxRepository repository)
    {
        _repository = repository;
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

        // 2. Tính doanh thu lũy kế đến hết kỳ trước và đến cuối kỳ hiện tại
        var priorPeriodsRevenue = priorPeriodToDateUtc.HasValue
            ? await _repository.GetAnnualAccumulatedRevenueAsync(filter.Year, priorPeriodToDateUtc.Value, cancellationToken)
            : 0m;

        var annualAccumulatedRevenue = await _repository.GetAnnualAccumulatedRevenueAsync(
            filter.Year,
            toDateUtc,
            cancellationToken);

        var (group, groupName, isExempt) = DetermineHouseholdGroup(annualAccumulatedRevenue);

        // 3. Doanh thu phát sinh trong kỳ
        var (gross, discount, returns) = await _repository.GetPeriodRevenueAsync(
            fromDateUtc,
            toDateUtc,
            filter.BranchId,
            cancellationToken);

        var taxableRevenueInPeriod = Math.Max(0, gross - discount - returns);

        decimal vatTaxableRevenue = 0;
        decimal vatRate = 0;
        decimal vatAmount = 0;

        decimal pitTaxableRevenue = 0;
        decimal pitRate = 0;
        decimal pitAmount = 0;

        decimal deductibleExpenses = 0;
        decimal ytdExpenses = 0;
        decimal ytdRevenue = 0;
        decimal taxableIncomeYtd = 0;
        decimal taxDueYtd = 0;
        decimal priorDeclaredPitTax = 0;
        decimal overpaidTaxCarry = 0;
        decimal taxableIncomeDeclaration = 0;
        decimal declarationPitAmount = 0;

        bool isVatReduced = false;
        if (!isExempt)
        {
            // A. THUẾ GTGT: Đánh trên toàn bộ doanh thu phát sinh trong kỳ chịu thuế
            decimal baseVatRate = filter.CustomVatRate ?? RetailVatRate;
            if (filter.ApplyVatReduction2026)
            {
                // Giảm 20% mức tỷ lệ % GTGT theo Nghị quyết 204/2025/QH15 và Nghị định 174/2025/NĐ-CP (1.0% -> 0.8%)
                vatRate = baseVatRate * 0.8m;
                isVatReduced = true;
            }
            else
            {
                vatRate = baseVatRate;
            }

            vatTaxableRevenue = taxableRevenueInPeriod;
            vatAmount = Math.Round(vatTaxableRevenue * (vatRate / 100m), 0, MidpointRounding.AwayFromZero);

            // B. THUẾ TNCN:
            // Tự động chuyển phương pháp Kê khai nếu chủ hộ đăng ký hoặc khi doanh thu năm vượt 3 tỷ (Nhóm 3, Nhóm 4)
            bool isDeclaration = filter.CalculationMethod == TaxCalculationMethod.DeclarationBased 
                || group == HouseholdTaxGroup.Group3_3BTo50B 
                || group == HouseholdTaxGroup.Group4_Over50B;

            if (isDeclaration)
            {
                // Phương pháp Kê khai theo mô hình lũy kế từ đầu năm YTD (Year-To-Date)
                var startOfYearUtc = new DateTime(filter.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                deductibleExpenses = await _repository.GetPeriodDeductibleExpensesAsync(fromDateUtc, toDateUtc, filter.BranchId, cancellationToken);
                ytdExpenses = await _repository.GetPeriodDeductibleExpensesAsync(startOfYearUtc, toDateUtc, filter.BranchId, cancellationToken);
                ytdRevenue = annualAccumulatedRevenue;

                taxableIncomeYtd = Math.Max(0, ytdRevenue - ytdExpenses);
                pitRate = filter.CustomPitRate ?? (group == HouseholdTaxGroup.Group4_Over50B ? 20.0m : (group == HouseholdTaxGroup.Group3_3BTo50B ? 17.0m : DeclarationDefaultPitRate));
                taxDueYtd = Math.Round(taxableIncomeYtd * (pitRate / 100m), 0, MidpointRounding.AwayFromZero);

                // Lấy tổng số thuế TNCN đã kê khai ở các kỳ trước trong năm từ các snapshot đã chốt (hoặc override khi test)
                if (filter.PriorDeclaredPitTaxOverride.HasValue)
                {
                    priorDeclaredPitTax = filter.PriorDeclaredPitTaxOverride.Value;
                }
                else
                {
                    var priorSnapshots = await _repository.GetSnapshotsAsync(filter.Year, filter.BranchId, cancellationToken);
                    priorDeclaredPitTax = priorSnapshots
                        .Where(s => s.PeriodType == filter.PeriodType && s.PeriodNumber < filter.PeriodNumber)
                        .Sum(s => s.PitAmount);
                }

                var pitPayable = Math.Max(0, taxDueYtd - priorDeclaredPitTax);
                overpaidTaxCarry = Math.Max(0, priorDeclaredPitTax - taxDueYtd);

                declarationPitAmount = pitPayable;
                taxableIncomeDeclaration = taxableIncomeYtd;
                pitAmount = declarationPitAmount;
                pitTaxableRevenue = taxableIncomeYtd;
            }
            else
            {
                // Phương pháp Tỷ lệ: Chỉ tính trên phần doanh thu lũy kế vượt 1 tỷ đồng
                var currentExcess = Math.Max(0, annualAccumulatedRevenue - TaxExemptionThreshold);
                var priorExcess = Math.Max(0, priorPeriodsRevenue - TaxExemptionThreshold);
                pitTaxableRevenue = Math.Min(taxableRevenueInPeriod, Math.Max(0, currentExcess - priorExcess));
                pitRate = filter.CustomPitRate ?? RetailPitRate;
                pitAmount = Math.Round(pitTaxableRevenue * (pitRate / 100m), 0, MidpointRounding.AwayFromZero);
            }
        }

        var totalTax = vatAmount + pitAmount;

        // 4. Lấy chứng từ sổ kế toán Thông tư 152/2025/TT-BTC
        var bookEntries = await _repository.GetBookEntriesAsync(
            fromDateUtc,
            toDateUtc,
            filter.BranchId,
            group,
            filter.CalculationMethod,
            null,
            cancellationToken);

        var primaryBook = group switch
        {
            HouseholdTaxGroup.Group1_Under1B => "S1a-HKD",
            HouseholdTaxGroup.Group2_1BTo3B => filter.CalculationMethod == TaxCalculationMethod.DeclarationBased 
                ? "Bộ 4 sổ: S2b, S2c, S2d, S2e-HKD" 
                : "S2a-HKD",
            HouseholdTaxGroup.Group3_3BTo50B => "Bộ 4 sổ: S2b, S2c, S2d, S2e-HKD",
            _ => "Bộ 4 sổ: S2b, S2c, S2d, S2e-HKD (Kèm chế độ kế toán doanh nghiệp cho Nhóm 4)"
        };

        var booksSummary = new TaxBooksSummaryDto
        {
            PrimaryBookCode = primaryBook,
            TotalRecordedRevenue = taxableRevenueInPeriod,
            TotalRecordedExpenses = deductibleExpenses,
            TotalTaxObligationsAccrued = totalTax,
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
            CalculationMethod = filter.CalculationMethod,
            IsTaxExempt = isExempt,
            TaxExemptionThreshold = TaxExemptionThreshold,
            GrossRevenueInPeriod = gross,
            DiscountAmountInPeriod = discount,
            ReturnAmountInPeriod = returns,
            TaxableRevenueInPeriod = taxableRevenueInPeriod,
            VatRate = vatRate,
            IsVatReduced = isVatReduced,
            VatTaxableRevenue = vatTaxableRevenue,
            VatAmount = vatAmount,
            PitRate = pitRate,
            PitTaxableRevenue = pitTaxableRevenue,
            PitAmount = pitAmount,
            DeductibleExpensesInPeriod = deductibleExpenses,
            YtdRevenue = ytdRevenue,
            YtdExpenses = ytdExpenses,
            TaxableIncomeYtd = taxableIncomeYtd,
            TaxDueYtd = taxDueYtd,
            PriorDeclaredPitTax = priorDeclaredPitTax,
            OverpaidTaxCarry = overpaidTaxCarry,
            TaxableIncomeDeclaration = taxableIncomeDeclaration,
            DeclarationPitAmount = declarationPitAmount,
            TotalTaxAmount = totalTax,
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
        CancellationToken cancellationToken = default)
    {
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

        var filter = new HouseholdTaxFilterDto
        {
            Year = request.Year,
            PeriodType = request.PeriodType,
            PeriodNumber = request.PeriodNumber,
            BranchId = request.BranchId,
            CalculationMethod = request.CalculationMethod
        };

        var summary = await GetTaxSummaryAsync(filter, cancellationToken);
        var (fromDateUtc, toDateUtc, _) = GetDateRangesForPeriod(request.Year, request.PeriodType, request.PeriodNumber);
        var bookEntries = await _repository.GetBookEntriesAsync(fromDateUtc, toDateUtc, request.BranchId, summary.HouseholdGroup, request.CalculationMethod, null, cancellationToken);
        var bookDataJson = JsonSerializer.Serialize(bookEntries);

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
            VatRate = summary.VatRate,
            VatAmount = summary.VatAmount,
            PitRate = summary.PitRate,
            PitAmount = summary.PitAmount,
            TotalTaxAmount = summary.TotalTaxAmount,
            BookDataJson = bookDataJson,
            FinalizedBy = finalizedBy,
            FinalizedAtUtc = DateTime.UtcNow,
            Notes = request.Notes
        };

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
            CalculationMethod = request.CalculationMethod,
            IsTaxExempt = created.IsTaxExempt,
            TaxableRevenueInPeriod = created.TaxableRevenueInPeriod,
            VatRate = created.VatRate,
            VatAmount = created.VatAmount,
            PitRate = created.PitRate,
            PitAmount = created.PitAmount,
            TotalTaxAmount = created.TotalTaxAmount,
            FinalizedBy = created.FinalizedBy,
            FinalizedAtUtc = created.FinalizedAtUtc,
            Notes = created.Notes
        };
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
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            IsTaxExempt = s.IsTaxExempt,
            TaxableRevenueInPeriod = s.TaxableRevenueInPeriod,
            VatRate = s.VatRate,
            VatAmount = s.VatAmount,
            PitRate = s.PitRate,
            PitAmount = s.PitAmount,
            TotalTaxAmount = s.TotalTaxAmount,
            FinalizedBy = s.FinalizedBy,
            FinalizedAtUtc = s.FinalizedAtUtc,
            Notes = s.Notes
        }).ToList();
    }

    public async Task<HouseholdTaxSnapshotDto?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var s = await _repository.GetSnapshotByIdAsync(id, cancellationToken);
        if (s == null)
        {
            return null;
        }

        return new HouseholdTaxSnapshotDto
        {
            Id = s.Id,
            BranchId = s.BranchId,
            PeriodType = s.PeriodType,
            Year = s.Year,
            PeriodNumber = s.PeriodNumber,
            AnnualAccumulatedRevenue = s.AnnualAccumulatedRevenue,
            HouseholdGroup = s.HouseholdGroup,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            IsTaxExempt = s.IsTaxExempt,
            TaxableRevenueInPeriod = s.TaxableRevenueInPeriod,
            VatRate = s.VatRate,
            VatAmount = s.VatAmount,
            PitRate = s.PitRate,
            PitAmount = s.PitAmount,
            TotalTaxAmount = s.TotalTaxAmount,
            FinalizedBy = s.FinalizedBy,
            FinalizedAtUtc = s.FinalizedAtUtc,
            Notes = s.Notes
        };
    }

    private static (DateTime FromDateUtc, DateTime ToDateUtc, DateTime? PriorPeriodToDateUtc) GetDateRangesForPeriod(int year, TaxPeriodType periodType, int periodNumber)
    {
        DateTime fromDate;
        DateTime toDate;
        DateTime? priorToDate = null;

        switch (periodType)
        {
            case TaxPeriodType.Month:
                var month = Math.Clamp(periodNumber, 1, 12);
                fromDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                toDate = fromDate.AddMonths(1).AddTicks(-1);
                if (month > 1)
                {
                    priorToDate = fromDate.AddTicks(-1);
                }
                break;

            case TaxPeriodType.Quarter:
                switch (periodNumber)
                {
                    case 1:
                        fromDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        toDate = new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Utc);
                        priorToDate = null;
                        break;
                    case 2:
                        fromDate = new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc);
                        toDate = new DateTime(year, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc);
                        priorToDate = new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Utc);
                        break;
                    case 3:
                        fromDate = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc);
                        toDate = new DateTime(year, 9, 30, 23, 59, 59, 999, DateTimeKind.Utc);
                        priorToDate = new DateTime(year, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc);
                        break;
                    case 4:
                        fromDate = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc);
                        toDate = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);
                        priorToDate = new DateTime(year, 9, 30, 23, 59, 59, 999, DateTimeKind.Utc);
                        break;
                    default:
                        fromDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        toDate = new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Utc);
                        priorToDate = null;
                        break;
                }
                break;

            case TaxPeriodType.Year:
            default:
                fromDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                toDate = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);
                priorToDate = null;
                break;
        }

        return (fromDate, toDate, priorToDate);
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
}
