using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.GoodsReceipts.Dtos;

public class GoodsReceiptFilterQuery
{
    public Guid? BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Status { get; set; } // DRAFT, COMPLETED, CANCELLED
    public string? Keyword { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool? IsTodayDelivery { get; set; }
    public bool? HasMismatch { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class GoodsReceiptDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderCode { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT"; // DRAFT, COMPLETED, CANCELLED
    public DateTime ReceivedAtUtc { get; set; }
    public string? InvoiceNo { get; set; }
    public string ReceiverUserId { get; set; } = string.Empty;
    public string? ReceiverName { get; set; }
    public string? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public decimal SubTotal { get; set; }
    public decimal FreightFee { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatPercent { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string PaymentMethod { get; set; } = "AP";
    public DateTime? PaymentDueDate { get; set; }
    public int TotalItems { get; set; }
    public decimal TotalOrderedQty { get; set; }
    public decimal TotalReceivedQty { get; set; }
    public decimal TotalAcceptedQty { get; set; }
    public decimal TotalRejectedQty { get; set; }
    public decimal TotalShortageQty => Math.Max(0, TotalOrderedQty - TotalReceivedQty);
    public bool HasMismatch { get; set; }
    public string? Note { get; set; }
    public List<GoodsReceiptLineDto> Lines { get; set; } = new();
}

public class GoodsReceiptLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool HasBatches { get; set; }
    public Guid? PurchaseOrderDetailId { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal DeliveredQty { get; set; } // ReceivedQty
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal ShortageQty => Math.Max(0, OrderedQty - DeliveredQty);
    public decimal DifferenceQty => AcceptedQty - OrderedQty;
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
    public string? BatchCode { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysToExpiry => ExpiryDate.HasValue ? (int)(ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays : null;
    public bool IsNearExpiry => DaysToExpiry.HasValue && DaysToExpiry.Value <= 30;
    public decimal FreightAllocated { get; set; }
    public decimal DiscountAllocated { get; set; }
    public decimal VatAllocated { get; set; }
    public decimal LandedUnitCost { get; set; }
    public string QualityStatus { get; set; } = "ACCEPTED";
    public string? RejectionReason { get; set; } // DAMAGE, EXPIRED, NEAR_EXPIRY, WRONG_SPEC, WRONG_ITEM, QUALITY_ISSUE, OTHER
    public string? RejectionNote { get; set; }
    public string? Note { get; set; }
    public List<GoodsReceiptBatchLineDto> Batches { get; set; } = new();
}

public class GoodsReceiptBatchLineDto
{
    public Guid? Id { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal AcceptedQty { get; set; }
    public int DaysToExpiry => (int)(ExpiryDate.Date - DateTime.UtcNow.Date).TotalDays;
    public bool IsNearExpiry => DaysToExpiry <= 30;
}

public class CreateGoodsReceiptCommand
{
    public Guid PurchaseOrderId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public string? InvoiceNo { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public decimal FreightFee { get; set; } = 0;
    public decimal DiscountAmount { get; set; } = 0;
    public decimal VatPercent { get; set; } = 0;
    public string PaymentMethod { get; set; } = "AP";
    public DateTime? PaymentDueDate { get; set; }
    public string? Note { get; set; }
    public bool AutoComplete { get; set; } = false;
    public List<CreateGoodsReceiptLineCommand> Lines { get; set; } = new();
}

public class CreateGoodsReceiptLineCommand
{
    public Guid ProductId { get; set; }
    public Guid? PurchaseOrderDetailId { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; } = 0;
    public decimal UnitPrice { get; set; }
    public string? BatchCode { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string QualityStatus { get; set; } = "ACCEPTED";
    public string? RejectionReason { get; set; }
    public string? RejectionNote { get; set; }
    public string? Note { get; set; }
    public List<GoodsReceiptBatchLineDto>? Batches { get; set; }
}

public class CompleteGoodsReceiptCommand
{
    public string? Note { get; set; }
}

public class CancelGoodsReceiptCommand
{
    public string Reason { get; set; } = string.Empty;
}

public class PurchaseOrderLookupDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PoReceiptDetailsDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public List<PoReceiptLineItemDto> Items { get; set; } = new();
}

public class PoReceiptLineItemDto
{
    public Guid PurchaseOrderDetailId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool HasBatches { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
}
