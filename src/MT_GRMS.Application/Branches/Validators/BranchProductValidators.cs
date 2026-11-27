using FluentValidation;
using MT_GRMS.Application.Branches.Dtos;

namespace MT_GRMS.Application.Branches.Validators;

public class AssignBranchProductsValidator : AbstractValidator<AssignBranchProductsCommand>
{
    public AssignBranchProductsValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.Items != null && x.Items.Count > 0) || (x.ProductIds != null && x.ProductIds.Count > 0))
            .WithMessage("Vui lòng chọn ít nhất một sản phẩm để gán vào chi nhánh.");

        RuleForEach(x => x.ProductIds)
            .NotEmpty().WithMessage("ID sản phẩm không được rỗng.");

        RuleForEach(x => x.Items)
            .Must(item => item != null && item.ProductId != Guid.Empty)
            .When(x => x.Items != null)
            .WithMessage("ID sản phẩm không được rỗng.");

        RuleFor(x => x.DefaultRetailPrice)
            .GreaterThanOrEqualTo(0).When(x => x.DefaultRetailPrice.HasValue)
            .WithMessage("Giá bán lẻ mặc định không thể nhỏ hơn 0 VNĐ.");

        RuleFor(x => x.DefaultMinStock)
            .GreaterThanOrEqualTo(0).When(x => x.DefaultMinStock.HasValue)
            .WithMessage("Định mức tồn tối thiểu mặc định không thể nhỏ hơn 0.");

    }
}

public class UpdateBranchProductValidator : AbstractValidator<UpdateBranchProductCommand>
{
    public UpdateBranchProductValidator()
    {
        RuleFor(x => x.RetailPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá bán lẻ niêm yết không được nhỏ hơn 0 VNĐ.")
            .LessThanOrEqualTo(1_000_000_000m).WithMessage("Giá bán lẻ niêm yết không được vượt quá 1.000.000.000 VNĐ.");

        RuleFor(x => x.RetailPrice)
            .GreaterThan(0).When(x => x.UnitPrices == null || x.UnitPrices.Count == 0)
            .WithMessage("Giá bán lẻ niêm yết phải lớn hơn 0 VNĐ.");

        RuleFor(x => x.MinStockLevel)
            .GreaterThanOrEqualTo(0).When(x => x.MinStockLevel.HasValue)
            .WithMessage("Định mức tồn an toàn tối thiểu không thể nhỏ hơn 0.");

        RuleFor(x => x.MinOrderQuantity)
            .GreaterThan(0).When(x => x.MinOrderQuantity.HasValue)
            .WithMessage("MOQ phải lớn hơn 0.");

        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrEmpty(s)
                || string.Equals(s, "Active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Inactive", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Trạng thái kinh doanh chỉ có thể là 'Active' hoặc 'Inactive'.");
    }
}
