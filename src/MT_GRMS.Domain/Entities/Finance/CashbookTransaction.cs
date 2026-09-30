using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

public enum CashbookType
{
    Receipt = 1, // Thu
    Payment = 2  // Chi
}

public class CashbookTransaction : BaseTenantEntity
{
    public string TransactionCode { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public CashbookType Type { get; set; }
    public string Category { get; set; } = string.Empty; // Bán hàng, Nhập hàng, Điện nước, Lương...
    public decimal Amount { get; set; }
    public string ReferenceCode { get; set; } = string.Empty; // Mã hóa đơn hoặc mã phiếu nhập
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
}
