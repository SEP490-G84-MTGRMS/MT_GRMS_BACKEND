using MT_GRMS.Application.HR.ShiftConfigs.Dtos;

namespace MT_GRMS.Application.HR.ShiftConfigs;

public interface IShiftConfigService
{
    Task<IReadOnlyList<ShiftConfigResponse>> GetShiftConfigsByBranchAsync(Guid? branchId, bool? isActive = null, CancellationToken cancellationToken = default);
    Task<ShiftConfigResponse> GetShiftConfigByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ShiftConfigResponse> CreateShiftConfigAsync(CreateShiftConfigRequest request, CancellationToken cancellationToken = default);
    Task<ShiftConfigResponse> UpdateShiftConfigAsync(Guid id, UpdateShiftConfigRequest request, CancellationToken cancellationToken = default);
    Task<ShiftConfigResponse> UpdateShiftConfigStatusAsync(Guid id, UpdateShiftConfigStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteShiftConfigAsync(Guid id, CancellationToken cancellationToken = default);
}

