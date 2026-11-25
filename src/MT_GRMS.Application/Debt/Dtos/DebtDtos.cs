using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Debt.Dtos;

public record RepayDebtRequest(
    Guid CustomerId,
    decimal Amount,
    FinanceAccountType PaymentSourceType,
    Guid PaymentSourceAccountId,
    string? ReferenceCode,
    string? Notes
);

public record CustomerDebtProfileResponse(
    Guid CustomerId,
    decimal CurrentDebtBalance,
    DateTime? LastDebtIncurredAtUtc,
    DateTime? LastRepaymentAtUtc
);

public record CustomerDebtLedgerItemResponse(
    Guid Id,
    string EntryCode,
    Guid CustomerId,
    string EntryType,
    decimal Amount,
    decimal? BalanceBefore,
    decimal? BalanceAfter,
    string ReferenceCode,
    string Description,
    DateTime OccurredAtUtc,
    Guid? PaymentAccountId,
    string? PaymentMethod,
    Guid? CashbookEntryId,
    decimal? TotalIncreased = null,
    decimal? TotalDecreased = null,
    decimal? CurrentBalance = null,
    int? TotalCount = null
);

public record RepaymentResultResponse(
    Guid DebtEntryId,
    string EntryCode,
    decimal RepaidAmount,
    decimal BalanceBefore,
    decimal RemainingDebt,
    Guid? CashbookEntryId
);

public record CustomerResponse(
    Guid Id,
    string CustomerCode,
    string FullName,
    string PhoneNumber,
    string? Email,
    string? Address,
    string MembershipTier,
    int LoyaltyPoints,
    decimal TotalSpending,
    decimal CurrentDebtAR,
    string Status,
    DateTime CreatedAt,
    List<CustomerInvoiceSummaryResponse>? Invoices = null,
    List<CustomerPaymentSummaryResponse>? Payments = null,
    int DebtOrderCount = 0,
    decimal TotalDebtPurchased = 0m
);

public record CustomerInvoiceSummaryResponse(
    string Id,
    string Date,
    decimal Amount,
    string Status
);

public record CustomerPaymentSummaryResponse(
    string Id,
    string Date,
    decimal Amount,
    string Method,
    string? StaffName,
    string? Note
);

public record CustomerKpiSummaryResponse(
    int TotalCustomers,
    decimal TotalDebtAR,
    int DebtorCount,
    int TotalLoyaltyPoints
);

public record CreateCustomerRequest(
    string PhoneNumber,
    string FullName,
    string? Email,
    string? Address,
    string? MembershipTier
);

public record UpdateCustomerRequest(
    string? FullName,
    string? Email,
    string? Address,
    string? MembershipTier
);
