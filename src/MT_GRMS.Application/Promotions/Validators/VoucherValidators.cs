using FluentValidation;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions.Validators;

public class VoucherBatchCreateRequestValidator : AbstractValidator<VoucherBatchCreateRequest>
{
    private static readonly HashSet<string> AllowedDiscountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FIXED_AMOUNT",
        "PERCENT"
    };

    public VoucherBatchCreateRequestValidator()
    {
        RuleFor(x => x.BatchCode)
            .NotEmpty().WithMessage("Mã đợt voucher không được để trống")
            .MaximumLength(50).WithMessage("Mã đợt voucher không quá 50 ký tự")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Mã đợt voucher chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên đợt voucher không được để trống")
            .MaximumLength(150).WithMessage("Tên đợt voucher không quá 150 ký tự");

        RuleFor(x => x.DiscountType)
            .NotEmpty().WithMessage("Hình thức giảm giá không được để trống")
            .Must(dt => !string.IsNullOrWhiteSpace(dt) && AllowedDiscountTypes.Contains(dt.Trim()))
            .WithMessage("Hình thức giảm giá voucher không hợp lệ (chỉ chấp nhận: FIXED_AMOUNT, PERCENT)");

        RuleFor(x => x.Prefix)
            .NotEmpty().WithMessage("Tiền tố mã voucher không được để trống")
            .MaximumLength(20).WithMessage("Tiền tố mã voucher không quá 20 ký tự")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Tiền tố voucher chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Ghi chú voucher không quá 1.000 ký tự")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.TotalQuantity)
            .GreaterThan(0).WithMessage("Tổng số lượng voucher phải lớn hơn 0")
            .LessThanOrEqualTo(10000).WithMessage("Không được tạo quá 10.000 voucher trong một đợt");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("Giá trị giảm giá phải lớn hơn 0");

        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100).WithMessage("Tỷ lệ giảm theo phần trăm không được vượt quá 100%")
            .When(x => string.Equals(x.DiscountType?.Trim(), "PERCENT", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.MinOrderValue)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị đơn hàng tối thiểu phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThan(0).WithMessage("Mức giảm tối đa phải lớn hơn 0")
            .When(x => x.MaxDiscountAmount.HasValue);

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("Thời gian kết thúc phải lớn hơn thời gian bắt đầu");

        RuleFor(x => x.StartDate)
            .Must(value => value.Date >= DateTime.UtcNow.Date)
            .WithMessage("Ngày bắt đầu không được là ngày trong quá khứ");
    }
}

public class ValidateVoucherRequestValidator : AbstractValidator<ValidateVoucherRequest>
{
    public ValidateVoucherRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã voucher không được để trống")
            .MaximumLength(50).WithMessage("Mã voucher không quá 50 ký tự");
        RuleFor(x => x.CartTotal)
            .GreaterThanOrEqualTo(0).WithMessage("Tổng tiền giỏ hàng không được âm");
        RuleFor(x => x.CustomerId)
            .Must(value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _))
            .WithMessage("CustomerId không đúng định dạng GUID");
    }
}

public class GenerateVoucherCodesRequestValidator : AbstractValidator<GenerateVoucherCodesRequest>
{
    public GenerateVoucherCodesRequestValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Số lượng mã voucher cần tạo phải lớn hơn 0")
            .LessThanOrEqualTo(10000).WithMessage("Không được tạo quá 10.000 mã voucher trong một lần");

        RuleFor(x => x.Prefix)
            .MaximumLength(20).WithMessage("Tiền tố mã voucher không quá 20 ký tự")
            .When(x => !string.IsNullOrWhiteSpace(x.Prefix));
    }
}

public class RedeemVoucherRequestValidator : AbstractValidator<RedeemVoucherRequest>
{
    public RedeemVoucherRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã voucher không được để trống")
            .MaximumLength(50).WithMessage("Mã voucher không quá 50 ký tự");

        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("OrderId không được để trống");

        RuleFor(x => x.OrderCode)
            .NotEmpty().WithMessage("Mã đơn hàng không được để trống")
            .MaximumLength(50).WithMessage("Mã đơn hàng không quá 50 ký tự");
    }
}

