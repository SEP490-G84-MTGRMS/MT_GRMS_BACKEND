using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.AttendancePolicies;

public class BranchAttendancePolicyService : IBranchAttendancePolicyService
{
    private const string DefaultConversionRulesJson =
        "[{\"id\":\"rule-1\",\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5,\"workUnitsLabel\":\"nửa công\"},{\"id\":\"rule-2\",\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1,\"workUnitsLabel\":\"1 công\"}]";

    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BranchAttendancePolicyService(
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork)
    {
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<AttendancePolicyResponse> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
        if (branch == null)
        {
            throw new NotFoundException("Branch", branchId);
        }

        if (!branch.IsAttendancePolicyCustomized)
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
                IsCustomized: false
            );
        }

        return MapToResponse(branch);
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

        branch.StandardDayHours = request.StandardDayHours;
        branch.IsConvertHoursToDays = request.IsConvertHoursToDays;
        branch.ConversionRulesJson = string.IsNullOrWhiteSpace(request.ConversionRulesJson)
            ? DefaultConversionRulesJson
            : request.ConversionRulesJson;
        branch.EnableLateThreshold = request.EnableLateThreshold;
        branch.LateThresholdMinutes = request.LateThresholdMinutes;
        branch.EnableEarlyLeaveThreshold = request.EnableEarlyLeaveThreshold;
        branch.EarlyLeaveThresholdMinutes = request.EarlyLeaveThresholdMinutes;
        branch.IsAttendancePolicyCustomized = true;

        _branchRepository.Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(branch);
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

        // Lấy thông số nguồn (nếu chưa cấu hình riêng thì dùng mặc định)
        var standardDayHours = sourceBranch.IsAttendancePolicyCustomized ? sourceBranch.StandardDayHours : 8.00m;
        var isConvertHoursToDays = !sourceBranch.IsAttendancePolicyCustomized || sourceBranch.IsConvertHoursToDays;
        var conversionRulesJson = sourceBranch.IsAttendancePolicyCustomized && !string.IsNullOrWhiteSpace(sourceBranch.ConversionRulesJson)
            ? sourceBranch.ConversionRulesJson
            : DefaultConversionRulesJson;
        var enableLateThreshold = !sourceBranch.IsAttendancePolicyCustomized || sourceBranch.EnableLateThreshold;
        var lateThresholdMinutes = sourceBranch.IsAttendancePolicyCustomized ? sourceBranch.LateThresholdMinutes : 0;
        var enableEarlyLeaveThreshold = !sourceBranch.IsAttendancePolicyCustomized || sourceBranch.EnableEarlyLeaveThreshold;
        var earlyLeaveThresholdMinutes = sourceBranch.IsAttendancePolicyCustomized ? sourceBranch.EarlyLeaveThresholdMinutes : 0;

        var copiedTargetIds = new List<Guid>();

        foreach (var targetBranchId in request.TargetBranchIds.Distinct())
        {
            if (targetBranchId == request.SourceBranchId) continue;

            var targetBranch = await _branchRepository.GetByIdAsync(targetBranchId, cancellationToken);
            if (targetBranch == null)
            {
                continue; // Bỏ qua chi nhánh không tồn tại trong Tenant
            }

            targetBranch.StandardDayHours = standardDayHours;
            targetBranch.IsConvertHoursToDays = isConvertHoursToDays;
            targetBranch.ConversionRulesJson = conversionRulesJson;
            targetBranch.EnableLateThreshold = enableLateThreshold;
            targetBranch.LateThresholdMinutes = lateThresholdMinutes;
            targetBranch.EnableEarlyLeaveThreshold = enableEarlyLeaveThreshold;
            targetBranch.EarlyLeaveThresholdMinutes = earlyLeaveThresholdMinutes;
            targetBranch.IsAttendancePolicyCustomized = true;

            _branchRepository.Update(targetBranch);
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

    private static AttendancePolicyResponse MapToResponse(Branch branch)
    {
        return new AttendancePolicyResponse(
            Id: branch.Id,
            BranchId: branch.Id,
            BranchName: branch.Name,
            StandardDayHours: branch.StandardDayHours,
            IsConvertHoursToDays: branch.IsConvertHoursToDays,
            ConversionRulesJson: branch.ConversionRulesJson ?? DefaultConversionRulesJson,
            EnableLateThreshold: branch.EnableLateThreshold,
            LateThresholdMinutes: branch.LateThresholdMinutes,
            EnableEarlyLeaveThreshold: branch.EnableEarlyLeaveThreshold,
            EarlyLeaveThresholdMinutes: branch.EarlyLeaveThresholdMinutes,
            IsCustomized: branch.IsAttendancePolicyCustomized
        );
    }
}
