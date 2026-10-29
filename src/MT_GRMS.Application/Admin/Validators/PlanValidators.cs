using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Validators;

public class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    public CreatePlanRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã gói không được để trống")
            .Matches(@"^[A-Z0-9_]+$").WithMessage("Mã gói chỉ gồm chữ cái in hoa, số và dấu gạch dưới (_)");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên gói không được để trống")
            .MaximumLength(100).WithMessage("Tên gói không được vượt quá 100 ký tự");

        RuleFor(x => x.MonthlyPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá tháng phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.YearlyPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá năm phải lớn hơn hoặc bằng 0");

        When(x => x.Quotas != null, () =>
        {
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
            .MaximumLength(100).WithMessage("Tên gói không được vượt quá 100 ký tự");

        RuleFor(x => x.MonthlyPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá tháng phải lớn hơn hoặc bằng 0");

        RuleFor(x => x.YearlyPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá năm phải lớn hơn hoặc bằng 0");

        When(x => x.Quotas != null, () =>
        {
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

