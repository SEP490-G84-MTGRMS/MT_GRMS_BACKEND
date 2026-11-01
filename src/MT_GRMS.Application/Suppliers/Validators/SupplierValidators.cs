using FluentValidation;
using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Application.Suppliers.Validators;

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nhà cung cấp không được để trống.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Tên nhà cung cấp không được chỉ chứa khoảng trắng.")
            .MaximumLength(200).WithMessage("Tên nhà cung cấp tối đa 200 ký tự.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Số điện thoại không được chỉ chứa khoảng trắng.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Trạng thái nhà cung cấp không hợp lệ.");

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không hợp lệ.")
                .MaximumLength(150).WithMessage("Email tối đa 150 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.TaxCode), () =>
        {
            RuleFor(x => x.TaxCode)
                .MaximumLength(50).WithMessage("Mã số thuế tối đa 50 ký tự.");
        });

        RuleFor(x => x.PaymentTermDays)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn thanh toán nợ phải lớn hơn hoặc bằng 0.");

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0).WithMessage("Hạn mức nợ phải lớn hơn hoặc bằng 0.");
    }
}

public class UpdateBasicSupplierCommandValidator : AbstractValidator<UpdateBasicSupplierCommand>
{
    public UpdateBasicSupplierCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nhà cung cấp không được để trống.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Tên nhà cung cấp không được chỉ chứa khoảng trắng.")
            .MaximumLength(200).WithMessage("Tên nhà cung cấp tối đa 200 ký tự.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Số điện thoại không được chỉ chứa khoảng trắng.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Trạng thái nhà cung cấp không hợp lệ.");

        When(x => !string.IsNullOrWhiteSpace(x.ContactPerson), () =>
        {
            RuleFor(x => x.ContactPerson)
                .MaximumLength(100).WithMessage("Người liên hệ tối đa 100 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.TaxCode), () =>
        {
            RuleFor(x => x.TaxCode)
                .MaximumLength(50).WithMessage("Mã số thuế tối đa 50 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Address), () =>
        {
            RuleFor(x => x.Address)
                .MaximumLength(300).WithMessage("Địa chỉ không vượt quá 300 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Địa chỉ email không hợp lệ.")
                .MaximumLength(150).WithMessage("Email tối đa 150 ký tự.");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Notes), () =>
        {
            RuleFor(x => x.Notes)
                .MaximumLength(500).WithMessage("Ghi chú không vượt quá 500 ký tự.");
        });
    }
}

public class UpdateSupplierBankCommandValidator : AbstractValidator<UpdateSupplierBankCommand>
{
    public UpdateSupplierBankCommandValidator()
    {
        When(x => !string.IsNullOrWhiteSpace(x.BankName) || !string.IsNullOrWhiteSpace(x.BankAccountNumber) || !string.IsNullOrWhiteSpace(x.BankAccountHolder), () =>
        {
            RuleFor(x => x.BankName)
                .NotEmpty().WithMessage("Vui lòng chọn hoặc nhập tên ngân hàng.")
                .MaximumLength(100).WithMessage("Tên ngân hàng tối đa 100 ký tự.");

            RuleFor(x => x.BankAccountNumber)
                .NotEmpty().WithMessage("Vui lòng nhập số tài khoản ngân hàng.")
                .Matches(@"^[0-9]+$").WithMessage("Số tài khoản ngân hàng chỉ được chứa chữ số.")
                .MaximumLength(50).WithMessage("Số tài khoản tối đa 50 ký tự.");

            RuleFor(x => x.BankAccountHolder)
                .NotEmpty().WithMessage("Vui lòng nhập tên chủ tài khoản.")
                .MaximumLength(100).WithMessage("Tên chủ tài khoản tối đa 100 ký tự.");
        });
    }
}

public class UpdateSupplierVatCommandValidator : AbstractValidator<UpdateSupplierVatCommand>
{
    public UpdateSupplierVatCommandValidator()
    {
        When(x => !string.IsNullOrWhiteSpace(x.CompanyName) || !string.IsNullOrWhiteSpace(x.TaxCode) || !string.IsNullOrWhiteSpace(x.InvoiceAddress), () =>
        {
            RuleFor(x => x.CompanyName)
                .NotEmpty().WithMessage("Tên công ty xuất hóa đơn không được để trống.")
                .MaximumLength(200).WithMessage("Tên công ty tối đa 200 ký tự.");

            RuleFor(x => x.TaxCode)
                .NotEmpty().WithMessage("Mã số thuế không được để trống.")
                .Matches(@"^[0-9]{10}(-[0-9]{3})?$|^[0-9]{13}$").WithMessage("Mã số thuế phải đúng định dạng 10 hoặc 13 chữ số.");

            When(x => !string.IsNullOrWhiteSpace(x.InvoiceAddress), () =>
            {
                RuleFor(x => x.InvoiceAddress)
                    .MaximumLength(300).WithMessage("Địa chỉ xuất hóa đơn tối đa 300 ký tự.");
            });
        });
    }
}

public class UpdateSupplierCommandValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên nhà cung cấp không được để trống.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Tên nhà cung cấp không được chỉ chứa khoảng trắng.")
            .MaximumLength(200).WithMessage("Tên nhà cung cấp tối đa 200 ký tự.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Số điện thoại không được chỉ chứa khoảng trắng.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.");

        When(x => x.Status.HasValue, () =>
        {
            RuleFor(x => x.Status!.Value)
                .IsInEnum().WithMessage("Trạng thái nhà cung cấp không hợp lệ.");
        });

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
