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
            .NotEmpty().WithMessage("Đơn vị giao hàng không được để trống.")
            .MaximumLength(50).WithMessage("Đơn vị giao hàng không được vượt quá 50 ký tự.");

        RuleFor(x => x.AgreedWholesalePrice)
            .GreaterThan(0).WithMessage("Giá sỉ thỏa thuận phải lớn hơn 0 VNĐ.");

        RuleFor(x => x.MinimumOrderQuantity)
            .GreaterThanOrEqualTo(1).WithMessage("Số lượng đặt hàng tối thiểu (MOQ) phải từ 1 trở lên.");

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
            .NotEmpty().WithMessage("Đơn vị giao hàng không được để trống.")
            .MaximumLength(50).WithMessage("Đơn vị giao hàng không được vượt quá 50 ký tự.");

        RuleFor(x => x.AgreedWholesalePrice)
            .GreaterThan(0).WithMessage("Giá sỉ thỏa thuận phải lớn hơn 0 VNĐ.");

        RuleFor(x => x.MinimumOrderQuantity)
            .GreaterThanOrEqualTo(1).WithMessage("Số lượng đặt hàng tối thiểu (MOQ) phải từ 1 trở lên.");

        RuleFor(x => x.SupplierProductCode)
            .MaximumLength(50).WithMessage("Mã sản phẩm phía NCC không được vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrEmpty(x.SupplierProductCode));
    }
}
