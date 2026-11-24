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
                (o.PaymentMethod == "DEBT" || o.PaymentMethod == "CREDIT") &&
                o.Status != "Cancelled" &&
                o.FinalAmount > 0)
            .Select(o => new { o.Id, o.OrderCode, o.FinalAmount, o.OrderDate }).ToListAsync(ct);
        var receipts = await _context.CashbookEntries.AsNoTracking()
            .Where(e => e.SourceType == "DEBT_REPAYMENT" && e.SourceId == customerId)
            .Select(e => new { e.Id, e.EntryCode, e.Amount, e.ReferenceCode,
                e.Description, e.OccurredAtUtc, e.AccountType, e.AccountId }).ToListAsync(ct);
        var returns = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.DebtOffsetAmount > 0 && o.Status != "Cancelled")
            .Select(o => new { o.Id, o.OrderCode, o.DebtOffsetAmount, o.OrderDate }).ToListAsync(ct);

        var chronological = orders
            .Select(o => (
                Id: o.Id,
                EntryCode: o.OrderCode,
                EntryType: "DebtIncurred",
                Amount: o.FinalAmount,
                Delta: o.FinalAmount,
                ReferenceCode: o.OrderCode,
                Description: "Bán nợ",
                OccurredAtUtc: o.OrderDate,
                AccountId: (Guid?)null,
                PaymentMethod: (string?)null,
                CashbookEntryId: (Guid?)null
            ))
            .Concat(receipts.Select(e => (
                Id: e.Id,
                EntryCode: e.EntryCode,
                EntryType: "DebtRepayment",
                Amount: e.Amount,
                Delta: -e.Amount,
                ReferenceCode: e.ReferenceCode,
                Description: e.Description,
                OccurredAtUtc: e.OccurredAtUtc,
                AccountId: (Guid?)e.AccountId,
                PaymentMethod: (string?)e.AccountType.ToString(),
                CashbookEntryId: (Guid?)e.Id
            )))
            .Concat(returns.Select(o => (
                Id: Guid.NewGuid(),
                EntryCode: $"{o.OrderCode}-RET",
                EntryType: "GoodsReturnOffset",
                Amount: o.DebtOffsetAmount,
                Delta: -o.DebtOffsetAmount,
                ReferenceCode: o.OrderCode,
                Description: "Trả hàng trừ công nợ",
                OccurredAtUtc: o.OrderDate,
                AccountId: (Guid?)null,
                PaymentMethod: (string?)null,
                CashbookEntryId: (Guid?)null
            )))
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();

        decimal running = 0m;
        var withBalances = new List<CustomerDebtLedgerItemResponse>(chronological.Count);
        foreach (var item in chronological)
        {
            var before = running;
            running = Math.Max(0m, running + item.Delta);
            withBalances.Add(new CustomerDebtLedgerItemResponse(
                item.Id, item.EntryCode, customerId, item.EntryType, item.Amount,
                before, running, item.ReferenceCode, item.Description, item.OccurredAtUtc,
                item.AccountId, item.PaymentMethod, item.CashbookEntryId));
        }

        withBalances.Reverse();
        return withBalances
            .Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToList();
    }
}
