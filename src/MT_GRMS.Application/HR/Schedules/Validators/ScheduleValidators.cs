using FluentValidation;
using MT_GRMS.Application.HR.Schedules.Dtos;

namespace MT_GRMS.Application.HR.Schedules.Validators;

public class CreateScheduleItemRequestValidator : AbstractValidator<CreateScheduleItemRequest>
{
    public CreateScheduleItemRequestValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Nhân viên không được để trống.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh không được để trống.");

        RuleFor(x => x.WorkDate)
            .Must(d => d != default).WithMessage("Ngày làm việc không hợp lệ.");

        When(x => !x.ShiftConfigId.HasValue, () =>
        {
            RuleFor(x => x.ShiftName)
                .NotEmpty().WithMessage("Tên ca không được để trống khi không chọn ca mẫu.")
                .MaximumLength(100).WithMessage("Tên ca tối đa 100 ký tự.");

            RuleFor(x => x.PlannedStartTime)
                .NotNull().WithMessage("Giờ bắt đầu ca không được để trống khi không chọn ca mẫu.");

            RuleFor(x => x.PlannedEndTime)
                .NotNull().WithMessage("Giờ kết thúc ca không được để trống khi không chọn ca mẫu.");
        });

        When(x => x.PlannedStartTime.HasValue, () =>
        {
            RuleFor(x => x.PlannedStartTime!.Value)
                .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                .WithMessage("Giờ bắt đầu ca không hợp lệ.");
        });

        When(x => x.PlannedEndTime.HasValue, () =>
        {
            RuleFor(x => x.PlannedEndTime!.Value)
                .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                .WithMessage("Giờ kết thúc ca không hợp lệ.");
        });

        When(x => x.DurationHours.HasValue, () =>
        {
            RuleFor(x => x.DurationHours!.Value)
                .GreaterThan(0).WithMessage("Thời lượng ca phải lớn hơn 0.")
                .LessThanOrEqualTo(24).WithMessage("Thời lượng ca không được vượt quá 24 giờ.");
        });
    }
}

public class BatchCreateScheduleRequestValidator : AbstractValidator<BatchCreateScheduleRequest>
{
    public BatchCreateScheduleRequestValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh không được để trống.");

        RuleFor(x => x.Schedules)
            .NotEmpty().WithMessage("Danh sách phân ca không được để trống.")
            .Must(s => s != null && s.Count > 0).WithMessage("Danh sách phân ca phải có ít nhất 1 ca.");

        RuleForEach(x => x.Schedules).SetValidator(new CreateScheduleItemRequestValidator());
    }
}

public class CreateRecurringScheduleRequestValidator : AbstractValidator<CreateRecurringScheduleRequest>
{
    public CreateRecurringScheduleRequestValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Nhân viên không được để trống.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh không được để trống.");

        RuleFor(x => x.StartDate)
            .Must(d => d != default).WithMessage("Ngày bắt đầu không hợp lệ.");

        When(x => x.HasEndDate && x.EndDate.HasValue, () =>
        {
            RuleFor(x => x.EndDate!.Value)
                .GreaterThanOrEqualTo(x => x.StartDate)
                .WithMessage("Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu.");
        });
    }
}

public class PublishSchedulesRequestValidator : AbstractValidator<PublishSchedulesRequest>
{
    public PublishSchedulesRequestValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh không được để trống.");

        RuleFor(x => x.FromDate)
            .Must(d => d != default).WithMessage("Ngày bắt đầu không hợp lệ.");

        RuleFor(x => x.ToDate)
            .Must(d => d != default).WithMessage("Ngày kết thúc không hợp lệ.")
            .GreaterThanOrEqualTo(x => x.FromDate).WithMessage("Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu.");
    }
}

public class ResetAttendanceRequestValidator : AbstractValidator<ResetAttendanceRequest>
{
    public ResetAttendanceRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(255).WithMessage("Lý do hủy chấm công tối đa 255 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }
}

