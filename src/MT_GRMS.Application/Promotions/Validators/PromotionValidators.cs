using FluentValidation;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions.Validators;

public class PromotionCreateRequestValidator : AbstractValidator<PromotionCreateRequest>
{
    public PromotionCreateRequestValidator()
    {
        RuleFor(x => x.PromotionCode)
            .NotEmpty().WithMessage("Mã khuyến mại không được để trống")
            .MaximumLength(50).WithMessage("Mã khuyến mại không quá 50 ký tự");

        RuleFor(x => x.PromotionName)
            .NotEmpty().WithMessage("Tên chương trình khuyến mại không được để trống")
            .MaximumLength(150).WithMessage("Tên chương trình khuyến mại không quá 150 ký tự");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Loại khuyến mại không được để trống");

        RuleFor(x => x.DiscountValue)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị chiết khấu phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.MinOrderValue)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị đơn hàng tối thiểu phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("Thời gian kết thúc phải lớn hơn thời gian bắt đầu");
    }
}

public class PromotionUpdateRequestValidator : AbstractValidator<PromotionUpdateRequest>
{
    public PromotionUpdateRequestValidator()
    {
        RuleFor(x => x.PromotionName)
            .NotEmpty().WithMessage("Tên chương trình khuyến mại không được để trống")
            .MaximumLength(150).WithMessage("Tên chương trình khuyến mại không quá 150 ký tự");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Loại khuyến mại không được để trống");

        RuleFor(x => x.DiscountValue)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị chiết khấu phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.MinOrderValue)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị đơn hàng tối thiểu phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("Thời gian kết thúc phải lớn hơn thời gian bắt đầu");
    }
}

