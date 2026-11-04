using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Cashbook;

public interface ICashbookRepository : IRepository<CashbookEntry>
{
    Task<FinanceAccount?> GetAccountWithLockAsync(Guid accountId, CancellationToken ct = default);
    Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default);
    Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default);
    Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default);
}
