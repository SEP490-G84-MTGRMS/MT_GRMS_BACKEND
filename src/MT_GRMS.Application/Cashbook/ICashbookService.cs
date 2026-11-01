using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Cashbook;

public interface ICashbookService
{
    Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default);
    Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default);
    Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default);
    Task<CashInTransitResponse> CreateDepositToBankAsync(DepositToBankRequest request, CancellationToken ct = default);
    Task<CashInTransitResponse> ConfirmBankDepositAsync(ConfirmBankDepositRequest request, CancellationToken ct = default);
    Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default);
    Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default);
    Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<BranchCashTill> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default);
    Task<BranchSafe> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default);
    Task<List<CashAccountResponse>> GetAccountsAsync(Guid? branchId = null, CancellationToken ct = default) =>
        Task.FromResult(new List<CashAccountResponse>());
    Task<CashbookSummaryResponse> GetSummaryAsync(Guid? branchId = null, CancellationToken ct = default) =>
        Task.FromResult(new CashbookSummaryResponse(0, 0, 0, 0, 0, 0, 0));
    Task<CashbookEntryResponse> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-REC", request.AccountId, "Till", null, "Receipt", request.Amount, request.Amount, "MANUAL", null, request.ReferenceCode ?? "", "", request.Description ?? "", DateTime.UtcNow));
    Task<CashbookEntryResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-PAY", request.AccountId, "Till", null, "Payment", request.Amount, 0, "MANUAL", null, request.ReferenceCode ?? "", "", request.Description ?? "", DateTime.UtcNow));
    Task<TransferFundResponse> TransferFundsAsync(TransferFundRequest request, CancellationToken ct = default) =>
        Task.FromResult(new TransferFundResponse(
            new CashbookEntryResponse(Guid.NewGuid(), "CB-TRF-OUT", request.FromAccountId, "Till", null, "Payment", request.Amount, 0, "INTERNAL_TRANSFER", null, "", "", request.Note ?? "", DateTime.UtcNow),
            new CashbookEntryResponse(Guid.NewGuid(), "CB-TRF-IN", request.ToAccountId, "Till", null, "Receipt", request.Amount, request.Amount, "INTERNAL_TRANSFER", null, "", "", request.Note ?? "", DateTime.UtcNow)
        ));
    Task<CashbookEntryResponse?> ReconcileCashAccountAsync(ReconcileAccountRequest request, CancellationToken ct = default) =>
        Task.FromResult<CashbookEntryResponse?>(null);
    Task<CashbookEntryResponse> VoidTransactionAsync(Guid entryId, VoidEntryRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-VOID", Guid.NewGuid(), "Till", null, "Payment", 0, 0, "COMPENSATING", entryId, "", "", request.Reason, DateTime.UtcNow));
}
