using FluentValidation;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions.Validators;

internal static class PromotionValidationRules
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORDER_DISCOUNT",
        "PERCENT",
        "FIXED_AMOUNT",
        "BUY_X_GET_Y",
        "PRODUCT_DISCOUNT"
    };

    private static readonly HashSet<string> AllowedProductRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "BUY",
        "GIFT",
        "DISCOUNT"
    };

    public static bool IsAllowedType(string? type)
        => !string.IsNullOrWhiteSpace(type) && AllowedTypes.Contains(type.Trim());

    public static bool IsPercentType(string? type)
        => string.Equals(type?.Trim(), "PERCENT", StringComparison.OrdinalIgnoreCase);

    public static bool IsFixedAmountType(string? type)
        => string.Equals(type?.Trim(), "FIXED_AMOUNT", StringComparison.OrdinalIgnoreCase);

    public static void ApplyCommonRules<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> nameExpr,
        System.Linq.Expressions.Expression<Func<T, string>> typeExpr,
        Func<T, string> getType,
        System.Linq.Expressions.Expression<Func<T, decimal>> discountExpr,
        System.Linq.Expressions.Expression<Func<T, decimal>> minOrderExpr,
        System.Linq.Expressions.Expression<Func<T, decimal?>> maxDiscountExpr,
        Func<T, decimal?> getMaxDiscount,
        System.Linq.Expressions.Expression<Func<T, int?>> usageLimitExpr,
        Func<T, int?> getUsageLimit,
        System.Linq.Expressions.Expression<Func<T, decimal?>> budgetLimitExpr,
        Func<T, decimal?> getBudgetLimit,
        System.Linq.Expressions.Expression<Func<T, string?>> targetBranchesExpr,
        Func<T, string?> getTargetBranches,
        System.Linq.Expressions.Expression<Func<T, DateTime>> endDateExpr,
        System.Linq.Expressions.Expression<Func<T, DateTime>> startDateExpr,
        System.Linq.Expressions.Expression<Func<T, IEnumerable<PromotionConditionDto>?>> conditionsExpr)
    {
        validator.RuleFor(nameExpr)
            .NotEmpty().WithMessage("Tên chương trình khuyến mại không được để trống")
            .MaximumLength(150).WithMessage("Tên chương trình khuyến mại không quá 150 ký tự");

        validator.RuleFor(typeExpr)
            .NotEmpty().WithMessage("Loại khuyến mại không được để trống")
            .Must(IsAllowedType).WithMessage("Loại khuyến mại không hợp lệ (chỉ chấp nhận: ORDER_DISCOUNT, PERCENT, FIXED_AMOUNT, BUY_X_GET_Y, PRODUCT_DISCOUNT)");

        validator.RuleFor(discountExpr)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị chiết khấu phải lớn hơn hoặc bằng 0");

        validator.RuleFor(discountExpr)
            .GreaterThan(0).WithMessage("Giá trị giảm theo phần trăm phải lớn hơn 0")
            .LessThanOrEqualTo(100).WithMessage("Giá trị giảm theo phần trăm không được vượt quá 100%")
            .When(x => IsPercentType(getType(x)));

        validator.RuleFor(discountExpr)
            .GreaterThan(0).WithMessage("Giá trị giảm cố định phải lớn hơn 0")
            .When(x => IsFixedAmountType(getType(x)));

        validator.RuleFor(minOrderExpr)
            .GreaterThanOrEqualTo(0).WithMessage("Giá trị đơn hàng tối thiểu phải lớn hơn hoặc bằng 0");

        validator.RuleFor(maxDiscountExpr)
            .GreaterThanOrEqualTo(0).WithMessage("Mức giảm tối đa không được âm")
            .When(x => getMaxDiscount(x).HasValue);

        validator.RuleFor(usageLimitExpr)
            .GreaterThan(0).WithMessage("Giới hạn lượt sử dụng phải lớn hơn 0")
            .When(x => getUsageLimit(x).HasValue);

        validator.RuleFor(budgetLimitExpr)
            .GreaterThan(0).WithMessage("Ngân sách tối đa phải lớn hơn 0")
            .When(x => getBudgetLimit(x).HasValue);

        validator.RuleFor(targetBranchesExpr)
            .MaximumLength(500).WithMessage("Danh sách chi nhánh áp dụng không quá 500 ký tự")
            .When(x => !string.IsNullOrWhiteSpace(getTargetBranches(x)));

        validator.RuleFor(endDateExpr)
            .GreaterThan(startDateExpr).WithMessage("Thời gian kết thúc phải lớn hơn thời gian bắt đầu");

        validator.RuleForEach(conditionsExpr).ChildRules(cond =>
        {
            cond.RuleFor(c => c.ProductRole)
                .Must(role => string.IsNullOrWhiteSpace(role) || AllowedProductRoles.Contains(role.Trim()))
                .WithMessage("Vai trò sản phẩm khuyến mại không hợp lệ (chỉ chấp nhận: BUY, GIFT, DISCOUNT)");

            cond.RuleFor(c => c.MinQty)
                .GreaterThan(0).WithMessage("Số lượng điều kiện tối thiểu phải lớn hơn 0");

            cond.RuleFor(c => c.DiscountValue)
                .GreaterThanOrEqualTo(0).WithMessage("Giá trị giảm theo sản phẩm không được âm")
                .When(c => c.DiscountValue.HasValue);
        });
    }
}

public class PromotionCreateRequestValidator : AbstractValidator<PromotionCreateRequest>
{
    public PromotionCreateRequestValidator()
    {
        RuleFor(x => x.PromotionCode)
            .NotEmpty().WithMessage("Mã khuyến mại không được để trống")
            .MaximumLength(50).WithMessage("Mã khuyến mại không quá 50 ký tự")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Mã khuyến mại chỉ gồm chữ, số, dấu gạch ngang hoặc gạch dưới");

        RuleFor(x => x.StartDate)
            .Must(value => value.Date >= DateTime.UtcNow.Date)
            .WithMessage("Ngày bắt đầu không được là ngày trong quá khứ");

        PromotionValidationRules.ApplyCommonRules(
            this,
            x => x.PromotionName,
            x => x.Type,
            x => x.Type,
            x => x.DiscountValue,
            x => x.MinOrderValue,
            x => x.MaxDiscountAmount,
            x => x.MaxDiscountAmount,
            x => x.UsageLimit,
            x => x.UsageLimit,
            x => x.BudgetLimit,
            x => x.BudgetLimit,
            x => x.TargetBranches,
            x => x.TargetBranches,
            x => x.EndDate,
            x => x.StartDate,
            x => x.Conditions);
    }
}

public class PromotionUpdateRequestValidator : AbstractValidator<PromotionUpdateRequest>
{
    public PromotionUpdateRequestValidator()
    {
        PromotionValidationRules.ApplyCommonRules(
            this,
            x => x.PromotionName,
            x => x.Type,
            x => x.Type,
            x => x.DiscountValue,
            x => x.MinOrderValue,
            x => x.MaxDiscountAmount,
            x => x.MaxDiscountAmount,
            x => x.UsageLimit,
            x => x.UsageLimit,
            x => x.BudgetLimit,
            x => x.BudgetLimit,
            x => x.TargetBranches,
            x => x.TargetBranches,
            x => x.EndDate,
            x => x.StartDate,
            x => x.Conditions);
    }
}

