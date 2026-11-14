using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;
using MT_GRMS.Application.HR.Employees.Dtos;

namespace MT_GRMS.Application.HR.Employees.Validators;

public static class VietnameseTextNormalizer
{
    public static DateTime GetVietnamNow() => DateTime.UtcNow.AddHours(7);

    public static DateTime GetVietnamToday() => DateTime.UtcNow.AddHours(7).Date;

    public static string NormalizeForComparison(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = Regex.Replace(input.Trim(), @"\s+", " ").ToLowerInvariant();
        var normalized = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c switch
                {
                    'đ' or 'Đ' => 'd',
                    _ => c
                });
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên nhân viên không được để trống.")
            .MinimumLength(2).WithMessage("Họ và tên phải từ 2 đến 100 ký tự.")
            .MaximumLength(100).WithMessage("Họ và tên không vượt quá 100 ký tự.")
            .Matches(@"^[\p{L}\s'\.\-]+$").WithMessage("Họ và tên không được chứa chữ số hoặc ký tự đặc biệt.");

        When(x => x.DateOfBirth.HasValue, () =>
        {
            RuleFor(x => x.DateOfBirth!.Value)
                .Must(dob =>
                {
                    var todayVn = VietnameseTextNormalizer.GetVietnamToday();
                    return dob.Date <= todayVn.AddYears(-15) && dob.Date >= todayVn.AddYears(-75);
                })
                .WithMessage("Nhân viên phải đủ từ 15 tuổi đến 75 tuổi theo quy định lao động.");
        });

        When(x => x.DateOfBirth.HasValue && x.StartDate.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.StartDate!.Value.Date >= x.DateOfBirth!.Value.Date.AddYears(15))
                .WithName(nameof(CreateEmployeeRequest.StartDate))
                .WithMessage("Ngày bắt đầu làm việc không hợp lệ so với ngày sinh (phải đủ 15 tuổi).");
        });

        RuleFor(x => x.CitizenId)
            .NotEmpty().WithMessage("Số CCCD không được để trống.")
            .Matches(@"^(00[1-9]|0[1-8][0-9]|09[0-6])[0-9]{9}$").WithMessage("Số CCCD phải gồm đúng 12 chữ số hợp lệ (3 số đầu thuộc mã tỉnh/thành 001–096).");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(@"^(0[3|5|7|8|9])[0-9]{8}$").WithMessage("Số điện thoại không đúng định dạng Việt Nam.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh làm việc bắt buộc phải được chọn.");

        RuleFor(x => x.BaseRate)
            .GreaterThanOrEqualTo(0).WithMessage("Mức lương cơ bản không được âm.")
            .LessThanOrEqualTo(1_000_000_000m).WithMessage("Mức lương cơ bản không được vượt quá 1.000.000.000 đ.");

        RuleForEach(x => x.Allowances)
            .ChildRules(al =>
            {
                al.RuleFor(a => a.Name)
                    .NotEmpty().WithMessage("Tên phụ cấp không được để trống.")
                    .MaximumLength(100).WithMessage("Tên phụ cấp không vượt quá 100 ký tự.");
                al.RuleFor(a => a.Amount)
                    .GreaterThanOrEqualTo(0).WithMessage("Mức phụ cấp không được âm.")
                    .LessThanOrEqualTo(500_000_000m).WithMessage("Mức phụ cấp không được vượt quá 500.000.000 đ.");
            });

        When(x => x.Allowances != null && x.Allowances.Count > 1, () =>
        {
            RuleFor(x => x.Allowances!)
                .Must(list =>
                {
                    var names = list
                        .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                        .Select(a => VietnameseTextNormalizer.NormalizeForComparison(a.Name))
                        .ToList();
                    return names.Count == names.Distinct().Count();
                })
                .WithMessage("Tên các khoản phụ cấp không được trùng lặp (kể cả viết hoa/thường hoặc có dấu/không dấu).");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không đúng định dạng (Ví dụ: nguyenvana@gmail.com hoặc nhanvien@mtmart.vn).")
                .MaximumLength(100).WithMessage("Email không vượt quá 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Address), () =>
        {
            RuleFor(x => x.Address)
                .MaximumLength(255).WithMessage("Địa chỉ không vượt quá 255 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Note), () =>
        {
            RuleFor(x => x.Note)
                .MaximumLength(500).WithMessage("Ghi chú không vượt quá 500 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.OtWeekday), () =>
        {
            RuleFor(x => x.OtWeekday)
                .MaximumLength(20).WithMessage("Hệ số làm thêm ngày thường không vượt quá 20 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.OtSunday), () =>
        {
            RuleFor(x => x.OtSunday)
                .MaximumLength(20).WithMessage("Hệ số làm thêm Chủ nhật không vượt quá 20 ký tự.");
        });

        When(x => x.InsuranceBaseSalary.HasValue, () =>
        {
            RuleFor(x => x.InsuranceBaseSalary!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Mức lương đóng bảo hiểm không được âm.")
                .LessThanOrEqualTo(1_000_000_000m).WithMessage("Mức lương đóng bảo hiểm không được vượt quá 1.000.000.000 đ.");
        });

        When(x => x.HasLaborContract && string.Equals(x.SalaryType?.Trim(), "monthly", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.InsuranceBaseSalary)
                .Must((req, insBase) => (insBase ?? req.BaseRate) > 0)
                .WithMessage("Mức lương đóng bảo hiểm phải lớn hơn 0 khi nhân viên có hợp đồng lao động.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.EmployeeCode), () =>
        {
            RuleFor(x => x.EmployeeCode!)
                .MaximumLength(30).WithMessage("Mã nhân viên không vượt quá 30 ký tự.")
                .Matches(@"^[A-Za-z0-9_-]+$").WithMessage("Mã nhân viên chỉ gồm chữ cái không dấu, số, gạch ngang/dưới và tối đa 30 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.BankAccountNumber), () =>
        {
            RuleFor(x => x.BankAccountNumber!)
                .MaximumLength(30).WithMessage("Số tài khoản ngân hàng không vượt quá 30 ký tự.")
                .Matches(@"^[A-Za-z0-9]+$").WithMessage("Số tài khoản ngân hàng chỉ được nhập chữ cái tiếng Việt không dấu (A-Z) và chữ số (0-9), viết liền không khoảng trắng.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.BankName), () =>
        {
            RuleFor(x => x.BankName)
                .MaximumLength(100).WithMessage("Tên ngân hàng không vượt quá 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.BankAccountHolder), () =>
        {
            RuleFor(x => x.BankAccountHolder)
                .MaximumLength(100).WithMessage("Chủ tài khoản ngân hàng không vượt quá 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.AvatarUrl) && !x.AvatarUrl!.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.AvatarUrl)
                .MaximumLength(255).WithMessage("Đường dẫn ảnh đại diện không vượt quá 255 ký tự.");
        });
    }
}

public class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên nhân viên không được để trống.")
            .MinimumLength(2).WithMessage("Họ và tên phải từ 2 đến 100 ký tự.")
            .MaximumLength(100).WithMessage("Họ và tên không vượt quá 100 ký tự.")
            .Matches(@"^[\p{L}\s'\.\-]+$").WithMessage("Họ và tên không được chứa chữ số hoặc ký tự đặc biệt.");

        When(x => x.DateOfBirth.HasValue, () =>
        {
            RuleFor(x => x.DateOfBirth!.Value)
                .Must(dob =>
                {
                    var todayVn = VietnameseTextNormalizer.GetVietnamToday();
                    return dob.Date <= todayVn.AddYears(-15) && dob.Date >= todayVn.AddYears(-75);
                })
                .WithMessage("Nhân viên phải đủ từ 15 tuổi đến 75 tuổi theo quy định lao động.");
        });

        When(x => x.DateOfBirth.HasValue && x.StartDate.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.StartDate!.Value.Date >= x.DateOfBirth!.Value.Date.AddYears(15))
                .WithName(nameof(CreateEmployeeRequest.StartDate))
                .WithMessage("Ngày bắt đầu làm việc không hợp lệ so với ngày sinh (phải đủ 15 tuổi).");
        });

        RuleFor(x => x.CitizenId)
            .NotEmpty().WithMessage("Số CCCD không được để trống.")
            .Matches(@"^(00[1-9]|0[1-8][0-9]|09[0-6])[0-9]{9}$").WithMessage("Số CCCD phải gồm đúng 12 chữ số hợp lệ (3 số đầu thuộc mã tỉnh/thành 001–096).");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(@"^(0[3|5|7|8|9])[0-9]{8}$").WithMessage("Số điện thoại không đúng định dạng Việt Nam.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh làm việc bắt buộc phải được chọn.");

        RuleFor(x => x.BaseRate)
            .GreaterThanOrEqualTo(0).WithMessage("Mức lương cơ bản không được âm.")
            .LessThanOrEqualTo(1_000_000_000m).WithMessage("Mức lương cơ bản không được vượt quá 1.000.000.000 đ.");

        RuleForEach(x => x.Allowances)
            .ChildRules(al =>
            {
                al.RuleFor(a => a.Name)
                    .NotEmpty().WithMessage("Tên phụ cấp không được để trống.")
                    .MaximumLength(100).WithMessage("Tên phụ cấp không vượt quá 100 ký tự.");
                al.RuleFor(a => a.Amount)
                    .GreaterThanOrEqualTo(0).WithMessage("Mức phụ cấp không được âm.")
                    .LessThanOrEqualTo(500_000_000m).WithMessage("Mức phụ cấp không được vượt quá 500.000.000 đ.");
            });

        When(x => x.Allowances != null && x.Allowances.Count > 1, () =>
        {
            RuleFor(x => x.Allowances!)
                .Must(list =>
                {
                    var names = list
                        .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                        .Select(a => VietnameseTextNormalizer.NormalizeForComparison(a.Name))
                        .ToList();
                    return names.Count == names.Distinct().Count();
                })
                .WithMessage("Tên các khoản phụ cấp không được trùng lặp (kể cả viết hoa/thường hoặc có dấu/không dấu).");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không đúng định dạng (Ví dụ: nguyenvana@gmail.com hoặc nhanvien@mtmart.vn).")
                .MaximumLength(100).WithMessage("Email không vượt quá 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Address), () =>
        {
            RuleFor(x => x.Address)
                .MaximumLength(255).WithMessage("Địa chỉ không vượt quá 255 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Note), () =>
        {
            RuleFor(x => x.Note)
                .MaximumLength(500).WithMessage("Ghi chú không vượt quá 500 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.OtWeekday), () =>
        {
            RuleFor(x => x.OtWeekday)
                .MaximumLength(20).WithMessage("Hệ số làm thêm ngày thường không vượt quá 20 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.OtSunday), () =>
        {
            RuleFor(x => x.OtSunday)
                .MaximumLength(20).WithMessage("Hệ số làm thêm Chủ nhật không vượt quá 20 ký tự.");
        });

        When(x => x.InsuranceBaseSalary.HasValue, () =>
        {
            RuleFor(x => x.InsuranceBaseSalary!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Mức lương đóng bảo hiểm không được âm.")
                .LessThanOrEqualTo(1_000_000_000m).WithMessage("Mức lương đóng bảo hiểm không được vượt quá 1.000.000.000 đ.");
        });

        When(x => x.HasLaborContract && string.Equals(x.SalaryType?.Trim(), "monthly", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.InsuranceBaseSalary)
                .Must((req, insBase) => (insBase ?? req.BaseRate) > 0)
                .WithMessage("Mức lương đóng bảo hiểm phải lớn hơn 0 khi nhân viên có hợp đồng lao động.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.EmployeeCode), () =>
        {
            RuleFor(x => x.EmployeeCode!)
                .MaximumLength(30).WithMessage("Mã nhân viên không vượt quá 30 ký tự.")
                .Matches(@"^[A-Za-z0-9_-]+$").WithMessage("Mã nhân viên chỉ gồm chữ cái không dấu, số, gạch ngang/dưới và tối đa 30 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.BankAccountNumber), () =>
        {
            RuleFor(x => x.BankAccountNumber!)
                .MaximumLength(30).WithMessage("Số tài khoản ngân hàng không vượt quá 30 ký tự.")
                .Matches(@"^[A-Za-z0-9]+$").WithMessage("Số tài khoản ngân hàng chỉ được nhập chữ cái tiếng Việt không dấu (A-Z) và chữ số (0-9), viết liền không khoảng trắng.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.BankName), () =>
        {
            RuleFor(x => x.BankName)
                .MaximumLength(100).WithMessage("Tên ngân hàng không vượt quá 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.BankAccountHolder), () =>
        {
            RuleFor(x => x.BankAccountHolder)
                .MaximumLength(100).WithMessage("Chủ tài khoản ngân hàng không vượt quá 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.AvatarUrl) && !x.AvatarUrl!.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.AvatarUrl)
                .MaximumLength(255).WithMessage("Đường dẫn ảnh đại diện không vượt quá 255 ký tự.");
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
            .MaximumLength(50).WithMessage("Tên đăng nhập không vượt quá 50 ký tự.")
            .Matches(@"^[A-Za-z0-9._\-]+$").WithMessage("Tên đăng nhập chỉ được chứa chữ cái không dấu (a-z), chữ số (0-9), dấu chấm, gạch dưới hoặc gạch ngang, không chứa khoảng trắng.");

        When(x => !string.IsNullOrWhiteSpace(x.DisplayName), () =>
        {
            RuleFor(x => x.DisplayName!)
                .MinimumLength(2).WithMessage("Tên hiển thị phải từ 2 đến 100 ký tự.")
                .MaximumLength(100).WithMessage("Tên hiển thị không vượt quá 100 ký tự.");
        });

        RuleFor(x => x.Password)
            .MinimumLength(6).When(x => !string.IsNullOrEmpty(x.Password))
            .WithMessage("Mật khẩu tối thiểu 6 ký tự.")
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Password))
            .WithMessage("Mật khẩu không vượt quá 50 ký tự.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Vai trò phân quyền không được để trống.")
            .Must(r => r.Equals("CASHIER", StringComparison.OrdinalIgnoreCase) ||
                       r.Equals("STAFF", StringComparison.OrdinalIgnoreCase) ||
                       r.Equals("EMPLOYEE", StringComparison.OrdinalIgnoreCase) ||
                       r.Equals("BRANCH_MANAGER", StringComparison.OrdinalIgnoreCase) ||
                       r.Equals("STORE_OWNER", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Vai trò không hợp lệ (hỗ trợ CASHIER, STAFF, BRANCH_MANAGER, STORE_OWNER, EMPLOYEE).");
    }
}
