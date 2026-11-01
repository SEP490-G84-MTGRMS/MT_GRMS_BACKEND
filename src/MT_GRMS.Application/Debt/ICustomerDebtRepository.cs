using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Debt;

public interface ICustomerDebtRepository : IRepository<CustomerDebtProfile>
{
    Task<CustomerDebtProfile?> GetProfileWithLockAsync(Guid customerId, CancellationToken ct = default);
    Task<List<CustomerDebtInvoice>> GetOpenInvoicesAsync(Guid customerId, CancellationToken ct = default);
    Task<CustomerDebtInvoice?> GetInvoiceByOrderIdAsync(Guid orderId, CancellationToken ct = default);
    Task<bool> HasOverdueInvoicesAsync(Guid customerId, DateTime asOfUtc, CancellationToken ct = default);
}
