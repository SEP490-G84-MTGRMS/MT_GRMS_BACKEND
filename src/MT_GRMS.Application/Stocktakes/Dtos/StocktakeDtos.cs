using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.Stocktakes.Dtos;

public record StocktakeFilterQuery
{
    public string? Keyword { get; init; }
    public Guid? BranchId { get; init; }
    public string? Status { get; init; } // ALL, DRAFT, PENDING_APPROVAL, APPROVED, REJECTED, NEEDS_RECOUNT, CANCELLED
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public record StocktakeListItemDto(
    Guid Id,
    string Code,
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string Scope,
    string ReasonType,
    string Status,
    string? CreatedByName,
    DateTime StartedAt,
    DateTime? SubmittedAt,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    int TotalLines,
    int AnomalyLines,
    int ReturnEligibleLines,
    string? Note,
    decimal TotalDefectiveQty = 0m,
    decimal TotalDamagedAndExpiredQty = 0m,
    decimal TotalVarianceQty = 0m
);

public record StocktakeLineDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    string CategoryName,
    string Unit,
    bool HasBatches,
    Guid? BranchStockId,
    string? BatchCode,
    DateTime? ManufactureDate,
    DateTime? ExpiryDate,
    decimal UnitCost,
    decimal SystemQty,
    decimal CurrentOnHandQty,
    decimal GoodQty,
    decimal DefectiveQty,
    decimal DamagedQty,
    decimal NearExpiryQty,
    decimal ExpiredQty,
    decimal ActualQty,
    decimal VarianceQty,
    string? DefectiveReasonCode,
    string? NearExpiryAction,
    string? DamagedAction,
    string? ExpiredAction,
    decimal ReturnedQty,
    decimal EligibleReturnQty,
    Guid? OriginalGrnId,
    Guid? OriginalGrnLineId,
    string? OriginalGrnCode,
    Guid? SupplierId,
    string? SupplierName,
    string? EvidenceUrl,
    string? Note
);

public record StocktakeDetailDto(
    Guid Id,
    string Code,
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string Scope,
    string ReasonType,
    string Status,
    string? CreatedByName,
    DateTime StartedAt,
    DateTime? SubmittedAt,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? ReviewNote,
    string? Note,
    IReadOnlyList<StocktakeLineDto> Lines
);

public record StocktakeLineInputDto(
    Guid? Id,
    Guid ProductId,
    Guid? BranchStockId,
    decimal GoodQty,
    decimal DefectiveQty,
    decimal DamagedQty,
    decimal NearExpiryQty,
    decimal ExpiredQty,
    string? DefectiveReasonCode,
    string? NearExpiryAction,
    string? EvidenceUrl,
    string? Note
);

public record CreateStocktakeCommand(
    Guid BranchId,
    string Scope, // ALL, CATEGORY, PRODUCT, LOT
    string ReasonType, // PERIODIC, ANOMALY, OWNER_REQUEST
    string? Note,
    List<Guid>? BranchStockIds,
    Guid? CategoryId,
    List<StocktakeLineInputDto>? Lines,
    bool AutoSubmit = false,
    string? CreatedByName = null
);

public record UpdateStocktakeCommand(
    string? ReasonType,
    string? Note,
    List<StocktakeLineInputDto> Lines,
    bool AutoSubmit = false
);

public record ReviewStocktakeLineActionDto(
    Guid StocktakeLineId,
    string? DamagedAction, // WRITE_OFF | RETURN_SUPPLIER
    string? ExpiredAction  // WRITE_OFF | RETURN_SUPPLIER
);

public record ReviewStocktakeCommand(
    string Decision, // APPROVE, REJECT, NEEDS_RECOUNT
    string? ReviewNote,
    string? ReviewedByName = null,
    /// <summary>
    /// RULE-03 Q7: bắt buộc true khi phiếu có dòng VarianceQty &gt; 0 (SURPLUS) mới được cộng tồn.
    /// </summary>
    bool ConfirmSurplusFound = false,
    /// <summary>
    /// Quyết định xử lý hư hỏng / hết hạn theo từng dòng trước khi trừ tồn hoặc giữ để trả NCC.
    /// </summary>
    List<ReviewStocktakeLineActionDto>? LineActions = null
);

public record CreateReturnFromStocktakeLineItemDto(
    Guid StocktakeLineId,
    Guid? GoodsReceiptId,
    Guid? GoodsReceiptLineId,
    decimal RequestedQty,
    string? Notes
);

public record CreateReturnFromStocktakeCommand(
    Guid GoodsReceiptId,
    string? ResolutionType, // REDUCE_PAYABLE, REFUND, EXCHANGE, OTHER
    string? ReasonNotes,
    string? AttachmentUrl,
    List<CreateReturnFromStocktakeLineItemDto> Lines,
    bool AutoSubmit = true
);

