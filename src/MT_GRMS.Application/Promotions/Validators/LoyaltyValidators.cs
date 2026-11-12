using FluentValidation;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions.Validators;

public class UpdateLoyaltyConfigRequestValidator : AbstractValidator<UpdateLoyaltyConfigRequest>
{
    public UpdateLoyaltyConfigRequestValidator()
    {
        RuleFor(x => x.EarningRateAmount)
            .GreaterThan(0).WithMessage("Số tiền chi tiêu để tích điểm phải lớn hơn 0");

        RuleFor(x => x.EarningPoints)
            .GreaterThan(0).WithMessage("Số điểm tích được phải lớn hơn 0");

        RuleFor(x => x.RedemptionRatePoints)
            .GreaterThan(0).WithMessage("Số điểm quy đổi phải lớn hơn 0");

        RuleFor(x => x.RedemptionRateAmount)
            .GreaterThan(0).WithMessage("Giá trị tiền quy đổi phải lớn hơn 0");

        RuleFor(x => x.MinRedemptionPoints)
            .GreaterThanOrEqualTo(0).WithMessage("Mức điểm tối thiểu để đổi không được âm");
    }
}

