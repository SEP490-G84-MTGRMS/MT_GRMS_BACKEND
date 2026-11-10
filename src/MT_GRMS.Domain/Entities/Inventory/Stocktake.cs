using System;
using System.Collections.Generic;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Bảng Stocktakes (Phiếu kiểm kê hàng hóa tại chi nhánh)
/// </summary>
public class Stocktake : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string Scope { get; set; } = "LOT"; // ALL, CATEGORY, PRODUCT, LOT
    public string ReasonType { get; set; } = "PERIODIC"; // PERIODIC, ANOMALY, OWNER_REQUEST
    public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING_APPROVAL, APPROVED, REJECTED, NEEDS_RECOUNT, CANCELLED

    public string? CreatedByName { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public string? Note { get; set; }

    // Navigation properties
    public virtual Branch? Branch { get; set; }
    public virtual ICollection<StocktakeLine> Lines { get; set; } = new List<StocktakeLine>();
}

/// <summary>
/// Bảng StocktakeLines (Chi tiết dòng sản phẩm / lô hàng được kiểm kê)
/// </summary>
public class StocktakeLine : BaseTenantEntity
{
    public Guid StocktakeId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? BranchStockId { get; set; }
    public string? BatchCode { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal UnitCost { get; set; } = 0m;

    /// <summary>
    /// Tồn hệ thống snapshot tại thời điểm lập phiếu kiểm kê
    /// </summary>
    public decimal SystemQty { get; set; } = 0m;

    /// <summary>
    /// 5 nhóm phân loại số lượng thực tế kiểm đếm
    /// </summary>
    public decimal GoodQty { get; set; } = 0m;
    public decimal DefectiveQty { get; set; } = 0m;
    public decimal DamagedQty { get; set; } = 0m;
    public decimal NearExpiryQty { get; set; } = 0m;
    public decimal ExpiredQty { get; set; } = 0m;

    /// <summary>
    /// Tổng thực tế = GoodQty + DefectiveQty + DamagedQty + NearExpiryQty + ExpiredQty
    /// </summary>
    public decimal ActualQty { get; set; } = 0m;

    /// <summary>
    /// Chênh lệch = ActualQty - SystemQty (<0: MISSING_LOST, >0: SURPLUS_FOUND, 0: Khớp)
    /// </summary>
    public decimal VarianceQty { get; set; } = 0m;

    /// <summary>
    /// Mã lý do cho phần hàng lỗi NCC: DEFECTIVE_SUPPLIER, RECALLED, WRONG_SPEC_DISCOVERED, NEAR_EXPIRY_RETURN
    /// </summary>
    public string? DefectiveReasonCode { get; set; }

    /// <summary>
    /// Hướng xử lý cho phần hàng cận hạn: NEAR_EXPIRY_MARKDOWN, NEAR_EXPIRY_RETURN
    /// </summary>
    public string? NearExpiryAction { get; set; }

    /// <summary>
    /// Số lượng hàng lỗi/cận hạn đủ điều kiện trả NCC đã được chuyển sang tạo Phiếu trả hàng
    /// </summary>
    public decimal ReturnedQty { get; set; } = 0m;

    /// <summary>
    /// Thông tin truy vết GRN gốc & NCC (nếu xác định được từ lô)
    /// </summary>
    public Guid? OriginalGrnId { get; set; }
    public Guid? OriginalGrnLineId { get; set; }
    public string? OriginalGrnCode { get; set; }
    public Guid? SupplierId { get; set; }

    public string? EvidenceUrl { get; set; }
    public string? Note { get; set; }

    // Navigation properties
    public virtual Stocktake? Stocktake { get; set; }
    public virtual Product? Product { get; set; }
    public virtual BranchStock? BranchStock { get; set; }
}

