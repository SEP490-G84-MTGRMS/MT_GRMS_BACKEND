using MT_GRMS.Application.Debt.Dtos;

namespace MT_GRMS.Application.Debt;

public interface ICustomerDebtService
{
    Task<RepaymentResultResponse> RepayDebtAsync(RepayDebtRequest request, CancellationToken ct = default);
    Task<CustomerDebtProfileResponse> GetProfileAsync(Guid customerId, CancellationToken ct = default);
    Task<List<CustomerDebtLedgerItemResponse>> GetLedgerAsync(Guid customerId, int page = 1, int pageSize = 50, CancellationToken ct = default);
}
