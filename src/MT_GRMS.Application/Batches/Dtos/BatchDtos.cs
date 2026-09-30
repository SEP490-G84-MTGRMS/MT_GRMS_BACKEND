namespace MT_GRMS.Application.Batches.Dtos;

public record BatchCreateRequest(
    Guid ProductId,
    Guid BranchId,
    string BatchCode,
    DateTime ManufactureDate,
    DateTime ExpiryDate,
    decimal InitialQuantity,
    decimal LandedCostPerUnit
);

public record BatchResponse(
    Guid Id,
    Guid ProductId,
    Guid BranchId,
    string BatchCode,
    DateTime ManufactureDate,
    DateTime ExpiryDate,
    decimal InitialQuantity,
    decimal LandedCostPerUnit,
    int DaysUntilExpiry,
    bool IsExpired
);

public record ExpiryAlertDto(
    Guid BatchId,
    string BatchCode,
    string ProductName,
    DateTime ExpiryDate,
    int DaysRemaining,
    decimal RemainingQuantity
);
