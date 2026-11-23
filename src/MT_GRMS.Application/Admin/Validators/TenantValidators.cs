using System.Text.RegularExpressions;
using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Validators;

public sealed class UpdateStoreInfoRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateStoreInfoRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Vui lòng nhập tên cửa hàng.")
            .Must(value => value.Trim().Length >= 2).WithMessage("Tên cửa hàng tối thiểu 2 ký tự.")
            .Must(value => value.Trim().Length <= 150).WithMessage("Tên cửa hàng tối đa 150 ký tự.");

        RuleFor(x => x.Hotline)
            .Must(value => string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value.Trim(), @"\A\+?[0-9]{9,12}\z"))
            .WithMessage("Số điện thoại phải có 9–12 chữ số, có thể bắt đầu bằng +.");

        RuleFor(x => x.TaxCode)
            .Must(value => value is null || value.Trim().Length <= 50)
            .WithMessage("Mã số thuế tối đa 50 ký tự.");

        RuleFor(x => x.City)
            .Must(value => value is null || value.Trim().Length <= 100)
            .WithMessage("Tỉnh / Thành phố tối đa 100 ký tự.");

        RuleFor(x => x.Address)
            .Must(value => value is null || value.Trim().Length <= 255)
            .WithMessage("Địa chỉ tối đa 255 ký tự.");
    }
}

public class LockTenantRequestValidator : AbstractValidator<LockTenantRequest>
{
    public LockTenantRequestValidator()
    {
        RuleFor(x => x.LockType)
            .NotEmpty().WithMessage("Loại khóa không được để trống")
            .Must(t => string.Equals(t, "soft", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "hard", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Loại khóa chỉ chấp nhận 'soft' hoặc 'hard'");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Lý do khóa không được để trống")
            .Must(reason => reason?.Trim().Length >= 10).WithMessage("Lý do khóa tối thiểu 10 ký tự")
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
            .Must(reason => reason?.Trim().Length >= 10).WithMessage("Lý do gia hạn tối thiểu 10 ký tự")
            .MaximumLength(500).WithMessage("Lý do gia hạn tối đa 500 ký tự");
    }
}

