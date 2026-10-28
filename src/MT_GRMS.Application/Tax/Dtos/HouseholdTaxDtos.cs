using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Dtos;

public class HouseholdTaxFilterDto
{
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public TaxPeriodType PeriodType { get; set; } = TaxPeriodType.Quarter;
    public int PeriodNumber { get; set; } = 1;
    public Guid? BranchId { get; set; }
    public TaxCalculationMethod CalculationMethod { get; set; } = TaxCalculationMethod.RevenuePercentage;
    public string SecurityPin { get; set; } = string.Empty;
    public bool ApplyVatReduction2026 { get; set; } = false; // Giảm 20% mức tỷ lệ GTGT (còn 0.8%) theo NQ 204/2025/QH15 & NĐ 174/2025/NĐ-CP
    public decimal? CustomVatRate { get; set; }
    public decimal? CustomPitRate { get; set; }
    public decimal? PriorDeclaredPitTaxOverride { get; set; } // Cho phép mock số thuế TNCN đã khai các kỳ trước khi test
}

public class HouseholdTaxSummaryDto
{
    public int Year { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public decimal AnnualAccumulatedRevenue { get; set; }
    public decimal PriorPeriodsAccumulatedRevenue { get; set; }
    public HouseholdTaxGroup HouseholdGroup { get; set; }
    public string HouseholdGroupName { get; set; } = string.Empty;
    public TaxCalculationMethod CalculationMethod { get; set; }
    public bool IsTaxExempt { get; set; }
    public decimal TaxExemptionThreshold { get; set; } = 1_000_000_000m; // 1 tỷ VNĐ theo NĐ 141/2026/NĐ-CP

    // Doanh thu trong kỳ
    public decimal GrossRevenueInPeriod { get; set; }
    public decimal DiscountAmountInPeriod { get; set; }
    public decimal ReturnAmountInPeriod { get; set; }
    public decimal TaxableRevenueInPeriod { get; set; }

    // Chi tiết thuế GTGT (Tính trên toàn bộ doanh thu kỳ khi lũy kế năm > 1 tỷ)
    public decimal VatRate { get; set; } // 1.0% hoặc 0.8% nếu giảm 20%
    public bool IsVatReduced { get; set; } // Cờ xác nhận áp dụng chính sách giảm thuế GTGT 2026
    public decimal VatTaxableRevenue { get; set; }
    public decimal VatAmount { get; set; }

    // Chi tiết thuế TNCN (Chỉ tính trên phần doanh thu lũy kế vượt 1 tỷ)
    public decimal PitRate { get; set; } // 0.5% (tỷ lệ) hoặc 15-20% (kê khai)
    public decimal PitTaxableRevenue { get; set; } // Doanh thu chịu thuế TNCN
    public decimal PitAmount { get; set; }

    // Phương pháp Kê khai theo mô hình lũy kế từ đầu năm YTD (Year-To-Date)
    public decimal CogsAmount { get; set; } // Giá vốn hàng bán (COGS) trong kỳ
    public decimal OpexAmount { get; set; } // Chi phí vận hành hợp lý (OPEX) trong kỳ
    public bool IsCogsEstimated { get; set; } // Cờ cảnh báo: True nếu giá vốn đang áp dụng ước tính 70% do thiếu giá vốn sản phẩm
    public decimal DeductibleExpensesInPeriod { get; set; }
    public decimal YtdRevenue { get; set; }
    public decimal YtdExpenses { get; set; }
    public decimal TaxableIncomeYtd { get; set; }
    public decimal TaxDueYtd { get; set; }
    public decimal PriorDeclaredPitTax { get; set; }
    public decimal OverpaidTaxCarry { get; set; }
    public decimal TaxableIncomeDeclaration { get; set; } // Thu nhập tính thuế lũy kế
    public decimal DeclarationPitAmount { get; set; }

    public decimal TotalTaxAmount { get; set; }

    // Sổ sách theo Thông tư 152/2025/TT-BTC
    public TaxBooksSummaryDto BooksSummary { get; set; } = new();

    public bool IsFinalized { get; set; }
    public Guid? SnapshotId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public string? FinalizedBy { get; set; }
}

public class TaxBooksSummaryDto
{
    public string PrimaryBookCode { get; set; } = "S2a-HKD";
    public decimal TotalRecordedRevenue { get; set; }
    public decimal TotalRecordedExpenses { get; set; }
    public decimal TotalCashAndBankReceipts { get; set; }
    public decimal TotalCashAndBankDisbursements { get; set; }
    public decimal TotalTaxObligationsAccrued { get; set; }
    public decimal TotalTaxPaidToState { get; set; }
}

public class AccountingBookEntryDto
{
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public AccountingBookType BookType { get; set; }
    public string BookCode { get; set; } = string.Empty; // S1a, S2a, S2b, S2c, S2d, S2e, S3a
    public string? BeneficiaryName { get; set; }
    public string? InvoiceNumber { get; set; }
}

public class CreateTaxSnapshotRequest
{
    public int Year { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public Guid? BranchId { get; set; }
    public TaxCalculationMethod CalculationMethod { get; set; } = TaxCalculationMethod.RevenuePercentage;
    public string SecurityPin { get; set; } = string.Empty;
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
    public TaxCalculationMethod CalculationMethod { get; set; }
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


