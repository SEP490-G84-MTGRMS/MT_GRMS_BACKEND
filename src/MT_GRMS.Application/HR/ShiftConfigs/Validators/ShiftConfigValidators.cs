using FluentValidation;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;

namespace MT_GRMS.Application.HR.ShiftConfigs.Validators;

public class CreateShiftConfigRequestValidator : AbstractValidator<CreateShiftConfigRequest>
{
    public CreateShiftConfigRequestValidator()
    {
        RuleFor(x => x.ShiftName)
            .NotEmpty().WithMessage("Tên ca không được để trống.")
            .Length(1, 100).WithMessage("Tên ca phải từ 1 đến 100 ký tự.");

        RuleFor(x => x)
            .Must(x => (x.BranchIds != null && x.BranchIds.Count > 0) || (x.BranchId.HasValue && x.BranchId.Value != Guid.Empty))
            .WithMessage("Phạm vi áp dụng (chi nhánh) là bắt buộc.");

        RuleFor(x => x.StartTime)
            .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            .WithMessage("Giờ bắt đầu không hợp lệ (phải trong khoảng 00:00 - 23:59).");

        RuleFor(x => x.EndTime)
            .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            .WithMessage("Giờ kết thúc không hợp lệ (phải trong khoảng 00:00 - 23:59).");

        When(x => !string.IsNullOrEmpty(x.ShiftCode), () =>
        {
            RuleFor(x => x.ShiftCode!)
                .Length(2, 30).WithMessage("Mã ca phải từ 2 đến 30 ký tự.")
                .Matches(@"^[A-Za-z0-9_-]+$").WithMessage("Mã ca chỉ gồm chữ cái không dấu, số, gạch ngang/dưới.");
        });

        RuleFor(x => x)
            .Must(x => x.StartTime != x.EndTime || (x.DurationHours.HasValue && x.DurationHours.Value > 0))
            .WithMessage("Giờ bắt đầu và giờ kết thúc ca không được trùng nhau (thời lượng ca phải lớn hơn 0).");

        When(x => x.DurationHours.HasValue, () =>
        {
            RuleFor(x => x.DurationHours!.Value)
                .GreaterThan(0).WithMessage("Thời lượng ca làm việc phải lớn hơn 0.")
                .LessThanOrEqualTo(24).WithMessage("Thời lượng ca làm việc không được vượt quá 24 giờ.");
        });

        When(x => x.AllowedCheckInStart.HasValue, () =>
        {
            RuleFor(x => x.AllowedCheckInStart!.Value)
                .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                .WithMessage("Khung check-in sớm không hợp lệ.");
        });

        When(x => x.AllowedCheckInEnd.HasValue, () =>
        {
            RuleFor(x => x.AllowedCheckInEnd!.Value)
                .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                .WithMessage("Khung check-in trễ không hợp lệ.");
        });

        When(x => !string.IsNullOrEmpty(x.Description), () =>
        {
            RuleFor(x => x.Description)
                .MaximumLength(255).WithMessage("Ghi chú không được vượt quá 255 ký tự.");
        });
    }
}

public class UpdateShiftConfigRequestValidator : AbstractValidator<UpdateShiftConfigRequest>
{
    public UpdateShiftConfigRequestValidator()
    {
        RuleFor(x => x.ShiftName)
            .NotEmpty().WithMessage("Tên ca không được để trống.")
            .Length(1, 100).WithMessage("Tên ca phải từ 1 đến 100 ký tự.");

        RuleFor(x => x.StartTime)
            .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            .WithMessage("Giờ bắt đầu không hợp lệ (phải trong khoảng 00:00 - 23:59).");

        RuleFor(x => x.EndTime)
            .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            .WithMessage("Giờ kết thúc không hợp lệ (phải trong khoảng 00:00 - 23:59).");

        When(x => !string.IsNullOrEmpty(x.ShiftCode), () =>
        {
            RuleFor(x => x.ShiftCode!)
                .Length(2, 30).WithMessage("Mã ca phải từ 2 đến 30 ký tự.")
                .Matches(@"^[A-Za-z0-9_-]+$").WithMessage("Mã ca chỉ gồm chữ cái không dấu, số, gạch ngang/dưới.");
        });

        RuleFor(x => x)
            .Must(x => x.StartTime != x.EndTime || (x.DurationHours.HasValue && x.DurationHours.Value > 0))
            .WithMessage("Giờ bắt đầu và giờ kết thúc ca không được trùng nhau (thời lượng ca phải lớn hơn 0).");

        When(x => x.DurationHours.HasValue, () =>
        {
            RuleFor(x => x.DurationHours!.Value)
                .GreaterThan(0).WithMessage("Thời lượng ca làm việc phải lớn hơn 0.")
                .LessThanOrEqualTo(24).WithMessage("Thời lượng ca làm việc không được vượt quá 24 giờ.");
        });

        When(x => x.AllowedCheckInStart.HasValue, () =>
        {
            RuleFor(x => x.AllowedCheckInStart!.Value)
                .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                .WithMessage("Khung check-in sớm không hợp lệ.");
        });

        When(x => x.AllowedCheckInEnd.HasValue, () =>
        {
            RuleFor(x => x.AllowedCheckInEnd!.Value)
                .Must(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                .WithMessage("Khung check-in trễ không hợp lệ.");
        });

        When(x => !string.IsNullOrEmpty(x.Description), () =>
        {
            RuleFor(x => x.Description)
                .MaximumLength(255).WithMessage("Ghi chú không được vượt quá 255 ký tự.");
        });
    }
}
