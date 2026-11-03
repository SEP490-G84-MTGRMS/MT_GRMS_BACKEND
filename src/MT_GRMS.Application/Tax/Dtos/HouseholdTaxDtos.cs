using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Dtos;

public class VatBreakdownDto
{
    public decimal RevenueStandardRate10 { get; set; }
    public decimal TaxAmountStandardRate10 { get; set; }
    public decimal RevenueReducedRate08 { get; set; }
    public decimal TaxAmountReducedRate08 { get; set; }
    public decimal EffectiveVatRate { get; set; }
}

public class HouseholdTaxFilterDto
{
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public TaxPeriodType PeriodType { get; set; } = TaxPeriodType.Quarter;
    public int PeriodNumber { get; set; } = 1;
    public Guid? BranchId { get; set; }
    public TaxCalculationMethod CalculationMethod { get; set; } = TaxCalculationMethod.RevenuePercentage;
    public string? Tier2Method { get; set; } // "REVENUE_EXCESS" (Cách 1: 0.5% vượt 1 tỷ) hoặc "PROFIT_15" (Cách 2: 15% lợi nhuận)
    public bool ApplyVatReduction2026 { get; set; } = false; // Bật/tắt chính sách giảm thuế GTGT (có thời hạn)
    public decimal? VatReductionPercentage { get; set; } // Mức giảm thuế GTGT tùy chỉnh (%, ví dụ: 20% hoặc 30%), mặc định 20% khi bật
    public decimal? ReducedVatRevenueInPeriod { get; set; } // Doanh thu hàng thường được giảm 0.8%
    public decimal? StandardVatRevenueInPeriod { get; set; } // Doanh thu hàng chịu 1.0% (bia, rượu, thuốc lá...)
    public decimal? CustomVatRate { get; set; }
    public decimal? CustomPitRate { get; set; }
    public decimal? PriorDeclaredPitTaxOverride { get; set; }
    public decimal? PriorDeclaredVatTaxOverride { get; set; } // Mock số thuế GTGT đã kê khai các kỳ trước // Cho phép mock số thuế TNCN đã khai các kỳ trước khi test
    public string? VatOption { get; set; } = "PRUDENT_YTD"; // "PRUDENT_YTD" (Thận trọng - Dự phòng toàn bộ năm) hoặc "QUARTER_ONLY" (Chỉ tính quý vượt)
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
    public string Tier2Method { get; set; } = "REVENUE_EXCESS";
    public bool IsTaxExempt { get; set; }
    public decimal TaxExemptionThreshold { get; set; } = 1_000_000_000m; // 1 tỷ VNĐ theo NĐ 141/2026/NĐ-CP

    // Cảnh báo Gross vượt nhưng Net chưa vượt ngưỡng (Doanh thu gộp vs Doanh thu thuần)
    public decimal AnnualAccumulatedGrossRevenue { get; set; }
    public bool IsGrossOver1BNetUnder1B { get; set; }
    public bool IsGrossOver3BNetUnder3B { get; set; }
    public string? NetVsGrossThresholdWarning { get; set; }
    public decimal? GrossBasedEstimatedTaxAmount { get; set; }

    // Cảnh báo vượt ngưỡng miễn thuế giữa năm (từ <= 1 tỷ lên > 1 tỷ)
    public bool IsThresholdCrossedMidYear { get; set; }
    public string? ThresholdExceededNote { get; set; }

    // Cảnh báo tiến trình ngưỡng 3 tỷ VNĐ (Điểm d Khoản 5 Điều 4 Nghị định 68/2026/NĐ-CP)
    public bool IsApproaching3BThreshold { get; set; } // >= 2.7 tỷ (90% của 3 tỷ)
    public bool IsExceeding3BThreshold { get; set; } // > 3 tỷ
    public bool Is3BCrossedMidYear { get; set; } // Vừa vượt 3 tỷ giữa năm (giữ tỷ lệ % đến hết năm, năm sau mới chuyển)
    public int? DeclarationMethodStartYear { get; set; } // Năm bắt buộc áp dụng phương pháp kê khai (ổn định 2 năm)

    // Cảnh báo tiến trình ngưỡng Nhóm 4 (toàn chuỗi Tenant)
    public bool IsApproachingGroup4Threshold { get; set; } // >= 40 tỷ (80% của 50 tỷ)
    public bool IsExceedingGroup4Threshold { get; set; } // >= 50 tỷ (100%)

    // Doanh thu trong kỳ
    public decimal GrossRevenueInPeriod { get; set; }
    public decimal DiscountAmountInPeriod { get; set; }
    public decimal ReturnAmountInPeriod { get; set; }
    public decimal TaxableRevenueInPeriod { get; set; }

    // Chi tiết thuế GTGT & Thuế suất hiệu dụng (Effective VAT Rate)
    public decimal OriginalVatRate { get; set; } = 1.0m; // Thuế suất GTGT gốc (ví dụ: 1.0%)
    public decimal VatReductionPercentage { get; set; } = 0m; // Mức giảm GTGT đang áp dụng (ví dụ: 20%)
    public decimal VatRate { get; set; } // Thuế suất GTGT hiệu dụng (VatAmount / TaxableRevenue)
    public bool IsVatReduced { get; set; } // Cờ xác nhận áp dụng chính sách giảm thuế GTGT 2026
    public decimal VatTaxableRevenue { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PriorDeclaredVatTax { get; set; } // Số thuế GTGT đã kê khai các kỳ trước trong năm
    public VatBreakdownDto VatBreakdown { get; set; } = new();

    // Tùy chọn ước tính thuế GTGT khi vượt 1 tỷ giữa năm (Thận trọng YTD vs Tối thiểu theo kỳ)
    public decimal QuarterlyVatAmount { get; set; }
    public decimal RetrospectiveVatAmount { get; set; }
    public string VatCalculationOption { get; set; } = "PRUDENT_YTD";
    public decimal PeriodNetRevenue { get; set; }

    // Chi tiết thuế TNCN (Chỉ tính trên phần doanh thu lũy kế vượt 1 tỷ hoặc trên lợi nhuận)
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
    public string? Tier2Method { get; set; } // "REVENUE_EXCESS" | "PROFIT_15"
    public string? VatOption { get; set; } = "PRUDENT_YTD"; // "PRUDENT_YTD" | "QUARTER_ONLY"
    public string SecurityPin { get; set; } = string.Empty;
    public bool ApplyVatReduction2026 { get; set; } = false;
    public decimal? VatReductionPercentage { get; set; }
    public decimal? ReducedVatRevenueInPeriod { get; set; }
    public decimal? StandardVatRevenueInPeriod { get; set; }
    public decimal? CustomVatRate { get; set; }
    public decimal? CustomPitRate { get; set; }

    // Ràng buộc kiểm toán khi dùng giá vốn ước tính
    public bool ConfirmEstimatedCogsResponsible { get; set; } = false;
    public string? EstimatedCogsConfirmationStatement { get; set; }

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
    public TaxCalculationMethod? AppliedPitMethod { get; set; }
    public bool IsTaxExempt { get; set; }
    public decimal TaxableRevenueInPeriod { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PitRate { get; set; }
    public decimal PitAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public VatBreakdownDto VatBreakdown { get; set; } = new();
    public bool IsCogsEstimated { get; set; }
    public string AuditSignatureHash { get; set; } = string.Empty;
    public bool IsIntegrityValid { get; set; } = true;
    public string FinalizedBy { get; set; } = string.Empty;
    public DateTime FinalizedAtUtc { get; set; }
    public string? Notes { get; set; }
}
