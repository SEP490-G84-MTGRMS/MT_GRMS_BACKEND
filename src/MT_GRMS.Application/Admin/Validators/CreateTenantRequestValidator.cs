using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Validators;

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(150)
            .WithMessage("Tên cửa hàng phải có từ 2 đến 150 ký tự.");
        RuleFor(x => x.Subdomain).NotEmpty().MinimumLength(3).MaximumLength(63)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Subdomain phải có từ 3 đến 63 ký tự, gồm chữ thường, số và dấu gạch nối ở giữa.");
        RuleFor(x => x.OwnerName).NotEmpty().MinimumLength(2).MaximumLength(100)
            .Matches(@"^[\p{L}][\p{L}\p{M}' -]*$")
            .WithMessage("Họ tên chủ cửa hàng không hợp lệ.");
        RuleFor(x => x.OwnerEmail).NotEmpty().MaximumLength(100).EmailAddress()
            .WithMessage("Email chủ cửa hàng không hợp lệ.");
        RuleFor(x => x.OwnerPhone).NotEmpty().Matches(@"^(03|05|07|08|09)\d{8}$")
            .WithMessage("Số điện thoại phải là số di động Việt Nam gồm 10 chữ số.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(64)
            .Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]").Matches("[^A-Za-z0-9]")
            .Must(value => value is not null && !value.Any(char.IsWhiteSpace))
            .WithMessage("Mật khẩu cần 8–64 ký tự, gồm chữ hoa, chữ thường, số, ký tự đặc biệt và không có khoảng trắng.");
        RuleFor(x => x.Address).MaximumLength(255);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.TaxCode).MaximumLength(50);
    }
}
