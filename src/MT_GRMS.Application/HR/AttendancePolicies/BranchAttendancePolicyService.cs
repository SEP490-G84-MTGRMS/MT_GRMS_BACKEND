using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.AttendancePolicies;

public class BranchAttendancePolicyService : IBranchAttendancePolicyService
{
    private const string DefaultConversionRulesJson =
        "[{\"id\":\"rule-1\",\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5,\"workUnitsLabel\":\"nửa công\"},{\"id\":\"rule-2\",\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1,\"workUnitsLabel\":\"1 công\"}]";

    private readonly IBranchAttendancePolicyRepository _repository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public BranchAttendancePolicyService(
        IBranchAttendancePolicyRepository repository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<AttendancePolicyResponse> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
        if (branch == null)
        {
            throw new NotFoundException("Branch", branchId);
        }

        var policy = await _repository.GetByBranchIdAsync(branchId, cancellationToken);
        if (policy == null)
        {
            // Trả về chính sách mặc định tiêu chuẩn nếu chi nhánh chưa từng lưu cấu hình riêng
            return new AttendancePolicyResponse(
                Id: Guid.Empty,
                BranchId: branch.Id,
                BranchName: branch.Name,
                StandardDayHours: 8.00m,
                IsConvertHoursToDays: true,
                ConversionRulesJson: DefaultConversionRulesJson,
                EnableLateThreshold: true,
                LateThresholdMinutes: 0,
                EnableEarlyLeaveThreshold: true,
                EarlyLeaveThresholdMinutes: 0,
                EnableOtBeforeShift: true,
                OtBeforeShiftMinutes: 0,
                EnableOtAfterShift: true,
                OtAfterShiftMinutes: 0,
                IsCustomized: false
            );
        }

        return MapToResponse(policy, branch.Name, isCustomized: true);
    }

    public async Task<AttendancePolicyResponse> UpsertPolicyAsync(
        Guid branchId,
        UpsertAttendancePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
        if (branch == null)
        {
            throw new NotFoundException("Branch", branchId);
        }

        var policy = await _repository.GetByBranchIdAsync(branchId, cancellationToken);

        if (policy == null)
        {
            policy = new BranchAttendancePolicy
            {
                TenantId = _tenantContext.CurrentTenantId,
                BranchId = branchId,
                StandardDayHours = request.StandardDayHours,
                IsConvertHoursToDays = request.IsConvertHoursToDays,
                ConversionRulesJson = string.IsNullOrWhiteSpace(request.ConversionRulesJson) ? DefaultConversionRulesJson : request.ConversionRulesJson,
                EnableLateThreshold = request.EnableLateThreshold,
                LateThresholdMinutes = request.LateThresholdMinutes,
                EnableEarlyLeaveThreshold = request.EnableEarlyLeaveThreshold,
                EarlyLeaveThresholdMinutes = request.EarlyLeaveThresholdMinutes,
                EnableOtBeforeShift = request.EnableOtBeforeShift,
                OtBeforeShiftMinutes = request.OtBeforeShiftMinutes,
                EnableOtAfterShift = request.EnableOtAfterShift,
                OtAfterShiftMinutes = request.OtAfterShiftMinutes
            };

            await _repository.AddAsync(policy, cancellationToken);
        }
        else
        {
            policy.StandardDayHours = request.StandardDayHours;
            policy.IsConvertHoursToDays = request.IsConvertHoursToDays;
            policy.ConversionRulesJson = string.IsNullOrWhiteSpace(request.ConversionRulesJson) ? DefaultConversionRulesJson : request.ConversionRulesJson;
            policy.EnableLateThreshold = request.EnableLateThreshold;
            policy.LateThresholdMinutes = request.LateThresholdMinutes;
            policy.EnableEarlyLeaveThreshold = request.EnableEarlyLeaveThreshold;
            policy.EarlyLeaveThresholdMinutes = request.EarlyLeaveThresholdMinutes;
            policy.EnableOtBeforeShift = request.EnableOtBeforeShift;
            policy.OtBeforeShiftMinutes = request.OtBeforeShiftMinutes;
            policy.EnableOtAfterShift = request.EnableOtAfterShift;
            policy.OtAfterShiftMinutes = request.OtAfterShiftMinutes;

            _repository.Update(policy);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(policy, branch.Name, isCustomized: true);
    }

    public async Task<CopyAttendancePolicyResultDto> CopyPolicyAsync(
        CopyAttendancePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceBranch = await _branchRepository.GetByIdAsync(request.SourceBranchId, cancellationToken);
        if (sourceBranch == null)
        {
            throw new NotFoundException("Branch", request.SourceBranchId);
        }

        var sourcePolicy = await _repository.GetByBranchIdAsync(request.SourceBranchId, cancellationToken);

        // Lấy thông số nguồn (nếu chưa có thì dùng mặc định)
        var standardDayHours = sourcePolicy?.StandardDayHours ?? 8.00m;
        var isConvertHoursToDays = sourcePolicy?.IsConvertHoursToDays ?? true;
        var conversionRulesJson = sourcePolicy?.ConversionRulesJson ?? DefaultConversionRulesJson;
        var enableLateThreshold = sourcePolicy?.EnableLateThreshold ?? true;
        var lateThresholdMinutes = sourcePolicy?.LateThresholdMinutes ?? 0;
        var enableEarlyLeaveThreshold = sourcePolicy?.EnableEarlyLeaveThreshold ?? true;
        var earlyLeaveThresholdMinutes = sourcePolicy?.EarlyLeaveThresholdMinutes ?? 0;
        var enableOtBeforeShift = sourcePolicy?.EnableOtBeforeShift ?? true;
        var otBeforeShiftMinutes = sourcePolicy?.OtBeforeShiftMinutes ?? 0;
        var enableOtAfterShift = sourcePolicy?.EnableOtAfterShift ?? true;
        var otAfterShiftMinutes = sourcePolicy?.OtAfterShiftMinutes ?? 0;

        var copiedTargetIds = new List<Guid>();

        foreach (var targetBranchId in request.TargetBranchIds.Distinct())
        {
            if (targetBranchId == request.SourceBranchId) continue;

            var targetBranch = await _branchRepository.GetByIdAsync(targetBranchId, cancellationToken);
            if (targetBranch == null)
            {
                continue; // Bỏ qua chi nhánh không tồn tại trong Tenant
            }

            var targetPolicy = await _repository.GetByBranchIdAsync(targetBranchId, cancellationToken);

            if (targetPolicy == null)
            {
                targetPolicy = new BranchAttendancePolicy
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    BranchId = targetBranchId,
                    StandardDayHours = standardDayHours,
                    IsConvertHoursToDays = isConvertHoursToDays,
                    ConversionRulesJson = conversionRulesJson,
                    EnableLateThreshold = enableLateThreshold,
                    LateThresholdMinutes = lateThresholdMinutes,
                    EnableEarlyLeaveThreshold = enableEarlyLeaveThreshold,
                    EarlyLeaveThresholdMinutes = earlyLeaveThresholdMinutes,
                    EnableOtBeforeShift = enableOtBeforeShift,
                    OtBeforeShiftMinutes = otBeforeShiftMinutes,
                    EnableOtAfterShift = enableOtAfterShift,
                    OtAfterShiftMinutes = otAfterShiftMinutes
                };

                await _repository.AddAsync(targetPolicy, cancellationToken);
            }
            else
            {
                targetPolicy.StandardDayHours = standardDayHours;
                targetPolicy.IsConvertHoursToDays = isConvertHoursToDays;
                targetPolicy.ConversionRulesJson = conversionRulesJson;
                targetPolicy.EnableLateThreshold = enableLateThreshold;
                targetPolicy.LateThresholdMinutes = lateThresholdMinutes;
                targetPolicy.EnableEarlyLeaveThreshold = enableEarlyLeaveThreshold;
                targetPolicy.EarlyLeaveThresholdMinutes = earlyLeaveThresholdMinutes;
                targetPolicy.EnableOtBeforeShift = enableOtBeforeShift;
                targetPolicy.OtBeforeShiftMinutes = otBeforeShiftMinutes;
                targetPolicy.EnableOtAfterShift = enableOtAfterShift;
                targetPolicy.OtAfterShiftMinutes = otAfterShiftMinutes;

                _repository.Update(targetPolicy);
            }

            copiedTargetIds.Add(targetBranchId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CopyAttendancePolicyResultDto(
            SourceBranchId: request.SourceBranchId,
            CopiedCount: copiedTargetIds.Count,
            TargetBranchIds: copiedTargetIds,
            Message: $"Đã sao chép chính sách từ {sourceBranch.Name} sang {copiedTargetIds.Count} chi nhánh thành công."
        );
    }

    private static AttendancePolicyResponse MapToResponse(BranchAttendancePolicy policy, string? branchName, bool isCustomized)
    {
        return new AttendancePolicyResponse(
            Id: policy.Id,
            BranchId: policy.BranchId,
            BranchName: branchName ?? policy.Branch?.Name,
            StandardDayHours: policy.StandardDayHours,
            IsConvertHoursToDays: policy.IsConvertHoursToDays,
            ConversionRulesJson: policy.ConversionRulesJson ?? DefaultConversionRulesJson,
            EnableLateThreshold: policy.EnableLateThreshold,
            LateThresholdMinutes: policy.LateThresholdMinutes,
            EnableEarlyLeaveThreshold: policy.EnableEarlyLeaveThreshold,
            EarlyLeaveThresholdMinutes: policy.EarlyLeaveThresholdMinutes,
            EnableOtBeforeShift: policy.EnableOtBeforeShift,
            OtBeforeShiftMinutes: policy.OtBeforeShiftMinutes,
            EnableOtAfterShift: policy.EnableOtAfterShift,
            OtAfterShiftMinutes: policy.OtAfterShiftMinutes,
            IsCustomized: isCustomized
        );
    }
}
