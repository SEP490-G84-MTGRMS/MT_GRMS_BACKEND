using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class CashbookRepository : GenericRepository<CashbookEntry>, ICashbookRepository
{
    public CashbookRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<FinanceAccount?> GetAccountWithLockAsync(Guid accountId, CancellationToken ct = default)
    {
        if (_context.Database.IsSqlServer())
        {
            return await _context.FinanceAccounts
                .FromSqlInterpolated($"SELECT * FROM FinanceAccounts WITH (UPDLOCK, ROWLOCK) WHERE Id = {accountId}")
                .SingleOrDefaultAsync(ct);
        }

        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _context.FinanceAccounts
                .FromSqlInterpolated($"SELECT * FROM \"FinanceAccounts\" WHERE \"Id\" = {accountId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
        }

        return await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
    }

    public async Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default)
    {
        var lastEntry = await _context.CashbookEntries
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return lastEntry?.AuditHash ?? "GENESIS_HASH";
    }

    public async Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default)
    {
        return await _context.ShiftZReports
            .Where(s => s.TillId == tillId && s.Status == Domain.Enums.ShiftState.Open)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default)
    {
        return await _context.ShiftZReports
            .Where(s => s.TillId == tillId && s.Status == Domain.Enums.ShiftState.Closed)
            .OrderByDescending(s => s.ClosedAtUtc)
            .FirstOrDefaultAsync(ct);
    }
}
