using FluentValidation;
using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Application.Suppliers.Validators;

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nhà cung cấp không được để trống.")
            .MaximumLength(200).WithMessage("Tên nhà cung cấp tối đa 200 ký tự.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.");

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không hợp lệ.")
                .MaximumLength(150).WithMessage("Email tối đa 150 ký tự.");
        });

        RuleFor(x => x.PaymentTermDays)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn thanh toán nợ phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức nợ phải lớn hơn hoặc bằng 0.");
    }
}

public class UpdateSupplierCommandValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nhà cung cấp không được để trống.")
            .MaximumLength(200).WithMessage("Tên nhà cung cấp tối đa 200 ký tự.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.");

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không hợp lệ.")
                .MaximumLength(150).WithMessage("Email tối đa 150 ký tự.");
        });

        RuleFor(x => x.PaymentTermDays)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn thanh toán nợ phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức nợ phải lớn hơn hoặc bằng 0.");
    }
}

public class RecordAPTransactionCommandValidator : AbstractValidator<RecordAPTransactionCommand>
{
    public RecordAPTransactionCommandValidator()
    {
        RuleFor(x => x.SupplierId)
            .NotEmpty().WithMessage("ID nhà cung cấp không được để trống.");

        RuleFor(x => x.DocNo)
            .NotEmpty().WithMessage("Mã chứng từ không được để trống.")
            .MaximumLength(100).WithMessage("Mã chứng từ tối đa 100 ký tự.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Số tiền giao dịch phải lớn hơn 0.");

        RuleFor(x => x.Direction)
            .Must(d => d == 1 || d == -1).WithMessage("Chiều biến động công nợ chỉ có thể là +1 (tăng nợ) hoặc -1 (giảm nợ).");
    }
}

public class UpdateSupplierDeliveryConfigCommandValidator : AbstractValidator<UpdateSupplierDeliveryConfigCommand>
{
    public UpdateSupplierDeliveryConfigCommandValidator()
    {
        RuleFor(x => x.LeadTimeHours)
            .GreaterThanOrEqualTo(0).WithMessage("Thời gian chuẩn bị giao hàng (Lead Time) phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.LeadTimeDays)
            .GreaterThanOrEqualTo(0).WithMessage("Lead time (ngày) phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.CutoffDayOffset)
            .GreaterThanOrEqualTo(0).WithMessage("Cutoff day offset phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.MinOrderQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Số lượng đặt tối thiểu (MOQ) phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.UrgentFeePercent)
            .InclusiveBetween(0, 100).WithMessage("Phụ phí hỏa tốc phải từ 0% đến 100%.");
    }
}
