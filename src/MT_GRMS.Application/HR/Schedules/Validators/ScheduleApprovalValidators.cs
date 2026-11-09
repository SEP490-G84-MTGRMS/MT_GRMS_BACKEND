using FluentValidation;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules.Validators;

public class CreateScheduleApprovalRequestValidator : AbstractValidator<CreateScheduleApprovalRequestDto>
{
    public CreateScheduleApprovalRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Lý do yêu cầu không được để trống.")
            .MinimumLength(5).WithMessage("Lý do yêu cầu tối thiểu 5 ký tự.")
            .MaximumLength(500).WithMessage("Lý do yêu cầu tối đa 500 ký tự.");

        When(x => x.RequestType == ScheduleRequestType.Overtime, () =>
        {
            RuleFor(x => x.RequestedOvertimeMinutes)
                .NotNull().WithMessage("Số phút làm thêm giờ (OT) không được để trống.")
                .GreaterThanOrEqualTo(15).WithMessage("Số phút OT tối thiểu là 15 phút.")
                .LessThanOrEqualTo(480).WithMessage("Số phút OT tối đa là 480 phút (8 giờ).");
        });

        When(x => x.RequestType == ScheduleRequestType.ShiftSwap, () =>
        {
            RuleFor(x => x.TargetEmployeeId)
                .NotEmpty().WithMessage("Vui lòng chọn đồng nghiệp muốn đổi ca cùng.");

            When(x => x.SwapType == ShiftSwapType.CrossSwap, () =>
            {
                RuleFor(x => x.TargetScheduleId)
                    .NotEmpty().WithMessage("Vui lòng chọn ca của đồng nghiệp muốn đổi chéo.");
            });
        });

        When(x => x.RequestType == ScheduleRequestType.LeaveRequest, () =>
        {
            RuleFor(x => x.LeaveType)
                .NotNull().WithMessage("Vui lòng chọn loại nghỉ phép (phép năm, ốm, việc riêng...).");
        });

        When(x => x.RequestType == ScheduleRequestType.CheckInIncident, () =>
        {
            RuleFor(x => x)
                .Must(x => x.RequestedCheckInTime.HasValue || x.RequestedCheckOutTime.HasValue)
                .WithMessage("Vui lòng cung cấp giờ vào hoặc giờ ra thực tế cần giải trình.");
        });
    }
}

public class ReviewScheduleApprovalRequestValidator : AbstractValidator<ReviewScheduleApprovalRequestDto>
{
    public ReviewScheduleApprovalRequestValidator()
    {
        When(x => !x.IsApproved, () =>
        {
            RuleFor(x => x.ReviewNote)
                .NotEmpty().WithMessage("Vui lòng cung cấp lý do từ chối yêu cầu.");
        });

        When(x => x.ApprovedOvertimeMinutes.HasValue, () =>
        {
            RuleFor(x => x.ApprovedOvertimeMinutes!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Số phút OT phê duyệt không được âm.")
                .LessThanOrEqualTo(480).WithMessage("Số phút OT tối đa là 480 phút.");
        });
    }
}

