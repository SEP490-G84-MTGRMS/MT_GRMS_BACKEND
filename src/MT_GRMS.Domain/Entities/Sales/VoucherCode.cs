using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng VoucherCodes (Quản lý trạng thái và việc sử dụng từng mã voucher/coupon theo đợt)
/// </summary>
public class VoucherCode : BaseTenantEntity
{
    public Guid PromotionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = "UNUSED"; // UNUSED, USED, EXPIRED, LOCKED
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public Guid? OrderId { get; set; }
    public string? OrderCode { get; set; }
    public DateTime? UsedAt { get; set; }

    // Navigations
    public Promotion? Promotion { get; set; }
}

