using FluentValidation;
using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Validators;

public class UpdatePlatformSettingValidator : AbstractValidator<UpdatePlatformSettingRequest>
{
    public UpdatePlatformSettingValidator()
    {
        RuleFor(x => x.TrialDays)
            .InclusiveBetween(1, 30)
            .WithMessage("Số ngày dùng thử phải từ 1 đến 30 ngày.");

        RuleFor(x => x.GracePeriodDays)
            .InclusiveBetween(1, 60)
            .WithMessage("Số ngày ân hạn phải từ 1 đến 60 ngày.");

        RuleFor(x => x.BranchAddonPriceMonthly)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Đơn giá mua thêm chi nhánh không được âm.");

        RuleFor(x => x.StorageAddonPricePerGbMonthly)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Đơn giá mua thêm dung lượng không được âm.");

        RuleFor(x => x.MasterBankCode)
            .NotEmpty().WithMessage("Mã ngân hàng thụ hưởng Master không được để trống.")
            .MaximumLength(50).WithMessage("Mã ngân hàng tối đa 50 ký tự.");

        RuleFor(x => x.MasterBankName)
            .NotEmpty().WithMessage("Tên ngân hàng thụ hưởng Master không được để trống.")
            .MaximumLength(200).WithMessage("Tên ngân hàng tối đa 200 ký tự.");

        RuleFor(x => x.MasterAccountNumber)
            .NotEmpty().WithMessage("Số tài khoản thụ hưởng Master không được để trống.")
            .MaximumLength(50).WithMessage("Số tài khoản tối đa 50 ký tự.");

        RuleFor(x => x.MasterAccountName)
            .NotEmpty().WithMessage("Tên chủ tài khoản thụ hưởng Master không được để trống.")
            .MaximumLength(200).WithMessage("Tên chủ tài khoản tối đa 200 ký tự.");

        RuleFor(x => x.SupportHotline)
            .NotEmpty().WithMessage("Hotline hỗ trợ không được để trống.")
            .MaximumLength(50).WithMessage("Hotline tối đa 50 ký tự.");

        RuleFor(x => x.SupportEmail)
            .NotEmpty().WithMessage("Email hỗ trợ không được để trống.")
            .EmailAddress().WithMessage("Định dạng email hỗ trợ không hợp lệ.")
            .MaximumLength(100).WithMessage("Email hỗ trợ tối đa 100 ký tự.");

        RuleFor(x => x.SupportZalo)
            .NotEmpty().WithMessage("Zalo CSKH không được để trống.")
            .MaximumLength(100).WithMessage("Zalo CSKH tối đa 100 ký tự.");
    }
}
