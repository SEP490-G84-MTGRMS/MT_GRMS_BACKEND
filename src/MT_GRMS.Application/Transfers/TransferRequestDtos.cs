using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Transfers;

public sealed record TransferDemandLineRequest(Guid ProductId, decimal Quantity, string Unit);
public sealed record CreateTransferRequestGroupRequest(
    Guid ToBranchId,
    DateTime? RequiredAt,
    Guid? RelatedOrderId,
    string? Note,
    IReadOnlyList<TransferDemandLineRequest> Items,
    string? RelatedOrderCode = null);

public sealed record TransferAvailableProductDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string BaseUnit,
    decimal TotalAvailableQuantity);

public sealed record TransferAllocationLineDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal ShortageQuantity,
    string Unit,
    decimal ReceivedQuantity = 0);

public sealed record TransferAllocationSourceDto(
    Guid FromBranchId,
    string FromBranchName,
    IReadOnlyList<TransferAllocationLineDto> Items);

public sealed record TransferAllocationPreviewDto(
    Guid ToBranchId,
    bool IsFullyAllocated,
    IReadOnlyList<TransferAllocationLineDto> Totals,
    IReadOnlyList<TransferAllocationSourceDto> Sources);

public sealed record TransferRequestGroupDto(
    Guid Id,
    string RequestCode,
    Guid ToBranchId,
    string Status,
    string Priority,
    DateTime CreatedAt,
    DateTime? RequiredAt,
    Guid? RelatedOrderId,
    string? Note,
    IReadOnlyList<TransferAllocationLineDto> Items,
    IReadOnlyList<TransferDto> Transfers);
