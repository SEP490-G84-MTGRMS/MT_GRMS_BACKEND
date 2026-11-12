using MT_GRMS.Application.HR.AttendancePolicies.Dtos;

namespace MT_GRMS.Application.HR.AttendancePolicies;

public interface IBranchAttendancePolicyService
{
    Task<AttendancePolicyResponse> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<AttendancePolicyResponse> UpsertPolicyAsync(Guid branchId, UpsertAttendancePolicyRequest request, CancellationToken cancellationToken = default);
    Task<CopyAttendancePolicyResultDto> CopyPolicyAsync(CopyAttendancePolicyRequest request, CancellationToken cancellationToken = default);
}

