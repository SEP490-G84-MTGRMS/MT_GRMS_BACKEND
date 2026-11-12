using FluentValidation;
using MT_GRMS.Application.BusinessDays.Dtos;

namespace MT_GRMS.Application.BusinessDays.Validators;

public class CloseBusinessDayRequestValidator : AbstractValidator<CloseBusinessDayRequest>
{
    public CloseBusinessDayRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.BusinessDate).NotEqual(default(DateOnly));
        RuleFor(x => x.ActualCashCounted).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
