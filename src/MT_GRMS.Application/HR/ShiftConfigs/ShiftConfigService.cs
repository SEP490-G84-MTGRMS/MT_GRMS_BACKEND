using System.Text.Json;
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

    public async Task<PagedResult<ShiftConfigResponse>> GetShiftConfigsPagedAsync(
        ShiftConfigQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _repository.GetPagedListAsync(filter, cancellationToken);

        return new PagedResult<ShiftConfigResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            Page = filter.Page > 0 ? filter.Page : 1,
            PageSize = filter.PageSize > 0 ? filter.PageSize : 20,
            TotalItems = totalCount
        };
    }

    public async Task<IReadOnlyList<ShiftConfigResponse>> GetShiftConfigsByBranchAsync(
        Guid? branchId,
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
        // 1. Phân giải danh sách Chi nhánh áp dụng
        var branchIds = request.BranchIds?.Where(g => g != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (branchIds.Count == 0 && request.BranchId.HasValue && request.BranchId.Value != Guid.Empty)
        {
            branchIds.Add(request.BranchId.Value);
        }
        var primaryBranchId = branchIds.FirstOrDefault();

        // 2. Tự sinh hoặc chuẩn hóa ShiftCode
        string normalizedCode;
        if (!string.IsNullOrWhiteSpace(request.ShiftCode))
        {
            normalizedCode = request.ShiftCode.Trim().ToUpper();
            var existing = await _repository.GetByCodeAsync(primaryBranchId, normalizedCode, cancellationToken);
            if (existing != null)
            {
                throw new BusinessException("DUPLICATE_SHIFT_CODE", $"Mã ca '{normalizedCode}' đã tồn tại trong chi nhánh này.");
            }
        }
        else
        {
            var baseCode = GenerateShiftCode(request.ShiftName);
            normalizedCode = baseCode;
            int counter = 1;
            while (await _repository.ExistsAsync(s => s.BranchId == primaryBranchId && s.ShiftCode == normalizedCode && !s.IsDeleted, cancellationToken))
            {
                counter++;
                normalizedCode = $"{baseCode}-{counter}";
            }
        }

        // 3. Tính toán DurationHours nếu không truyền
        var durationHours = request.DurationHours ?? CalculateDuration(request.StartTime, request.EndTime);

        // 4. Tính toán khung giờ check-in mặc định nếu không truyền
        var allowedCheckInStart = request.AllowedCheckInStart ?? NormalizeTime(request.StartTime - TimeSpan.FromMinutes(30));
        var allowedCheckInEnd = request.AllowedCheckInEnd ?? NormalizeTime(request.StartTime + TimeSpan.FromMinutes(60));

        // 5. Xác định check-in hôm sau
        var isCheckInNextDay = request.IsCheckInNextDay ?? (allowedCheckInEnd <= allowedCheckInStart || request.EndTime < request.StartTime);

        var shiftConfig = new ShiftConfig
        {
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = primaryBranchId,
            BranchIds = branchIds.Count > 0 ? JsonSerializer.Serialize(branchIds.Select(g => g.ToString())) : null,
            ShiftCode = normalizedCode,
            ShiftName = request.ShiftName.Trim(),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            DurationHours = durationHours,
            AllowedCheckInStart = allowedCheckInStart,
            AllowedCheckInEnd = allowedCheckInEnd,
            IsCheckInNextDay = isCheckInNextDay,
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
        var isCheckInNextDay = request.IsCheckInNextDay ?? (allowedCheckInEnd <= allowedCheckInStart || request.EndTime < request.StartTime);

        if (request.BranchIds != null && request.BranchIds.Count > 0)
        {
            var branchIds = request.BranchIds.Where(g => g != Guid.Empty).Distinct().ToList();
            shift.BranchIds = JsonSerializer.Serialize(branchIds.Select(g => g.ToString()));
            if (branchIds.Count > 0)
            {
                shift.BranchId = branchIds[0];
            }
        }
        else if (request.BranchId.HasValue && request.BranchId.Value != Guid.Empty)
        {
            shift.BranchId = request.BranchId.Value;
            shift.BranchIds = JsonSerializer.Serialize(new[] { request.BranchId.Value.ToString() });
        }

        if (!string.IsNullOrWhiteSpace(request.ShiftCode))
        {
            shift.ShiftCode = request.ShiftCode.Trim().ToUpper();
        }

        shift.ShiftName = request.ShiftName.Trim();
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.DurationHours = durationHours;
        shift.AllowedCheckInStart = allowedCheckInStart;
        shift.AllowedCheckInEnd = allowedCheckInEnd;
        shift.IsCheckInNextDay = isCheckInNextDay;
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

    private static string GenerateShiftCode(string shiftName)
    {
        if (string.IsNullOrWhiteSpace(shiftName))
        {
            return "CA-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        }

        var normalized = shiftName.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
                else if (c == ' ' || c == '-' || c == '_')
                {
                    if (sb.Length > 0 && sb[^1] != '-')
                    {
                        sb.Append('-');
                    }
                }
            }
        }
        var code = sb.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(code))
        {
            return "CA-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        }
        return code.StartsWith("CA-") ? code : $"CA-{code}";
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
        var branchIdsList = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(s.BranchIds))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(s.BranchIds);
                if (parsed != null)
                {
                    branchIdsList.AddRange(parsed.Select(str => Guid.TryParse(str, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty));
                }
            }
            catch
            {
                branchIdsList.AddRange(s.BranchIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(str => Guid.TryParse(str.Trim(), out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty));
            }
        }
        if (branchIdsList.Count == 0 && s.BranchId != Guid.Empty)
        {
            branchIdsList.Add(s.BranchId);
        }

        return new ShiftConfigResponse(
            s.Id,
            s.BranchId,
            branchIdsList,
            s.ShiftCode,
            s.ShiftName,
            s.StartTime,
            s.EndTime,
            s.DurationHours,
            s.AllowedCheckInStart,
            s.AllowedCheckInEnd,
            s.IsCheckInNextDay,
            s.Description,
            s.IsActive,
            s.CreatedAt,
            s.UpdatedAt,
            s.Branch?.Name,
            null
        );
    }
}
