using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Operational expense voucher (FE-07).
/// </summary>
public class ExpenseVoucher : BaseTenantEntity
{
    public string VoucherCode { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty; // Người nhận tiền
    public string Category { get; set; } = string.Empty; // Điện, nước, mặt bằng, rác, sửa chữa...
    public decimal Amount { get; set; }
    public ExpenseVoucherStatus Status { get; set; } = ExpenseVoucherStatus.Draft;
    public FinancialNature Nature { get; set; } = FinancialNature.OperatingExpense;
    public string? ReferenceCode { get; set; } // Mã chứng từ tham chiếu (phiếu cọc gốc, đơn nhập, ca trực...)
    public decimal RefundedAmount { get; set; } = 0; // Số tiền cọc đã hoàn trả (đối với phiếu cọc)
    public bool IsSettled { get; set; } = false; // Đã tất toán hoàn toàn hay chưa
    public FinanceAccountType PaymentSourceType { get; set; } = FinanceAccountType.Till;
    public Guid? PaymentSourceAccountId { get; set; }
    public bool IsTaxDeductible { get; set; } = false; // Một sổ hai góc nhìn
    public string? InvoiceNumber { get; set; }
    public string? SellerTaxCode { get; set; }
    public string? EvidenceAttachmentUrl { get; set; }
    public bool IsFlaggedAsSuspiciousSplit { get; set; } = false; // Cờ nghi vấn tách bill
    public string? SuspiciousReason { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow; // Kỳ tính P&L dồn tích
    public string? Note { get; set; }
}