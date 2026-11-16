using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Transfers;

public sealed record TransferLineRequest(Guid ProductId, decimal Quantity, string Unit, string? BatchCode, DateTime? ExpiryDate, decimal UnitPrice);
public sealed record CreateTransferRequest(Guid FromBranchId, Guid ToBranchId, bool DispatchNow, DateTime? ExpectedDeliveryDate, string? Note, IReadOnlyList<TransferLineRequest> Items, TransferOrigin Origin = TransferOrigin.OwnerAllocation, TransferPriority Priority = TransferPriority.Normal, Guid? RelatedOrderId = null);
public sealed record ReceiveTransferLineRequest(Guid DetailId, decimal ReceivedQuantity, decimal DamagedQuantity, string? DiscrepancyReason);
public sealed record ReceiveTransferRequest(string? Note, IReadOnlyList<ReceiveTransferLineRequest> Items);
public sealed record CancelTransferRequest(string Reason);
public sealed record ReturnTransferRequest(string Reason);
public sealed record TransferStatusHistoryDto(Guid Id, string? FromStatus, string ToStatus, string Action, Guid ActorUserId, Guid? ActorBranchId, string ActorRole, string? Reason, DateTime CreatedAt);
public sealed record TransferLineDto(Guid Id, Guid ProductId, string ProductCode, string ProductName, string Unit, decimal SentQuantity, decimal ReceivedQuantity, decimal DamagedQuantity, string? BatchCode, DateTime? ExpiryDate, decimal UnitPrice, decimal Amount, string? DiscrepancyReason);
public sealed record TransferDto(Guid Id, string TransferCode, Guid FromBranchId, string FromBranchName, Guid ToBranchId, string ToBranchName, string Status, string? CompletionResult, string Origin, string Priority, DateTime CreatedAt, DateTime? SubmittedAt, DateTime? DispatchedAt, DateTime? ReceivedAt, DateTime? ExpectedDeliveryDate, string CreatedBy, decimal TotalAmount, bool HasDiscrepancy, string? Note, string? DiscrepancyNote, string? CancelReason, IReadOnlyList<TransferLineDto> Items, IReadOnlyList<TransferStatusHistoryDto> History);
public sealed record TransferFilterParams(string? Search, Guid? FromBranchId, Guid? ToBranchId, TransferStatus? Status, DateTime? FromDate, DateTime? ToDate, int Page = 1, int PageSize = 20, IReadOnlyList<TransferStatus>? Statuses = null, string? DateField = null, string? ReceiveStatus = null);
public sealed record TransferStockBatchDto(Guid ProductId, string? BatchCode, DateTime? ExpiryDate, decimal AvailableQuantity);
