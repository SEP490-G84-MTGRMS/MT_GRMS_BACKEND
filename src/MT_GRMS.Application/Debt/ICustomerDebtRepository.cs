using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.Debt;

public interface ICustomerDebtRepository : IRepository<Customer>
{
    Task<Customer?> GetCustomerWithLockAsync(Guid customerId, CancellationToken ct = default);
    Task<List<CustomerDebtInvoice>> GetOpenInvoicesAsync(Guid customerId, CancellationToken ct = default);
    Task<CustomerDebtInvoice?> GetInvoiceByOrderIdAsync(Guid orderId, CancellationToken ct = default);
    Task<bool> HasOverdueInvoicesAsync(Guid customerId, DateTime asOfUtc, CancellationToken ct = default);
}
