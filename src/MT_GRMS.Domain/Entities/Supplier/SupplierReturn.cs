using System;
using System.Collections.Generic;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng SupplierReturns (Phiếu yêu cầu trả hàng nhà cung cấp - RTV)
/// </summary>
public class SupplierReturn : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid GoodsReceiptId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? StocktakeId { get; set; }
    public string? StocktakeCode { get; set; }
    public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING_SUPPLIER, APPROVED, REJECTED, PARTIALLY_HANDED_OVER, COMPLETED, CANCELLED
    public string Reason { get; set; } = string.Empty; // Sắp hết hạn, Hàng hư hỏng lỗi chất lượng, Sai quy cách, Thỏa thuận thu hồi, Khác
    public string? ReasonNotes { get; set; }
    public string? SupplierResponseNote { get; set; }
    public string? CancellationReason { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? ResolutionType { get; set; } = "REDUCE_PAYABLE"; // REDUCE_PAYABLE, REFUND, EXCHANGE, OTHER
    public string? SettlementStatus { get; set; } = "UNSETTLED"; // NOT_REQUIRED, UNPAID, PARTIALLY_PAID, PAID (legacy: UNSETTLED/SETTLED)
    /// <summary>NOT_REQUIRED | WAITING_HANDOVER | WAITING_PURCHASE_ORDER | WAITING_RECEIPT | PARTIALLY_RECEIVED | COMPLETED | CANCELLED</summary>
    public string ReplacementStatus { get; set; } = "NOT_REQUIRED";

    public Guid? PolicyId { get; set; }
    public string? PolicySnapshotJson { get; set; }
    public decimal? StoreCostSharePercentSnapshot { get; set; }
    public decimal? SupplierCostSharePercentSnapshot { get; set; }
    public decimal ReplacementValue { get; set; } = 0;
    public decimal StorePayableAmount { get; set; } = 0;
    public decimal StorePaidAmount { get; set; } = 0;
    public decimal SupplierBearAmount { get; set; } = 0;

    public decimal TotalRequestedQty { get; set; } = 0;
    public decimal TotalApprovedQty { get; set; } = 0;
    public decimal TotalHandedOverQty { get; set; } = 0;
    public decimal TotalEstimatedAmount { get; set; } = 0;

    public DateTime? SubmittedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? SupplierConfirmedBy { get; set; }
    public DateTime? HandedOverAt { get; set; }
    public string? HandedOverBy { get; set; }
    public string? HandoverNote { get; set; }

    // Navigation properties
    public virtual GoodsReceipt? GoodsReceipt { get; set; }
    public virtual Supplier? Supplier { get; set; }
    public virtual Branch? Branch { get; set; }
    public virtual ICollection<SupplierReturnLine> Lines { get; set; } = new List<SupplierReturnLine>();
}

/// <summary>
/// Bảng SupplierReturnLines (Chi tiết mặt hàng và lô hàng cần trả)
/// </summary>
public class SupplierReturnLine : BaseTenantEntity
{
    public Guid SupplierReturnId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? GoodsReceiptLineId { get; set; }
    public Guid? StocktakeLineId { get; set; }
    public string? BatchCode { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal RequestedQty { get; set; } = 0;
    public decimal SupplierAcceptedQty { get; set; } = 0;
    public decimal ApprovedQty { get; set; } = 0;
    public decimal RejectedQty { get; set; } = 0;
    public decimal HandedOverQty { get; set; } = 0;
    /// <summary>Qty đã gắn vào PO đổi hàng (chưa hủy).</summary>
    public decimal ExchangePoOrderedQty { get; set; } = 0;

    public decimal UnitCost { get; set; } = 0;
    public decimal ReplacementUnitPrice { get; set; } = 0;
    public decimal LineTotal => RequestedQty * UnitCost;
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }

    // Navigation properties
    public virtual SupplierReturn? SupplierReturn { get; set; }
    public virtual Product? Product { get; set; }
}
