using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class CustomerDebtRepository : GenericRepository<Customer>, ICustomerDebtRepository
{
    public CustomerDebtRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Customer?> GetByIdReadOnlyAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);
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
                o.Status == "Completed" && !o.IsDeleted &&
                o.FinalAmount > 0)
            .Select(o => new { o.Id, o.OrderCode, o.FinalAmount, o.OrderDate }).ToListAsync(ct);

        var rawReceipts = await _context.CashbookEntries.AsNoTracking()
            .Where(e => (e.SourceType == "DEBT_REPAYMENT" || e.SourceType == "DEBT") &&
                e.SourceId == customerId && !e.IsDeleted &&
                e.EntryType != CashbookEntryType.Payment)
            .Select(e => new { e.Id, e.EntryCode, e.Amount, e.ReferenceCode,
                e.Description, e.OccurredAtUtc, e.AccountType, e.AccountId }).ToListAsync(ct);

        var receiptIds = rawReceipts.Select(r => r.Id).ToList();
        var reversedReceiptIds = receiptIds.Count == 0
            ? new HashSet<Guid>()
            : (await _context.CashbookEntries.AsNoTracking()
                .Where(r => !r.IsDeleted &&
                    (r.SourceType == "COMPENSATING" || r.SourceType == "REVERSAL") &&
                    ((r.SourceId.HasValue && receiptIds.Contains(r.SourceId.Value)) ||
                     (r.ReversedEntryId.HasValue && receiptIds.Contains(r.ReversedEntryId.Value))))
                .Select(r => new { r.SourceId, r.ReversedEntryId })
                .ToListAsync(ct))
                .SelectMany(r => new[] { r.SourceId, r.ReversedEntryId })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

        var receipts = rawReceipts.Where(r => !reversedReceiptIds.Contains(r.Id)).ToList();

        var returns = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.DebtOffsetAmount > 0 &&
                o.Status != "Cancelled" && !o.IsDeleted)
            .Select(o => new { o.Id, o.OrderCode, o.DebtOffsetAmount, o.OrderDate }).ToListAsync(ct);

        var chronological = orders
            .Select(o => (
                Id: o.Id,
                EntryCode: o.OrderCode,
                EntryType: "DebtIncurred",
                SortPriority: 0,
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
                SortPriority: 1,
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
                SortPriority: 1,
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
            .ThenBy(e => e.SortPriority)
            .ThenBy(e => e.EntryCode, StringComparer.Ordinal)
            .ToList();

        var totalIncreased = chronological.Where(x => x.Delta > 0).Sum(x => x.Amount);
        var totalDecreased = chronological.Where(x => x.Delta < 0).Sum(x => x.Amount);
        var currentBalance = totalIncreased - totalDecreased;
        var totalCount = chronological.Count;

        decimal running = 0m;
        var withBalances = new List<CustomerDebtLedgerItemResponse>(chronological.Count);
        foreach (var item in chronological)
        {
            var before = running;
            running += item.Delta;
            withBalances.Add(new CustomerDebtLedgerItemResponse(
                item.Id, item.EntryCode, customerId, item.EntryType, item.Amount,
                before, running, item.ReferenceCode, item.Description, item.OccurredAtUtc,
                item.AccountId, item.PaymentMethod, item.CashbookEntryId,
                TotalIncreased: totalIncreased,
                TotalDecreased: totalDecreased,
                CurrentBalance: currentBalance,
                TotalCount: totalCount));
        }

        withBalances.Reverse();
        return withBalances
            .Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToList();
    }
}
