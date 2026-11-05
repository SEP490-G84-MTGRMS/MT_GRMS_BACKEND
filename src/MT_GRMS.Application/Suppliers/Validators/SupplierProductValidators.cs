using FluentValidation;
using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Application.Suppliers.Validators;

public class AssignSupplierProductValidator : AbstractValidator<AssignSupplierProductCommand>
{
    public AssignSupplierProductValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Vui lòng chọn sản phẩm cần gán cho nhà cung cấp.");

        RuleFor(x => x.DeliveryUoM)
            .MaximumLength(50).WithMessage("Đơn vị giao hàng không được vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeliveryUoM));

        RuleFor(x => x.AgreedWholesalePrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá nhập không được âm.");

        RuleFor(x => x.MinimumOrderQuantity)
            .GreaterThan(0).WithMessage("Số lượng đặt hàng tối thiểu (MOQ) phải lớn hơn 0.");

        RuleFor(x => x.SupplierProductCode)
            .MaximumLength(50).WithMessage("Mã sản phẩm phía NCC không được vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrEmpty(x.SupplierProductCode));
    }
}

public class UpdateSupplierProductValidator : AbstractValidator<UpdateSupplierProductCommand>
{
    public UpdateSupplierProductValidator()
    {
        RuleFor(x => x.DeliveryUoM)
            .MaximumLength(50).WithMessage("Đơn vị giao hàng không được vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeliveryUoM));

        RuleFor(x => x.AgreedWholesalePrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá nhập không được âm.");

        RuleFor(x => x.MinimumOrderQuantity)
            .GreaterThan(0).WithMessage("Số lượng đặt hàng tối thiểu (MOQ) phải lớn hơn 0.");

        RuleFor(x => x.SupplierProductCode)
            .MaximumLength(50).WithMessage("Mã sản phẩm phía NCC không được vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrEmpty(x.SupplierProductCode));
    }
}
