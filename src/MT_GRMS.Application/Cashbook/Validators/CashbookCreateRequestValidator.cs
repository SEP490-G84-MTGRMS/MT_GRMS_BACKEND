using FluentValidation;
using MT_GRMS.Application.Cashbook.Dtos;

namespace MT_GRMS.Application.Cashbook.Validators;

public class CashbookCreateRequestValidator : AbstractValidator<CashbookCreateRequest>
{
    public CashbookCreateRequestValidator()
    {
    }
}
