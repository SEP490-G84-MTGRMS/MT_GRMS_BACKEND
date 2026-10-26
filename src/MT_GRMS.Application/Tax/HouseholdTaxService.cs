using System.Text.Json;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Tax;

public class HouseholdTaxService : IHouseholdTaxService
{
    private readonly IHouseholdTaxRepository _repository;
    public const decimal TaxExemptionThreshold = 1_000_000_000m; // 1 tỷ VNĐ theo Luật 108/2025/QH15 & NĐ 141/2026/NĐ-CP
    public const decimal RetailVatRate = 1.0m; // 1.0% GTGT ngành bán lẻ hàng hóa
    public const decimal RetailPitRate = 0.5m; // 0.5% TNCN ngành bán lẻ hàng hóa

    public HouseholdTaxService(IHouseholdTaxRepository repository)
    {
        _repository = repository;
    }

    public async Task<HouseholdTaxSummaryDto> GetTaxSummaryAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default)
    {
        var (fromDateUtc, toDateUtc) = GetDateRangeForPeriod(filter.Year, filter.PeriodType, filter.PeriodNumber);

        // 1. Kiểm tra xem kỳ này đã được chốt chưa
        var snapshot = await _repository.GetSnapshotAsync(
            filter.Year,
            filter.PeriodType,
            filter.PeriodNumber,
            filter.BranchId,
            cancellationToken);

        // 2. Tính doanh thu lũy kế năm của toàn Tenant đến cuối kỳ này để xác định nhóm HKD
        var annualAccumulatedRevenue = await _repository.GetAnnualAccumulatedRevenueAsync(
            filter.Year,
            toDateUtc,
            cancellationToken);

        var (group, groupName, isExempt, vatRate, pitRate) = DetermineHouseholdGroup(annualAccumulatedRevenue);

        // 3. Doanh thu tính thuế trong kỳ
        var (gross, discount, returns) = await _repository.GetPeriodRevenueAsync(
            fromDateUtc,
            toDateUtc,
            filter.BranchId,
            cancellationToken);

        var taxableRevenue = Math.Max(0, gross - discount - returns);

        decimal vatAmount = 0;
        decimal pitAmount = 0;

        if (!isExempt)
        {
            vatAmount = Math.Round(taxableRevenue * (vatRate / 100m), 2, MidpointRounding.AwayFromZero);
            pitAmount = Math.Round(taxableRevenue * (pitRate / 100m), 2, MidpointRounding.AwayFromZero);
        }

        var totalTax = vatAmount + pitAmount;

        // 4. Tổng hợp số liệu 4 sổ TT 152/2025/TT-BTC
        var bookEntries = await _repository.GetBookEntriesAsync(
            fromDateUtc,
            toDateUtc,
            filter.BranchId,
            null,
            cancellationToken);

        var booksSummary = new TaxBooksSummaryDto
        {
            S2bTotalRevenue = bookEntries.Where(x => x.BookType == AccountingBookType.S2b_Revenue).Sum(x => x.Amount),
            S2cTotalSalary = bookEntries.Where(x => x.BookType == AccountingBookType.S2c_Salary).Sum(x => x.Amount),
            S2dTotalSupplies = bookEntries.Where(x => x.BookType == AccountingBookType.S2d_Supplies).Sum(x => x.Amount),
            S2eTotalServices = bookEntries.Where(x => x.BookType == AccountingBookType.S2e_Services).Sum(x => x.Amount)
        };

        return new HouseholdTaxSummaryDto
        {
            Year = filter.Year,
            PeriodType = filter.PeriodType,
            PeriodNumber = filter.PeriodNumber,
            BranchId = filter.BranchId,
            BranchName = filter.BranchId.HasValue ? $"Chi nhánh {filter.BranchId.Value.ToString()[..8]}" : "Toàn bộ hệ thống",
            AnnualAccumulatedRevenue = annualAccumulatedRevenue,
            HouseholdGroup = group,
            HouseholdGroupName = groupName,
            IsTaxExempt = isExempt,
            TaxExemptionThreshold = TaxExemptionThreshold,
            GrossRevenueInPeriod = gross,
            DiscountAmountInPeriod = discount,
            ReturnAmountInPeriod = returns,
            TaxableRevenueInPeriod = taxableRevenue,
            VatRate = vatRate,
            VatAmount = vatAmount,
            PitRate = pitRate,
            PitAmount = pitAmount,
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
        var (fromDateUtc, toDateUtc) = GetDateRangeForPeriod(year, periodType, periodNumber);

        return await _repository.GetBookEntriesAsync(
            fromDateUtc,
            toDateUtc,
            branchId,
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
            BranchId = request.BranchId
        };

        var summary = await GetTaxSummaryAsync(filter, cancellationToken);
        var (fromDateUtc, toDateUtc) = GetDateRangeForPeriod(request.Year, request.PeriodType, request.PeriodNumber);
        var bookEntries = await _repository.GetBookEntriesAsync(fromDateUtc, toDateUtc, request.BranchId, null, cancellationToken);
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

    private static (DateTime FromDateUtc, DateTime ToDateUtc) GetDateRangeForPeriod(int year, TaxPeriodType periodType, int periodNumber)
    {
        return periodType switch
        {
            TaxPeriodType.Month => (
                new DateTime(year, Math.Clamp(periodNumber, 1, 12), 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(year, Math.Clamp(periodNumber, 1, 12), 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddTicks(-1)
            ),
            TaxPeriodType.Quarter => periodNumber switch
            {
                1 => (new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Utc)),
                2 => (new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc)),
                3 => (new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 9, 30, 23, 59, 59, 999, DateTimeKind.Utc)),
                4 => (new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)),
                _ => (new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 3, 31, 23, 59, 59, 999, DateTimeKind.Utc))
            },
            TaxPeriodType.Year => (
                new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)
            ),
            _ => (new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc))
        };
    }

    private static (HouseholdTaxGroup Group, string GroupName, bool IsExempt, decimal VatRate, decimal PitRate) DetermineHouseholdGroup(decimal annualAccumulatedRevenue)
    {
        if (annualAccumulatedRevenue <= TaxExemptionThreshold)
        {
            return (
                HouseholdTaxGroup.Group1_Under1B,
                "Nhóm 1 (Doanh thu năm <= 1 tỷ đồng)",
                true,
                0m,
                0m
            );
        }

        if (annualAccumulatedRevenue <= 3_000_000_000m)
        {
            return (
                HouseholdTaxGroup.Group2_1BTo3B,
                "Nhóm 2 (Doanh thu năm từ 1 tỷ - 3 tỷ đồng)",
                false,
                RetailVatRate,
                RetailPitRate
            );
        }

        if (annualAccumulatedRevenue <= 50_000_000_000m)
        {
            return (
                HouseholdTaxGroup.Group3_3BTo50B,
                "Nhóm 3 (Doanh thu năm từ 3 tỷ - 50 tỷ đồng)",
                false,
                RetailVatRate,
                RetailPitRate
            );
        }

        return (
            HouseholdTaxGroup.Group4_Over50B,
            "Nhóm 4 (Doanh thu năm trên 50 tỷ đồng)",
            false,
            RetailVatRate,
            RetailPitRate
        );
    }
}
