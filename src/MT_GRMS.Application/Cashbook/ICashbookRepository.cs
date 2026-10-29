using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Cashbook;

public interface ICashbookRepository : IRepository<CashbookEntry>
{
    Task<BranchCashTill?> GetTillWithLockAsync(Guid tillId, CancellationToken ct = default);
    Task<BranchSafe?> GetSafeWithLockAsync(Guid safeId, CancellationToken ct = default);
    Task<TenantBankAccount?> GetBankAccountWithLockAsync(Guid bankAccountId, CancellationToken ct = default);
    Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default);
    Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default);
    Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default);
}