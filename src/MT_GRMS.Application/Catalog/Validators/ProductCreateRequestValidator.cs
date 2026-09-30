using FluentValidation;
using MT_GRMS.Application.Catalog.Dtos;

namespace MT_GRMS.Application.Catalog.Validators;

public class ProductCreateRequestValidator : AbstractValidator<ProductCreateRequest>
{
    public ProductCreateRequestValidator()
    {
    }
}
