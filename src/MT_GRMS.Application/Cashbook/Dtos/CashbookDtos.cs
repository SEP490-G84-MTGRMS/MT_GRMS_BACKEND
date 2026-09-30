using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Cashbook.Dtos;

public record CashbookCreateRequest(
    Guid BranchId,
    CashbookType Type,
    string Category,
    decimal Amount,
    string ReferenceCode,
    string Description
);

public record CashbookTransactionResponse(
    Guid Id,
    string TransactionCode,
    Guid BranchId,
    CashbookType Type,
    string Category,
    decimal Amount,
    string ReferenceCode,
    string Description,
    DateTime CreatedAt
);
