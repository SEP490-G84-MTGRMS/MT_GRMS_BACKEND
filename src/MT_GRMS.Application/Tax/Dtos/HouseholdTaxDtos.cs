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

public class RiskyGoodsReceiptDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? InvoiceNo { get; set; }
    public string RiskReason { get; set; } = string.Empty;
}

public class TaxExpenseComplianceSummaryDto
{
    public decimal ExcludedCashExpensesAmount { get; set; }
    public int ExcludedCashExpensesCount { get; set; }
    public decimal ExcludedOwnerSalaryAmount { get; set; }
    public int ExcludedOwnerSalaryCount { get; set; }
    public List<RiskyGoodsReceiptDto> RiskyGoodsReceipts { get; set; } = new();
}

public class HouseholdTaxFilterDto
{
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public TaxPeriodType PeriodType { get; set; } = TaxPeriodType.Quarter;
    public int PeriodNumber { get; set; } = 1;
    public Guid? BranchId { get; set; }
    public AccountingBookType? BookType { get; set; }
    public TaxCalculationMethod CalculationMethod { get; set; } = TaxCalculationMethod.RevenuePercentage;
    public string? Tier2Method { get; set; } // "REVENUE_EXCESS" (Cách 1: 0.5% vượt 1 tỷ) hoặc "PROFIT_15" (Cách 2: 15% lợi nhuận)
    public bool ApplyVatReduction2026 { get; set; } = false; // Bật/tắt chính sách giảm thuế GTGT (có thời hạn đến 31/12/2026)
    public decimal? VatReductionPercentage { get; set; } // Mức giảm thuế GTGT tùy chỉnh (%, ví dụ: 20% hoặc 30%), mặc định 20% khi bật
    public bool? ApplyPitReduction { get; set; } // Giảm 30% thuế TNCN theo NQ 43/2026 & NĐ 398/2026 (null = tự động áp dụng năm 2026-2027)
    public decimal? ReducedVatRevenueInPeriod { get; set; } // Doanh thu hàng thường được giảm 0.8%
    public decimal? StandardVatRevenueInPeriod { get; set; } // Doanh thu hàng chịu 1.0% (bia, rượu, thuốc lá...)
    public decimal? CustomVatRate { get; set; }
    public decimal? CustomPitRate { get; set; }
    public decimal? PriorDeclaredPitTaxOverride { get; set; }
    public decimal? PriorDeclaredVatTaxOverride { get; set; }
    public string? VatOption { get; set; } = "PRUDENT_YTD"; // Luôn áp dụng PRUDENT_YTD theo hướng dẫn NĐ 68/2026
}

public class HouseholdTaxSummaryDto
{
    public int Year { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateTime FilingDeadlineLocal { get; set; }
    public string FilingDeadlineNote { get; set; } = string.Empty;

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
    public DateTime? EInvoiceRegistrationDeadlineLocal { get; set; } // Hạn 30 ngày đăng ký HĐĐT từ máy tính tiền theo NĐ 254/2026

    // Cảnh báo tiến trình ngưỡng chuyển đổi phương pháp (3 tỷ năm 2026, 10 tỷ từ năm 2027)
    public decimal EffectiveMethodSwitchThreshold { get; set; } = 3_000_000_000m;
    public bool IsApproaching3BThreshold { get; set; } // >= 90% ngưỡng chuyển đổi
    public bool IsExceeding3BThreshold { get; set; } // > ngưỡng chuyển đổi
    public bool Is3BCrossedMidYear { get; set; } // Vừa vượt ngưỡng giữa năm (giữ tỷ lệ % đến hết năm)
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
    public bool IsVatReductionPolicyActive { get; set; } = true; // True nếu kỳ đang nằm trong thời hạn VatReductionDeadlineUtc của TaxSettings:Policy
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
    public decimal PitRate { get; set; } // 0.5% (tỷ lệ/tạm nộp) hoặc 15-20% (quyết toán kê khai)
    public decimal PitTaxableRevenue { get; set; } // Doanh thu chịu thuế TNCN hoặc thu nhập tính thuế khi quyết toán
    public decimal PitBeforeReduction { get; set; } // Thuế TNCN trước khi giảm 30% (NQ 43/2026 & NĐ 398/2026)
    public decimal PitReductionPercentage { get; set; } // Tỷ lệ giảm thuế TNCN đang áp dụng (30% nếu <= 10 tỷ)
    public decimal PitReductionAmount { get; set; } // Số tiền thuế TNCN được giảm 30% trong kỳ
    public bool IsPitReduced { get; set; } // True nếu kỳ đang áp dụng giảm 30% TNCN theo NĐ 398/2026
    public bool RequiresPitReductionAdjustmentForm03 { get; set; } // True khi vượt 10 tỷ và cần nộp Mẫu số 03 (NĐ 398/2026)
    public string? PitReductionFormCode { get; set; } // "Mẫu số 01 (NĐ 398/2026)" | "Mẫu số 03 (NĐ 398/2026)" | null
    public decimal PitAmount { get; set; }

    // Phương pháp Kê khai theo mô hình lũy kế từ đầu năm YTD (Year-To-Date)
    public decimal CogsAmount { get; set; } // Giá vốn hàng bán có chứng từ hợp lệ trong kỳ
    public decimal OpexAmount { get; set; } // Chi phí vận hành hợp lệ theo Điều 6 NĐ 68 trong kỳ
    public bool IsCogsEstimated { get; set; } // True nếu có mặt hàng bán ra thiếu giá vốn nhập kho (KHÔNG tự cộng 70% vào chi phí được trừ)
    public decimal EstimatedCogsWarningAmount { get; set; } // Giá vốn ước tính tham khảo nội bộ cho các mặt hàng thiếu chứng từ (KHÔNG đưa vào tờ khai)
    public decimal DeductibleExpensesInPeriod { get; set; }
    public decimal YtdRevenue { get; set; }
    public decimal YtdExpenses { get; set; }
    public decimal TaxableIncomeYtd { get; set; }
    public decimal TaxDueYtdBeforeReduction { get; set; }
    public decimal PitReductionYtd { get; set; }
    public decimal TaxDueYtd { get; set; }
    public decimal PriorDeclaredPitTax { get; set; }
    public decimal OverpaidTaxCarry { get; set; }
    public decimal TaxableIncomeDeclaration { get; set; } // Thu nhập tính thuế lũy kế
    public decimal DeclarationPitAmount { get; set; }
    public bool IsProvisionalPitPeriod { get; set; } // True khi kê khai theo thu nhập đang ở kỳ tạm nộp Tháng/Quý (Khoản 2 Điều 10 NĐ 68)
    public decimal AnnualSettlementPitRate { get; set; } // Thuế suất quyết toán năm (15% / 17% / 20%)
    public decimal EstimatedAnnualSettlementPit { get; set; } // Thuế TNCN dự kiến khi quyết toán năm theo lợi nhuận thực tế

    // Kiểm soát chi phí hợp lệ theo Điều 6 NĐ 68/2026/NĐ-CP
    public decimal ExcludedCashExpensesAmount { get; set; }
    public int ExcludedCashExpensesCount { get; set; }
    public decimal ExcludedOwnerSalaryAmount { get; set; }
    public int ExcludedOwnerSalaryCount { get; set; }
    public List<RiskyGoodsReceiptDto> RiskyGoodsReceipts { get; set; } = new();

    public decimal TotalTaxAmount { get; set; }

    // Sổ sách theo Thông tư 152/2025/TT-BTC
    public TaxBooksSummaryDto BooksSummary { get; set; } = new();

    public bool IsFinalized { get; set; }
    public Guid? SnapshotId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public string? FinalizedBy { get; set; }
}

public class HouseholdTaxpayerProfileDto
{
    public string TaxpayerName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string BusinessLocation { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string TaxAuthorityName { get; set; } = string.Empty;
}

public class TaxDeclarationLineDto
{
    public int Sequence { get; set; }
    public string BusinessSector { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public decimal VatNonTaxableRevenue { get; set; }
    public decimal VatTaxableRevenue { get; set; }
    public decimal VatZeroRateRevenue { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PitTaxableRevenue { get; set; }
    public decimal PitRevenueDeduction { get; set; }
    public decimal PitRate { get; set; }
    public decimal PitAmount { get; set; }
}

public class HouseholdTaxDeclarationPreviewDto
{
    public string FormCode { get; set; } = "01/CNKD";
    public string FormTitle { get; set; } = "Tờ khai thuế đối với hộ kinh doanh, cá nhân kinh doanh";
    public string LegalBasis { get; set; } = "Mẫu 01/CNKD";
    public bool IsFinalized { get; set; }
    public Guid? SnapshotId { get; set; }
    public int RevisionNumber { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public int Year { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime FilingDeadlineLocal { get; set; }
    public bool IsFirstDeclaration { get; set; } = true;
    public bool IsSupplementaryDeclaration { get; set; }
    public HouseholdTaxpayerProfileDto Taxpayer { get; set; } = new();
    public List<TaxDeclarationLineDto> Lines { get; set; } = new();
    public decimal TotalVatAmount { get; set; }
    public decimal TotalPitAmount { get; set; }
    public decimal PitBeforeReduction { get; set; }
    public decimal PitReductionPercentage { get; set; }
    public decimal PitReductionAmount { get; set; }
    public bool IsPitReduced { get; set; }
    public string? PitReductionFormCode { get; set; }
    public string? PitReductionNote { get; set; }
    public bool IsProvisionalPitPeriod { get; set; }
    public decimal EstimatedAnnualSettlementPit { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public bool RequiresBusinessActivityAppendix { get; set; }
    public string AppendixCode { get; set; } = "01-2/BK-HĐKD";
    public decimal AppendixRevenue { get; set; }
    public decimal AppendixCogs { get; set; }
    public decimal AppendixOpex { get; set; }
    public decimal AppendixTaxableIncome { get; set; }
    public bool IsProfileComplete { get; set; }
    public List<string> MissingProfileFields { get; set; } = new();
    public string DataNotice { get; set; } = string.Empty;
}

public class TaxDeclarationXmlResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
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

    // Các trường bổ trợ chuẩn hóa theo Thông tư 152/2025/TT-BTC
    public string? Unit { get; set; } // Đơn vị tính (S2d)
    public decimal? UnitPrice { get; set; } // Đơn giá (S2d)
    public decimal? InQuantity { get; set; } // Số lượng nhập (S2d)
    public decimal? InAmount { get; set; } // Số tiền nhập / thu / gửi vào (S2d, S2e)
    public decimal? OutQuantity { get; set; } // Số lượng xuất (S2d)
    public decimal? OutAmount { get; set; } // Số tiền xuất / chi / rút ra (S2d, S2e)
    public decimal? BalanceQuantity { get; set; } // Số lượng tồn (S2d)
    public decimal? BalanceAmount { get; set; } // Số tiền tồn / số dư quỹ (S2d, S2e)
    public string? SectionGroup { get; set; } // Phân nhóm (ngành nghề S2a/S2b, mục chi phí a->e S2c, tài khoản tiền S2e, loại thuế S3a)
    public decimal? TaxRate { get; set; } // Tỷ lệ % hoặc thuế suất (S2a, S2b, S2c)
    public decimal? TaxAmount { get; set; } // Số tiền thuế (S2a, S2b, S2c, S3a)
    public string? Note { get; set; } // Ghi chú
    public bool IsSummaryRow { get; set; } // Dòng tổng cộng / phân nhóm / thuế
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
    public bool? ApplyPitReduction { get; set; }
    public decimal? ReducedVatRevenueInPeriod { get; set; }
    public decimal? StandardVatRevenueInPeriod { get; set; }
    public decimal? CustomVatRate { get; set; }
    public decimal? CustomPitRate { get; set; }

    // Ràng buộc kiểm toán khi dùng giá vốn ước tính
    public bool ConfirmEstimatedCogsResponsible { get; set; } = false;
    public string? EstimatedCogsConfirmationStatement { get; set; }

    /// <summary>
    /// Cho phép chốt lại (điều chỉnh hồi tố) kỳ tính thuế đã khóa trước đó.
    /// </summary>
    public bool AllowSupersedeExisting { get; set; } = false;

    /// <summary>
    /// Bắt buộc khi AllowSupersedeExisting = true: Lý do thay thế / số biên bản điều chỉnh.
    /// </summary>
    public string? RectificationReason { get; set; }

    public string? Notes { get; set; }
}

public class HouseholdTaxSnapshotDto
{
    public Guid Id { get; set; }
    public string TaxPolicyJson { get; set; } = "{}";
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
    public TaxSnapshotStatus Status { get; set; } = TaxSnapshotStatus.Finalized;
    public int RevisionNumber { get; set; } = 0;
    public Guid? SupersededSnapshotId { get; set; }
    public string? RectificationReason { get; set; }
}

/// <summary>
/// DTO yêu cầu thiết lập / đổi mã PIN bảo mật của chủ hộ kinh doanh.
/// </summary>
public class ChangeSecurityPinRequest
{
    public string? CurrentPassword { get; set; }
    public string? CurrentPin { get; set; }
    public string NewPin { get; set; } = string.Empty;
}

/// <summary>
/// DTO yêu cầu khôi phục mã PIN bảo mật khi quên (xác thực qua mật khẩu đăng nhập).
/// </summary>
public class ResetSecurityPinRequest
{
    public string? UserId { get; set; }
    public string Password { get; set; } = string.Empty;
    public string NewPin { get; set; } = string.Empty;
}
