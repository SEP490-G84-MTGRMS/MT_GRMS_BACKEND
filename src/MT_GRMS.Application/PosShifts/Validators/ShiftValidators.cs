using FluentValidation;
using MT_GRMS.Application.PosShifts.Dtos;

namespace MT_GRMS.Application.PosShifts.Validators;

public class OpenShiftRequestValidator : AbstractValidator<OpenShiftRequest>
{
    public OpenShiftRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.TillId).NotEmpty();
        RuleFor(x => x.ShiftName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OpeningFloat).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class CashMovementRequestValidator : AbstractValidator<CashMovementRequest>
{
    public CashMovementRequestValidator()
    {
        RuleFor(x => x.MovementType).IsInEnum();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CreatedByName).MaximumLength(150);
    }
}

public class ConfirmCloseShiftRequestValidator : AbstractValidator<ConfirmCloseShiftRequest>
{
    public ConfirmCloseShiftRequestValidator()
    {
        RuleFor(x => x.ActualCashCounted).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.RetainedFloatAmount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.DroppedToSafeAmount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x).Must(x => Math.Abs(x.RetainedFloatAmount + x.DroppedToSafeAmount - x.ActualCashCounted) <= 0.01m)
            .WithMessage("Tiền để lại két cộng tiền nộp két phải bằng tiền thực đếm.");
        RuleFor(x => x.ReceiverUserId).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.EarlyOrLateReason).MaximumLength(500);
        RuleFor(x => x.DiscrepancyReason).MaximumLength(500);
    }
}

public class ForceCloseShiftRequestValidator : AbstractValidator<ForceCloseShiftRequest>
{
    public ForceCloseShiftRequestValidator()
    {
        RuleFor(x => x.ActualCashCounted).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.RetainedFloatAmount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.DroppedToSafeAmount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x).Must(x => Math.Abs(x.RetainedFloatAmount + x.DroppedToSafeAmount - x.ActualCashCounted) <= 0.01m)
            .WithMessage("Tiền để lại két cộng tiền nộp két phải bằng tiền thực đếm.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class ConfirmHandoverRequestValidator : AbstractValidator<ConfirmHandoverRequest>
{
    public ConfirmHandoverRequestValidator()
    {
        RuleFor(x => x.Status).Must(x => x is Domain.Enums.HandoverStatus.CONFIRMED or Domain.Enums.HandoverStatus.DISPUTED);
        RuleFor(x => x.DisputeReason).NotEmpty().When(x => x.Status == Domain.Enums.HandoverStatus.DISPUTED);
        RuleFor(x => x.DisputeReason).MaximumLength(500);
    }
}

public class StartClosingShiftRequestValidator : AbstractValidator<StartClosingShiftRequest>
{
    public StartClosingShiftRequestValidator() => RuleFor(x => x.Notes).MaximumLength(500);
}

public class CancelClosingShiftRequestValidator : AbstractValidator<CancelClosingShiftRequest>
{
    public CancelClosingShiftRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public class UpdateCountedCashRequestValidator : AbstractValidator<UpdateCountedCashRequest>
{
    public UpdateCountedCashRequestValidator()
    {
        RuleFor(x => x.CountedCash).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999_999m);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
