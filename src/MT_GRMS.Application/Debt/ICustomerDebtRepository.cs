using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.Debt;

public interface ICustomerDebtRepository : IRepository<Customer>
{
    Task<Customer?> GetByIdReadOnlyAsync(Guid customerId, CancellationToken ct = default);
    Task<Customer?> GetCustomerWithLockAsync(Guid customerId, CancellationToken ct = default);
    Task<List<Dtos.CustomerDebtLedgerItemResponse>> GetLedgerAsync(Guid customerId, int page, int pageSize, CancellationToken ct = default);
}
