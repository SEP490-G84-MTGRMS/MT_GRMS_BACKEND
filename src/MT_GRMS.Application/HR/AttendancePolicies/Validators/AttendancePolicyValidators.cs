using FluentValidation;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;

namespace MT_GRMS.Application.HR.AttendancePolicies.Validators;

public class UpsertAttendancePolicyRequestValidator : AbstractValidator<UpsertAttendancePolicyRequest>
{
    public UpsertAttendancePolicyRequestValidator()
    {
        RuleFor(x => x.StandardDayHours)
            .GreaterThan(0).WithMessage("Số giờ của 1 ngày công chuẩn phải lớn hơn 0.")
            .LessThanOrEqualTo(24).WithMessage("Số giờ của 1 ngày công chuẩn không được vượt quá 24 giờ.");

        RuleFor(x => x.LateThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Số phút tính đi muộn không được âm.")
            .LessThanOrEqualTo(240).WithMessage("Số phút tính đi muộn tối đa là 240 phút.");

        RuleFor(x => x.EarlyLeaveThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Số phút tính về sớm không được âm.")
            .LessThanOrEqualTo(240).WithMessage("Số phút tính về sớm tối đa là 240 phút.");
    }
}

public class CopyAttendancePolicyRequestValidator : AbstractValidator<CopyAttendancePolicyRequest>
{
    public CopyAttendancePolicyRequestValidator()
    {
        RuleFor(x => x.SourceBranchId)
            .NotEmpty().WithMessage("Chi nhánh nguồn (SourceBranchId) không được để trống.");

        RuleFor(x => x.TargetBranchIds)
            .NotNull().WithMessage("Danh sách chi nhánh đích không được null.")
            .Must(t => t != null && t.Count > 0).WithMessage("Phải chọn ít nhất 1 chi nhánh đích để sao chép.")
            .Must((req, targetIds) => targetIds == null || !targetIds.Contains(req.SourceBranchId))
            .WithMessage("Danh sách chi nhánh đích không được bao gồm chi nhánh nguồn.");
    }
}

