using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.SupplierReturns.Dtos;

public record SupplierReturnFilterQuery
{
    public string? Keyword { get; init; }
    public Guid? BranchId { get; init; }
    public Guid? SupplierId { get; init; }
    public string? Status { get; init; } // ALL, DRAFT, PENDING_SUPPLIER, APPROVED, REJECTED, COMPLETED, CANCELLED
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public record SupplierReturnKpiSummaryDto(
    int TotalReturns,
    int PendingCount,
    int ApprovedCount,
    int CompletedCount,
    decimal TotalValue
);

public record SupplierReturnListItemDto(
    Guid Id,
    string Code,
    Guid GoodsReceiptId,
    string GoodsReceiptCode,
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string Status,
    string Reason,
    int ItemCount,
    decimal TotalRequestedQty,
    decimal TotalApprovedQty,
    decimal TotalHandedOverQty,
    decimal TotalEstimatedAmount,
    DateTime CreatedAt,
    DateTime? HandedOverAt
);

public record SupplierReturnLineDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    string Unit,
    Guid? GoodsReceiptLineId,
    string? BatchCode,
    DateTime? ExpiryDate,
    decimal ReceivedQty,
    decimal ReturnedQty,
    decimal PendingReturnQty,
    decimal AvailableReturnQty,
    decimal RequestedQty,
    decimal ApprovedQty,
    decimal RejectedQty,
    decimal HandedOverQty,
    decimal UnitCost,
    decimal LineTotal,
    string? RejectionReason,
    string? Notes
);

public record SupplierReturnDetailDto(
    Guid Id,
    string Code,
    Guid GoodsReceiptId,
    string GoodsReceiptCode,
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string Status,
    string Reason,
    string? ReasonNotes,
    string? SupplierResponseNote,
    string? CancellationReason,
    string? AttachmentUrl,
    decimal TotalRequestedQty,
    decimal TotalApprovedQty,
    decimal TotalHandedOverQty,
    decimal TotalEstimatedAmount,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? RespondedAt,
    DateTime? HandedOverAt,
    string? HandedOverBy,
    IReadOnlyList<SupplierReturnLineDto> Lines
);

public record EligibleGrnLookupDto(
    Guid Id,
    string Code,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    string BranchName,
    DateTime ReceivedDate,
    int LineCount
);

public record GrnReturnableLineDto(
    Guid GoodsReceiptLineId,
    Guid ProductId,
    string Sku,
    string ProductName,
    string Unit,
    string? BatchCode,
    DateTime? ExpiryDate,
    decimal ReceivedQty,
    decimal ReturnedQty,
    decimal PendingReturnQty,
    decimal AvailableReturnQty,
    decimal UnitCost
);

public record CreateSupplierReturnLineDto(
    Guid ProductId,
    Guid? GoodsReceiptLineId,
    string? BatchCode,
    DateTime? ExpiryDate,
    decimal RequestedQty,
    decimal UnitCost,
    string? Notes
);

public record CreateSupplierReturnCommand(
    Guid GoodsReceiptId,
    string Reason,
    string? ReasonNotes,
    string? AttachmentUrl,
    List<CreateSupplierReturnLineDto> Lines,
    bool AutoSubmit = false
);

public record SupplierResponseLineDto(
    Guid LineId,
    decimal ApprovedQty,
    decimal RejectedQty,
    string? RejectionReason
);

public record RecordSupplierResponseCommand(
    string ResultType, // APPROVED_FULL, APPROVED_PARTIAL, REJECTED
    string? SupplierResponseNote,
    List<SupplierResponseLineDto>? LineResponses
);

public record HandoverLineDto(
    Guid LineId,
    decimal HandedOverQty
);

public record CompleteHandoverCommand(
    string? HandedOverBy,
    string? Notes,
    List<HandoverLineDto>? Lines
);

public record CancelSupplierReturnCommand(
    string Reason
);
