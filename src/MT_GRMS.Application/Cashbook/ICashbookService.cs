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
}