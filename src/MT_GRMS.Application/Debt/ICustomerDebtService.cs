using MT_GRMS.Application.Debt.Dtos;

namespace MT_GRMS.Application.Debt;

public interface ICustomerDebtService
{
    Task<CustomerDebtInvoiceResponse> RecordSaleDebtAsync(RecordSaleDebtRequest request, CancellationToken ct = default);
    Task<RepaymentResultResponse> RepayDebtAsync(RepayDebtRequest request, CancellationToken ct = default);
    Task<CustomerDebtEntryResponse> OffsetGoodsReturnDebtAsync(OffsetGoodsReturnDebtRequest request, CancellationToken ct = default);
    Task<CustomerDebtEntryResponse> AdjustDebtAsync(AdjustDebtRequest request, CancellationToken ct = default);
    Task<CustomerDebtEntryResponse> WriteOffBadDebtAsync(WriteOffBadDebtRequest request, CancellationToken ct = default);
    Task<CustomerDebtProfileResponse> GetProfileAsync(Guid customerId, CancellationToken ct = default);
    Task<List<CustomerDebtEntryResponse>> GetLedgerAsync(Guid customerId, int page = 1, int pageSize = 50, CancellationToken ct = default);
}
