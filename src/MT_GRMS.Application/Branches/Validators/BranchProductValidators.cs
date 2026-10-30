using FluentValidation;
using MT_GRMS.Application.Branches.Dtos;

namespace MT_GRMS.Application.Branches.Validators;

public class AssignBranchProductsValidator : AbstractValidator<AssignBranchProductsCommand>
{
    public AssignBranchProductsValidator()
    {
        RuleFor(x => x.ProductIds)
            .NotEmpty().WithMessage("Vui lòng chọn ít nhất một sản phẩm để gán vào chi nhánh.");

        RuleForEach(x => x.ProductIds)
            .NotEmpty().WithMessage("ID sản phẩm không được rỗng.");

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
            .GreaterThan(0).WithMessage("Giá bán lẻ niêm yết phải lớn hơn 0 VNĐ.");

        RuleFor(x => x.MinStockLevel)
            .GreaterThanOrEqualTo(0).WithMessage("Định mức tồn an toàn tối thiểu không thể nhỏ hơn 0.");
    }
}
