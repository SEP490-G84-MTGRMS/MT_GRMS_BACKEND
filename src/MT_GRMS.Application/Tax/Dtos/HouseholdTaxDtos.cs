using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Dtos;

public class HouseholdTaxFilterDto
{
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public TaxPeriodType PeriodType { get; set; } = TaxPeriodType.Quarter;
    public int PeriodNumber { get; set; } = 1;
    public Guid? BranchId { get; set; }
}

public class HouseholdTaxSummaryDto
{
    public int Year { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public decimal AnnualAccumulatedRevenue { get; set; }
    public HouseholdTaxGroup HouseholdGroup { get; set; }
    public string HouseholdGroupName { get; set; } = string.Empty;
    public bool IsTaxExempt { get; set; }
    public decimal TaxExemptionThreshold { get; set; } = 1_000_000_000m; // 1 tỷ VNĐ theo NĐ 141/2026

    public decimal GrossRevenueInPeriod { get; set; }
    public decimal DiscountAmountInPeriod { get; set; }
    public decimal ReturnAmountInPeriod { get; set; }
    public decimal TaxableRevenueInPeriod { get; set; }

    public decimal VatRate { get; set; } // 1.0%
    public decimal VatAmount { get; set; }
    public decimal PitRate { get; set; } // 0.5%
    public decimal PitAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }

    public TaxBooksSummaryDto BooksSummary { get; set; } = new();

    public bool IsFinalized { get; set; }
    public Guid? SnapshotId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public string? FinalizedBy { get; set; }
}

public class TaxBooksSummaryDto
{
    public decimal S2bTotalRevenue { get; set; }
    public decimal S2cTotalSalary { get; set; }
    public decimal S2dTotalSupplies { get; set; }
    public decimal S2eTotalServices { get; set; }
    public decimal TotalDeductibleExpenses => S2cTotalSalary + S2dTotalSupplies + S2eTotalServices;
}

public class AccountingBookEntryDto
{
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public AccountingBookType BookType { get; set; }
    public string BookCode { get; set; } = string.Empty; // S2b-HKD, S2c-HKD, S2d-HKD, S2e-HKD
    public string? BeneficiaryName { get; set; }
    public string? InvoiceNumber { get; set; }
}

public class CreateTaxSnapshotRequest
{
    public int Year { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public Guid? BranchId { get; set; }
    public string? Notes { get; set; }
}

public class HouseholdTaxSnapshotDto
{
    public Guid Id { get; set; }
    public Guid? BranchId { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int Year { get; set; }
    public int PeriodNumber { get; set; }
    public decimal AnnualAccumulatedRevenue { get; set; }
    public HouseholdTaxGroup HouseholdGroup { get; set; }
    public bool IsTaxExempt { get; set; }
    public decimal TaxableRevenueInPeriod { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PitRate { get; set; }
    public decimal PitAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public string FinalizedBy { get; set; } = string.Empty;
    public DateTime FinalizedAtUtc { get; set; }
    public string? Notes { get; set; }
}
