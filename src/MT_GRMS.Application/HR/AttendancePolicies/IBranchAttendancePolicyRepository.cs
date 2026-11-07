using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Application.HR.AttendancePolicies;

public interface IBranchAttendancePolicyRepository
{
    Task<BranchAttendancePolicy?> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BranchAttendancePolicy>> GetByBranchIdsAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default);
    Task AddAsync(BranchAttendancePolicy policy, CancellationToken cancellationToken = default);
    void Update(BranchAttendancePolicy policy);
}

