using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Validators;

public class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    public CreatePlanRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã gói không được để trống")
            .MaximumLength(50).WithMessage("Mã gói tối đa 50 ký tự")
            .Matches(@"\A[A-Z0-9_]+\z").WithMessage("Mã gói chỉ gồm chữ cái in hoa, số và dấu gạch dưới (_)");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên gói không được để trống")
            .Must(name => name?.Trim().Length >= 2).WithMessage("Tên gói tối thiểu 2 ký tự")
            .MaximumLength(100).WithMessage("Tên gói không được vượt quá 100 ký tự");

        RuleFor(x => x.MonthlyPrice)
            .PrecisionScale(18, 2, true).WithMessage("Giá tối đa 16 chữ số nguyên và 2 chữ số thập phân")
            .GreaterThanOrEqualTo(0).WithMessage("Giá tháng phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.YearlyPrice)
            .PrecisionScale(18, 2, true).WithMessage("Giá tối đa 16 chữ số nguyên và 2 chữ số thập phân")
            .GreaterThanOrEqualTo(0).WithMessage("Giá năm phải lớn hơn hoặc bằng 0");

        When(x => x.Quotas != null, () =>
        {
            RuleFor(x => x.Quotas!.BackupFrequency).Must(v => v is "Hourly" or "Daily" or "Weekly")
                .WithMessage("Chu kỳ sao lưu không hợp lệ");

            RuleFor(x => x.Quotas!.MaxBranches)
                .Must(v => v == -1 || v >= 1)
                .WithMessage("Số chi nhánh tối thiểu là 1 hoặc -1 (vô hạn)");

            RuleFor(x => x.Quotas!.MaxEmployees)
                .Must(v => v == -1 || v >= 1)
                .WithMessage("Số nhân viên tối thiểu là 1 hoặc -1 (vô hạn)");

            RuleFor(x => x.Quotas!.MaxStorageGb)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Dung lượng lưu trữ tối thiểu là 1 GB");
        });
    }
}

public class UpdatePlanRequestValidator : AbstractValidator<UpdatePlanRequest>
{
    public UpdatePlanRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên gói không được để trống")
            .Must(name => name?.Trim().Length >= 2).WithMessage("Tên gói tối thiểu 2 ký tự")
            .MaximumLength(100).WithMessage("Tên gói không được vượt quá 100 ký tự");

        RuleFor(x => x.MonthlyPrice)
            .PrecisionScale(18, 2, true).WithMessage("Giá tối đa 16 chữ số nguyên và 2 chữ số thập phân")
            .GreaterThanOrEqualTo(0).WithMessage("Giá tháng phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.YearlyPrice)
            .PrecisionScale(18, 2, true).WithMessage("Giá tối đa 16 chữ số nguyên và 2 chữ số thập phân")
            .GreaterThanOrEqualTo(0).WithMessage("Giá năm phải lớn hơn hoặc bằng 0");

        When(x => x.Quotas != null, () =>
        {
            RuleFor(x => x.Quotas!.BackupFrequency).Must(v => v is "Hourly" or "Daily" or "Weekly")
                .WithMessage("Chu kỳ sao lưu không hợp lệ");

            RuleFor(x => x.Quotas!.MaxBranches)
                .Must(v => v == -1 || v >= 1)
                .WithMessage("Số chi nhánh tối thiểu là 1 hoặc -1 (vô hạn)");

            RuleFor(x => x.Quotas!.MaxEmployees)
                .Must(v => v == -1 || v >= 1)
                .WithMessage("Số nhân viên tối thiểu là 1 hoặc -1 (vô hạn)");

            RuleFor(x => x.Quotas!.MaxStorageGb)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Dung lượng lưu trữ tối thiểu là 1 GB");
        });
    }
}

