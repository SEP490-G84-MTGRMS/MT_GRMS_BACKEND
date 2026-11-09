using System.Text.RegularExpressions;
using FluentValidation;
using MT_GRMS.Application.HR.Payslips.Dtos;

namespace MT_GRMS.Application.HR.Payslips.Validators;

public class GeneratePayrollBatchRequestValidator : AbstractValidator<GeneratePayrollBatchRequest>
{
    private static readonly Regex PeriodMonthRegex = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    public GeneratePayrollBatchRequestValidator()
    {
        RuleFor(x => x.PeriodMonth)
            .NotEmpty().WithMessage("Kỳ tính lương (PeriodMonth) không được để trống.")
            .Matches(PeriodMonthRegex).WithMessage("Kỳ tính lương phải có định dạng yyyy-MM (Ví dụ: 2026-11).");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Ghi chú không được vượt quá 500 ký tự.");
    }
}

public class PayPayslipRequestValidator : AbstractValidator<PayPayslipRequest>
{
    private static readonly string[] AllowedPaymentMethods = { "CASH", "VIETQR", "BANK_TRANSFER" };

    public PayPayslipRequestValidator()
    {
        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("Phương thức thanh toán không được để trống.")
            .Must(m => AllowedPaymentMethods.Contains(m.Trim().ToUpper()))
            .WithMessage("Phương thức thanh toán phải là CASH, VIETQR hoặc BANK_TRANSFER.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Ghi chú thanh toán không được vượt quá 500 ký tự.");
    }
}

public class BatchPayPayslipsRequestValidator : AbstractValidator<BatchPayPayslipsRequest>
{
    private static readonly string[] AllowedPaymentMethods = { "CASH", "VIETQR", "BANK_TRANSFER" };

    public BatchPayPayslipsRequestValidator()
    {
        RuleFor(x => x.PayslipIds)
            .NotNull().WithMessage("Danh sách phiếu lương không được null.")
            .Must(ids => ids != null && ids.Count > 0)
            .WithMessage("Phải chọn ít nhất 1 phiếu lương để thanh toán.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("Phương thức thanh toán không được để trống.")
            .Must(m => AllowedPaymentMethods.Contains(m.Trim().ToUpper()))
            .WithMessage("Phương thức thanh toán phải là CASH, VIETQR hoặc BANK_TRANSFER.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Ghi chú thanh toán không được vượt quá 500 ký tự.");
    }
}

