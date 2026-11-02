using FluentValidation;
using MT_GRMS.Application.HR.Employees.Dtos;

namespace MT_GRMS.Application.HR.Employees.Validators;

public class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên nhân viên không được để trống.")
            .MaximumLength(100).WithMessage("Họ và tên không vượt quá 100 ký tự.");

        RuleFor(x => x.CitizenId)
            .NotEmpty().WithMessage("Số CCCD không được để trống.")
            .Matches(@"^[0-9]{12}$").WithMessage("Số CCCD phải gồm đúng 12 chữ số hợp lệ.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(@"^(0[3|5|7|8|9])[0-9]{8}$").WithMessage("Số điện thoại không đúng định dạng Việt Nam.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh làm việc bắt buộc phải được chọn.");

        RuleFor(x => x.BaseRate)
            .GreaterThanOrEqualTo(0).WithMessage("Mức lương cơ bản không được âm.");

        RuleForEach(x => x.Allowances)
            .ChildRules(al =>
            {
                al.RuleFor(a => a.Name).NotEmpty().WithMessage("Tên phụ cấp không được để trống.");
                al.RuleFor(a => a.Amount).GreaterThanOrEqualTo(0).WithMessage("Mức phụ cấp không được âm.");
            });

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không đúng định dạng.")
                .MaximumLength(100).WithMessage("Email không vượt quá 100 ký tự.");
        });
    }
}

public class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên nhân viên không được để trống.")
            .MaximumLength(100).WithMessage("Họ và tên không vượt quá 100 ký tự.");

        RuleFor(x => x.CitizenId)
            .NotEmpty().WithMessage("Số CCCD không được để trống.")
            .Matches(@"^[0-9]{12}$").WithMessage("Số CCCD phải gồm đúng 12 chữ số hợp lệ.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(@"^(0[3|5|7|8|9])[0-9]{8}$").WithMessage("Số điện thoại không đúng định dạng Việt Nam.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh làm việc bắt buộc phải được chọn.");

        RuleFor(x => x.BaseRate)
            .GreaterThanOrEqualTo(0).WithMessage("Mức lương cơ bản không được âm.");

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không đúng định dạng.")
                .MaximumLength(100).WithMessage("Email không vượt quá 100 ký tự.");
        });
    }
}

public class CreateIdentityAccountRequestValidator : AbstractValidator<CreateIdentityAccountRequest>
{
    public CreateIdentityAccountRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Tên đăng nhập không được để trống.")
            .MinimumLength(3).WithMessage("Tên đăng nhập tối thiểu 3 ký tự.")
            .MaximumLength(50).WithMessage("Tên đăng nhập không vượt quá 50 ký tự.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .MinimumLength(6).WithMessage("Mật khẩu tối thiểu 6 ký tự.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Vai trò phân quyền không được để trống.")
            .Must(r => r is "CASHIER" or "STAFF" or "BRANCH_MANAGER" or "STORE_OWNER")
            .WithMessage("Vai trò không hợp lệ (hỗ trợ CASHIER, STAFF, BRANCH_MANAGER, STORE_OWNER).");
    }
}

