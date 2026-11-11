using FluentValidation;
using MT_GRMS.Application.Catalog.Dtos;

namespace MT_GRMS.Application.Catalog.Validators;

public class ProductCreateRequestValidator : AbstractValidator<ProductCreateRequest>
{
    public ProductCreateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
            .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự.");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Vui lòng chọn nhóm hàng hợp lệ.");

        RuleFor(x => x.SellingPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá bán không được nhỏ hơn 0.")
            .LessThanOrEqualTo(1_000_000_000m).WithMessage("Giá bán không được vượt quá 1.000.000.000 đ.");

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0).When(x => x.CostPrice.HasValue).WithMessage("Giá vốn không được nhỏ hơn 0.")
            .LessThanOrEqualTo(1_000_000_000m).When(x => x.CostPrice.HasValue).WithMessage("Giá vốn không được vượt quá 1.000.000.000 đ.");

        RuleFor(x => x.ProductCode)
            .MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.ProductCode))
            .Matches(@"^[A-Za-z0-9_-]{1,50}$").When(x => !string.IsNullOrWhiteSpace(x.ProductCode))
            .WithMessage("Mã hàng không được vượt quá 50 ký tự và chỉ gồm chữ cái không dấu, số, gạch ngang/dưới.");

        RuleFor(x => x.Barcode)
            .MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.Barcode))
            .Must(b => string.IsNullOrWhiteSpace(b) || !b.Trim().Any(char.IsWhiteSpace))
            .WithMessage("Mã vạch không hợp lệ (tối đa 50 ký tự, không chứa khoảng trắng).");

        RuleFor(x => x.BaseUnit)
            .MaximumLength(30).When(x => !string.IsNullOrWhiteSpace(x.BaseUnit))
            .WithMessage("Tên đơn vị cơ bản không được vượt quá 30 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Mô tả sản phẩm không được vượt quá 1000 ký tự.");

        RuleFor(x => x.InternalNote)
            .MaximumLength(500).When(x => !string.IsNullOrWhiteSpace(x.InternalNote))
            .WithMessage("Ghi chú nội bộ không được vượt quá 500 ký tự.");
    }
}
