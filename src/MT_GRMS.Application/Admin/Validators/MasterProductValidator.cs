using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Admin.Validators;

public sealed class MasterProductValidator : AbstractValidator<IMasterProductFields>
{
    public MasterProductValidator()
    {
        RuleFor(x => x.Name).Must(value => value?.Trim().Length >= 2).WithMessage("Tên hàng mẫu phải có ít nhất 2 ký tự.")
            .MaximumLength(250).WithMessage("Tên hàng mẫu tối đa 250 ký tự.");
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(100).WithName("Thương hiệu");
        RuleFor(x => x.CategoryName).NotEmpty().MaximumLength(100).WithName("Ngành hàng");
        RuleFor(x => x.BaseUnit).NotEmpty().MaximumLength(30).WithName("Đơn vị cơ bản");
        RuleFor(x => x.SuggestedRetailPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true).WithName("Giá bán lẻ");
        RuleFor(x => x.VatRate).Must(rate => rate is 0 or 8 or 10).WithMessage("Thuế suất phải là 0, 8 hoặc 10%.");
        RuleFor(x => x.TaxCategory).MaximumLength(50);
        RuleFor(x => x.ImageUrl).MaximumLength(1000)
            .Must(value => string.IsNullOrWhiteSpace(value) ||
                (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0))
            .WithMessage("Link ảnh phải là địa chỉ HTTP hoặc HTTPS.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleForEach(x => x.Units).NotNull().ChildRules(unit =>
        {
            unit.RuleFor(x => x.UnitName).NotEmpty().MaximumLength(50).WithName("Tên đơn vị quy đổi");
            unit.RuleFor(x => x.ConversionRate).GreaterThan(1).PrecisionScale(18, 4, true).WithName("Hệ số quy đổi");
            unit.RuleFor(x => x.Barcode).MaximumLength(50).WithName("Mã vạch đơn vị");
        });
    }

    internal static void EnsureValid(IMasterProductFields request)
    {
        var result = new MasterProductValidator().Validate(request);
        if (!result.IsValid)
            throw new BusinessException("VALIDATION_ERROR", result.Errors[0].ErrorMessage);
    }

    public static void ValidateCreate(CreateMasterProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
            throw new BusinessException("BARCODE_REQUIRED", "Mã vạch (Barcode) là bắt buộc.");
        if (request.Barcode.Trim().Length > 50)
            throw new BusinessException("VALIDATION_ERROR", "Mã vạch tối đa 50 ký tự.");
        EnsureValid(request);
    }
}
