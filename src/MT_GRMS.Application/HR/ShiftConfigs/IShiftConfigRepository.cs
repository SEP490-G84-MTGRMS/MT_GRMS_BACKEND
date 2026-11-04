using System.Linq.Expressions;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Application.HR.ShiftConfigs;

public interface IShiftConfigRepository
{
    Task<ShiftConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftConfig>> GetByBranchIdAsync(Guid branchId, bool? isActive = null, CancellationToken cancellationToken = default);
    Task<ShiftConfig?> GetByCodeAsync(Guid branchId, string shiftCode, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Expression<Func<ShiftConfig, bool>> predicate, CancellationToken cancellationToken = default);
    Task AddAsync(ShiftConfig shiftConfig, CancellationToken cancellationToken = default);
    void Update(ShiftConfig shiftConfig);
    void Delete(ShiftConfig shiftConfig);
}

