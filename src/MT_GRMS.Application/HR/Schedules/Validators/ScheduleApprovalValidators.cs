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

        When(x => x.RequestType == ScheduleRequestType.CheckOutIncident, () =>
        {
            RuleFor(x => x.RequestedCheckOutTime)
                .NotNull().WithMessage("Vui lòng cung cấp giờ ra thực tế cần giải trình.");
        });

        When(x => x.RequestedCheckInTime.HasValue && x.RequestedCheckOutTime.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.RequestedCheckOutTime!.Value >= x.RequestedCheckInTime!.Value)
                .WithMessage("Giờ ra thực tế không được nhỏ hơn giờ vào thực tế.");
        });

        When(x => x.EvidenceImageUrls != null, () =>
        {
            RuleFor(x => x.EvidenceImageUrls!)
                .Must(list => list.Count <= 8)
                .WithMessage("Chỉ được đính kèm tối đa 8 ảnh minh chứng.");
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
                .NotEmpty().WithMessage("Vui lòng cung cấp lý do từ chối yêu cầu.")
                .MaximumLength(500).WithMessage("Lý do từ chối không được vượt quá 500 ký tự.");
        });

        When(x => x.IsApproved && !string.IsNullOrWhiteSpace(x.ReviewNote), () =>
        {
            RuleFor(x => x.ReviewNote!)
                .MaximumLength(500).WithMessage("Ghi chú phê duyệt không được vượt quá 500 ký tự.");
        });

        When(x => x.ApprovedOvertimeMinutes.HasValue, () =>
        {
            RuleFor(x => x.ApprovedOvertimeMinutes!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Số phút OT phê duyệt không được âm.")
                .LessThanOrEqualTo(480).WithMessage("Số phút OT tối đa là 480 phút.");
        });
    }
}

