using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.ShiftConfigs;

public class ShiftConfigService : IShiftConfigService
{
    private readonly IShiftConfigRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public ShiftConfigService(
        IShiftConfigRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ShiftConfigResponse>> GetShiftConfigsByBranchAsync(
        Guid branchId,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var shifts = await _repository.GetByBranchIdAsync(branchId, isActive, cancellationToken);
        return shifts.Select(MapToResponse).ToList();
    }

    public async Task<ShiftConfigResponse> GetShiftConfigByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var shift = await _repository.GetByIdAsync(id, cancellationToken);
        if (shift == null)
        {
            throw new NotFoundException("ShiftConfig", id);
        }

        return MapToResponse(shift);
    }

    public async Task<ShiftConfigResponse> CreateShiftConfigAsync(
        CreateShiftConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = request.ShiftCode.Trim().ToUpper();

        // 1. Kiểm tra trùng lặp ShiftCode trong chi nhánh
        var existing = await _repository.GetByCodeAsync(request.BranchId, normalizedCode, cancellationToken);
        if (existing != null)
        {
            throw new BusinessException("DUPLICATE_SHIFT_CODE", $"Mã ca '{normalizedCode}' đã tồn tại trong chi nhánh này.");
        }

        // 2. Tính toán DurationHours nếu không truyền
        var durationHours = request.DurationHours ?? CalculateDuration(request.StartTime, request.EndTime);

        // 3. Tính toán khung giờ check-in mặc định nếu không truyền
        var allowedCheckInStart = request.AllowedCheckInStart ?? NormalizeTime(request.StartTime - TimeSpan.FromMinutes(30));
        var allowedCheckInEnd = request.AllowedCheckInEnd ?? NormalizeTime(request.StartTime + TimeSpan.FromMinutes(60));

        var shiftConfig = new ShiftConfig
        {
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = request.BranchId,
            ShiftCode = normalizedCode,
            ShiftName = request.ShiftName.Trim(),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            DurationHours = durationHours,
            AllowedCheckInStart = allowedCheckInStart,
            AllowedCheckInEnd = allowedCheckInEnd,
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(shiftConfig, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(shiftConfig);
    }

    public async Task<ShiftConfigResponse> UpdateShiftConfigAsync(
        Guid id,
        UpdateShiftConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await _repository.GetByIdAsync(id, cancellationToken);
        if (shift == null)
        {
            throw new NotFoundException("ShiftConfig", id);
        }

        var durationHours = request.DurationHours ?? CalculateDuration(request.StartTime, request.EndTime);
        var allowedCheckInStart = request.AllowedCheckInStart ?? NormalizeTime(request.StartTime - TimeSpan.FromMinutes(30));
        var allowedCheckInEnd = request.AllowedCheckInEnd ?? NormalizeTime(request.StartTime + TimeSpan.FromMinutes(60));

        shift.ShiftName = request.ShiftName.Trim();
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.DurationHours = durationHours;
        shift.AllowedCheckInStart = allowedCheckInStart;
        shift.AllowedCheckInEnd = allowedCheckInEnd;
        shift.Description = request.Description?.Trim();
        shift.IsActive = request.IsActive;
        shift.UpdatedAt = DateTime.UtcNow;

        _repository.Update(shift);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(shift);
    }

    public async Task<ShiftConfigResponse> UpdateShiftConfigStatusAsync(
        Guid id,
        UpdateShiftConfigStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await _repository.GetByIdAsync(id, cancellationToken);
        if (shift == null)
        {
            throw new NotFoundException("ShiftConfig", id);
        }

        shift.IsActive = request.IsActive;
        shift.UpdatedAt = DateTime.UtcNow;

        _repository.Update(shift);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(shift);
    }

    public async Task DeleteShiftConfigAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var shift = await _repository.GetByIdAsync(id, cancellationToken);
        if (shift == null)
        {
            throw new NotFoundException("ShiftConfig", id);
        }

        _repository.Delete(shift);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static decimal CalculateDuration(TimeSpan startTime, TimeSpan endTime)
    {
        TimeSpan diff;
        if (endTime >= startTime)
        {
            diff = endTime - startTime;
        }
        else
        {
            // Ca vắt đêm: qua ngày hôm sau
            diff = endTime + TimeSpan.FromHours(24) - startTime;
        }

        return Math.Round((decimal)diff.TotalHours, 2);
    }

    private static TimeSpan NormalizeTime(TimeSpan time)
    {
        var totalHours = time.TotalHours % 24;
        if (totalHours < 0) totalHours += 24;
        return TimeSpan.FromHours(totalHours);
    }

    private static ShiftConfigResponse MapToResponse(ShiftConfig s)
    {
        return new ShiftConfigResponse(
            s.Id,
            s.BranchId,
            s.ShiftCode,
            s.ShiftName,
            s.StartTime,
            s.EndTime,
            s.DurationHours,
            s.AllowedCheckInStart,
            s.AllowedCheckInEnd,
            s.Description,
            s.IsActive,
            s.CreatedAt,
            s.UpdatedAt
        );
    }
}

