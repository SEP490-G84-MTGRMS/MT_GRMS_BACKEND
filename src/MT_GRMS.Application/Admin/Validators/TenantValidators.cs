using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Validators;

public class LockTenantRequestValidator : AbstractValidator<LockTenantRequest>
{
    public LockTenantRequestValidator()
    {
        RuleFor(x => x.LockType)
            .NotEmpty().WithMessage("Loại khóa không được để trống")
            .Must(t => t.ToLower() == "soft" || t.ToLower() == "hard")
            .WithMessage("Loại khóa chỉ chấp nhận 'soft' hoặc 'hard'");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Lý do khóa không được để trống")
            .MaximumLength(500).WithMessage("Lý do khóa tối đa 500 ký tự");
    }
}

public class ExtendGraceRequestValidator : AbstractValidator<ExtendGraceRequest>
{
    public ExtendGraceRequestValidator()
    {
        RuleFor(x => x.AdditionalDays)
            .GreaterThan(0).WithMessage("Số ngày gia hạn phải lớn hơn 0")
            .LessThanOrEqualTo(365).WithMessage("Số ngày gia hạn tối đa là 365 ngày");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Lý do gia hạn không được để trống")
            .MaximumLength(500).WithMessage("Lý do gia hạn tối đa 500 ký tự");
    }
}

