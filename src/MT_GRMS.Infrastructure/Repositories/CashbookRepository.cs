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

    public async Task<BranchCashTill?> GetTillWithLockAsync(Guid tillId, CancellationToken ct = default)
    {
        if (_context.Database.IsSqlServer())
        {
            return await _context.BranchCashTills
                .FromSqlInterpolated($"SELECT * FROM BranchCashTills WITH (UPDLOCK, ROWLOCK) WHERE Id = {tillId}")
                .SingleOrDefaultAsync(ct);
        }

        return await _context.BranchCashTills.SingleOrDefaultAsync(t => t.Id == tillId, ct);
    }

    public async Task<BranchSafe?> GetSafeWithLockAsync(Guid safeId, CancellationToken ct = default)
    {
        if (_context.Database.IsSqlServer())
        {
            return await _context.BranchSafes
                .FromSqlInterpolated($"SELECT * FROM BranchSafes WITH (UPDLOCK, ROWLOCK) WHERE Id = {safeId}")
                .SingleOrDefaultAsync(ct);
        }

        return await _context.BranchSafes.SingleOrDefaultAsync(s => s.Id == safeId, ct);
    }

    public async Task<TenantBankAccount?> GetBankAccountWithLockAsync(Guid bankAccountId, CancellationToken ct = default)
    {
        if (_context.Database.IsSqlServer())
        {
            return await _context.TenantBankAccounts
                .FromSqlInterpolated($"SELECT * FROM TenantBankAccounts WITH (UPDLOCK, ROWLOCK) WHERE Id = {bankAccountId}")
                .SingleOrDefaultAsync(ct);
        }

        return await _context.TenantBankAccounts.SingleOrDefaultAsync(b => b.Id == bankAccountId, ct);
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
