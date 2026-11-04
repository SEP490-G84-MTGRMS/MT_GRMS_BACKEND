using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Debt;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class CustomerDebtRepository : GenericRepository<Customer>, ICustomerDebtRepository
{
    public CustomerDebtRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Customer?> GetCustomerWithLockAsync(Guid customerId, CancellationToken ct = default)
    {
        if (_context.Database.IsSqlServer())
        {
            return await _context.Customers
                .FromSqlInterpolated($"SELECT * FROM Customers WITH (UPDLOCK, ROWLOCK) WHERE CustomerID = {customerId}")
                .SingleOrDefaultAsync(ct);
        }

        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _context.Customers
                .FromSqlInterpolated($"SELECT * FROM \"Customers\" WHERE \"CustomerID\" = {customerId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
        }

        return await _context.Customers.SingleOrDefaultAsync(p => p.Id == customerId, ct);
    }

    public async Task<List<CustomerDebtInvoice>> GetOpenInvoicesAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _context.CustomerDebtInvoices
            .Where(i => i.CustomerId == customerId && (i.Status == DebtInvoiceStatus.Unpaid || i.Status == DebtInvoiceStatus.PartiallyPaid))
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<CustomerDebtInvoice?> GetInvoiceByOrderIdAsync(Guid orderId, CancellationToken ct = default)
    {
        return await _context.CustomerDebtInvoices
            .SingleOrDefaultAsync(i => i.OrderId == orderId, ct);
    }

    public async Task<bool> HasOverdueInvoicesAsync(Guid customerId, DateTime asOfUtc, CancellationToken ct = default)
    {
        return await _context.CustomerDebtInvoices
            .AnyAsync(i => i.CustomerId == customerId &&
                           (i.Status == DebtInvoiceStatus.Unpaid || i.Status == DebtInvoiceStatus.PartiallyPaid) &&
                           i.DueDate < asOfUtc, ct);
    }
}
