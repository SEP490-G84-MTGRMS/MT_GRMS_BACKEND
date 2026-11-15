using FluentValidation;
using MT_GRMS.Application.BusinessDays.Dtos;

namespace MT_GRMS.Application.BusinessDays.Validators;

public class CloseBusinessDayRequestValidator : AbstractValidator<CloseBusinessDayRequest>
{
    public CloseBusinessDayRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.BusinessDate).NotEqual(default(DateOnly));
        RuleFor(x => x.FundCounts).NotNull();
        RuleForEach(x => x.FundCounts).ChildRules(fund =>
        {
            fund.RuleFor(x => x.FundId).NotEmpty();
            fund.RuleFor(x => x.ActualCashCounted).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        });
        RuleFor(x => x.FundCounts)
            .Must(items => items == null || items.Select(x => x.FundId).Distinct().Count() == items.Count)
            .WithMessage("Mỗi quỹ chỉ được kiểm đếm một lần.");
        RuleFor(x => x.PreviewToken).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
