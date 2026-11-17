using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Sales;
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

    public async Task<List<CustomerDebtLedgerItemResponse>> GetLedgerAsync(Guid customerId, int page, int pageSize, CancellationToken ct = default)
    {
        var orders = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId &&
                (o.PaymentMethod == "DEBT" || o.PaymentMethod == "CREDIT") && o.Status != "Cancelled")
            .Select(o => new { o.Id, o.OrderCode, o.FinalAmount, o.OrderDate }).ToListAsync(ct);
        var receipts = await _context.CashbookEntries.AsNoTracking()
            .Where(e => e.SourceType == "DEBT_REPAYMENT" && e.SourceId == customerId)
            .Select(e => new { e.Id, e.EntryCode, e.Amount, e.ReferenceCode,
                e.Description, e.OccurredAtUtc, e.AccountType, e.AccountId }).ToListAsync(ct);
        var returns = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.DebtOffsetAmount > 0)
            .Select(o => new { o.Id, o.OrderCode, o.DebtOffsetAmount, o.OrderDate }).ToListAsync(ct);
        var sales = orders.Select(o => new CustomerDebtLedgerItemResponse(o.Id, o.OrderCode,
            customerId, "DebtIncurred", o.FinalAmount, null, null, o.OrderCode,
            "Bán nợ", o.OrderDate, null, null, null));
        var payments = receipts.Select(e => new CustomerDebtLedgerItemResponse(e.Id, e.EntryCode,
            customerId, "DebtRepayment", e.Amount, null, null, e.ReferenceCode,
            e.Description, e.OccurredAtUtc, e.AccountId, e.AccountType.ToString(), e.Id));
        var returnItems = returns.Select(o => new CustomerDebtLedgerItemResponse(o.Id, o.OrderCode,
            customerId, "GoodsReturnOffset", o.DebtOffsetAmount, null, null, o.OrderCode,
            "Trả hàng", o.OrderDate, null, null, null));
        return sales.Concat(payments).Concat(returnItems).OrderByDescending(e => e.OccurredAtUtc)
            .Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200)).ToList();
    }
}
