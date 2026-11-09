using FluentValidation;
using MT_GRMS.Application.PosOrders.Dtos;

namespace MT_GRMS.Application.PosOrders.Validators;

public class OrderCreateRequestValidator : AbstractValidator<OrderCreateRequest>
{
    public OrderCreateRequestValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty()
            .WithMessage("Chi nhánh (BranchId) không được để trống.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .WithMessage("Phương thức thanh toán không được để trống.");

        RuleFor(x => x.Items)
            .NotNull()
            .WithMessage("Giỏ hàng không được để trống.")
            .Must(items => items != null && items.Count > 0)
            .WithMessage("Giỏ hàng phải có ít nhất 1 mặt hàng.")
            .Must(items => items == null || items.Count == 0 || items.Any(i => !i.IsGift))
            .WithMessage("Đơn hàng bắt buộc phải có ít nhất 1 mặt hàng mua trả tiền (không thể chỉ gồm quà tặng khuyến mãi).");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty()
                .WithMessage("Mã sản phẩm không được để trống.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithMessage("Số lượng sản phẩm phải lớn hơn 0.");

            item.RuleFor(i => i.UnitPrice)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Đơn giá sản phẩm không được âm.");

            item.RuleFor(i => i.DiscountAmount)
                .GreaterThanOrEqualTo(0)
                .When(i => i.DiscountAmount.HasValue)
                .WithMessage("Giảm giá mặt hàng không được âm.");
        });

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DiscountAmount.HasValue)
            .WithMessage("Chiết khấu đơn hàng không được âm.");

        RuleFor(x => x.CashTendered)
            .GreaterThanOrEqualTo(0)
            .When(x => x.CashTendered.HasValue)
            .WithMessage("Số tiền khách đưa không được âm.");

        RuleFor(x => x.PromotionDiscountAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PromotionDiscountAmount.HasValue)
            .WithMessage("Tiền giảm khuyến mãi không được âm.");

        RuleFor(x => x.VoucherDiscountAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.VoucherDiscountAmount.HasValue)
            .WithMessage("Tiền giảm voucher không được âm.");

        RuleFor(x => x.PointsDiscountAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PointsDiscountAmount.HasValue)
            .WithMessage("Tiền quy đổi điểm thưởng không được âm.");
    }
}
