using System;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng ProductPriceHistories: Lưu vết nhật ký thay đổi giá bán sản phẩm (FE-03.2)
/// Phân biệt phạm vi Master (toàn chuỗi, BranchId = null) và Branch (giá riêng chi nhánh, BranchId != null).
/// </summary>
public class ProductPriceHistory : BaseTenantEntity
{
    public Guid ProductId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ProductUnitId { get; set; }

    /// <summary>
    /// MASTER: Giá bán chuẩn toàn hệ thống; BRANCH: Giá bán riêng theo chi nhánh
    /// </summary>
    public string PriceScope { get; set; } = "MASTER"; // MASTER, BRANCH

    public string PriceType { get; set; } = "SELLING_PRICE"; // SELLING_PRICE, COST_PRICE

    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? CostPriceSnapshot { get; set; }

    /// <summary>
    /// Đánh dấu nếu giá bán mới thấp hơn giá vốn (FE-03.1 Below-Cost Warning)
    /// </summary>
    public bool IsBelowCost { get; set; } = false;

    public string? ManagerApprovedBy { get; set; }
    public string? ManagerApprovalReason { get; set; }

    public string ChangedByUserId { get; set; } = string.Empty;
    public string ChangedByUserName { get; set; } = string.Empty;
    public string Reason { get; set; } = "Điều chỉnh giá bán";

    // Navigations
    public Product? Product { get; set; }
    public Branch? Branch { get; set; }
}

