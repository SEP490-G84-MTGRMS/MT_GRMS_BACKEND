using FluentValidation;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions.Validators;

public class VoucherBatchCreateRequestValidator : AbstractValidator<VoucherBatchCreateRequest>
{
    public VoucherBatchCreateRequestValidator()
    {
        RuleFor(x => x.BatchCode)
            .NotEmpty().WithMessage("Mã đợt voucher không được để trống")
            .MaximumLength(50).WithMessage("Mã đợt voucher không quá 50 ký tự");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên đợt voucher không được để trống")
            .MaximumLength(150).WithMessage("Tên đợt voucher không quá 150 ký tự");

        RuleFor(x => x.Prefix)
            .NotEmpty().WithMessage("Tiền tố mã voucher không được để trống")
            .MaximumLength(20).WithMessage("Tiền tố mã voucher không quá 20 ký tự");

        RuleFor(x => x.TotalQuantity)
            .GreaterThan(0).WithMessage("Tổng số lượng voucher phải lớn hơn 0")
            .LessThanOrEqualTo(10000).WithMessage("Không được tạo quá 10.000 voucher trong một đợt");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("Giá trị giảm giá phải lớn hơn 0");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("Thời gian kết thúc phải lớn hơn thời gian bắt đầu");
    }
}

