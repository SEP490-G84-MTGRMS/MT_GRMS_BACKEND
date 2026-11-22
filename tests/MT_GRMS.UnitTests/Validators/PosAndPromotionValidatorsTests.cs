using System;
using System.Collections.Generic;
using FluentValidation.TestHelper;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.PosShifts.Validators;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Application.Promotions.Validators;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Validators;

public class PosAndPromotionValidatorsTests
{
    [Fact]
    public void OpenShiftRequestValidator_ShouldValidate()
    {
        var validator = new OpenShiftRequestValidator();

        var valid = new OpenShiftRequest(
            BranchId: Guid.NewGuid(),
            TillId: Guid.NewGuid(),
            ShiftName: "Morning Shift",
            OpeningFloat: 500000m,
            Notes: "Note"
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new OpenShiftRequest(
            BranchId: Guid.Empty,
            TillId: Guid.Empty,
            ShiftName: "",
            OpeningFloat: -100m,
            Notes: new string('N', 501)
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.BranchId);
        res.ShouldHaveValidationErrorFor(x => x.TillId);
        res.ShouldHaveValidationErrorFor(x => x.ShiftName);
        res.ShouldHaveValidationErrorFor(x => x.OpeningFloat);
        res.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void CashMovementRequestValidator_ShouldValidate()
    {
        var validator = new CashMovementRequestValidator();

        var valid = new CashMovementRequest(
            MovementType: CashMovementType.IN,
            Category: CashMovementCategory.CASH_DEPOSIT,
            Amount: 100000m,
            Reason: "Top up float for day",
            CreatedByName: "Admin"
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new CashMovementRequest(
            MovementType: (CashMovementType)99,
            Category: (CashMovementCategory)99,
            Amount: 0m,
            Reason: "",
            CreatedByName: new string('A', 151)
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.Amount);
        res.ShouldHaveValidationErrorFor(x => x.Reason);
        res.ShouldHaveValidationErrorFor(x => x.CreatedByName);
    }

    [Fact]
    public void ConfirmCloseShiftRequestValidator_ShouldValidate()
    {
        var validator = new ConfirmCloseShiftRequestValidator();

        var valid = new ConfirmCloseShiftRequest(
            ActualCashCounted: 1000000m,
            RetainedFloatAmount: 300000m,
            DroppedToSafeAmount: 700000m,
            ReceiverUserId: "user-123",
            Notes: "All balanced",
            EarlyOrLateReason: null,
            DiscrepancyReason: null
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalidSum = new ConfirmCloseShiftRequest(
            ActualCashCounted: 1000000m,
            RetainedFloatAmount: 200000m,
            DroppedToSafeAmount: 700000m, // 200k + 700k != 1000k
            ReceiverUserId: null,
            Notes: new string('N', 501)
        );
        var res = validator.TestValidate(invalidSum);
        res.ShouldHaveValidationErrorFor(x => x);
        res.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void ForceCloseShiftRequestValidator_ShouldValidate()
    {
        var validator = new ForceCloseShiftRequestValidator();

        var valid = new ForceCloseShiftRequest(
            ActualCashCounted: 500000m,
            RetainedFloatAmount: 200000m,
            DroppedToSafeAmount: 300000m,
            Reason: "Power outage emergency shutdown",
            Notes: "Closed by manager"
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new ForceCloseShiftRequest(
            ActualCashCounted: 500000m,
            RetainedFloatAmount: 200000m,
            DroppedToSafeAmount: 200000m, // 400k != 500k
            Reason: "",
            Notes: new string('N', 501)
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x);
        res.ShouldHaveValidationErrorFor(x => x.Reason);
        res.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void ConfirmHandoverRequestValidator_ShouldValidate()
    {
        var validator = new ConfirmHandoverRequestValidator();

        var validConfirmed = new ConfirmHandoverRequest(
            Status: HandoverStatus.CONFIRMED,
            DisputeReason: null
        );
        validator.TestValidate(validConfirmed).ShouldNotHaveAnyValidationErrors();

        var validDisputed = new ConfirmHandoverRequest(
            Status: HandoverStatus.DISPUTED,
            DisputeReason: "Float is missing 50,000 VND"
        );
        validator.TestValidate(validDisputed).ShouldNotHaveAnyValidationErrors();

        var invalidDisputed = new ConfirmHandoverRequest(
            Status: HandoverStatus.DISPUTED,
            DisputeReason: ""
        );
        validator.TestValidate(invalidDisputed).ShouldHaveValidationErrorFor(x => x.DisputeReason);
    }

    [Fact]
    public void MinorShiftValidators_ShouldValidate()
    {
        var startValidator = new StartClosingShiftRequestValidator();
        startValidator.TestValidate(new StartClosingShiftRequest("Note")).ShouldNotHaveAnyValidationErrors();
        startValidator.TestValidate(new StartClosingShiftRequest(new string('N', 501)))
            .ShouldHaveValidationErrorFor(x => x.Notes);

        var cancelValidator = new CancelClosingShiftRequestValidator();
        cancelValidator.TestValidate(new CancelClosingShiftRequest("Reason")).ShouldNotHaveAnyValidationErrors();
        cancelValidator.TestValidate(new CancelClosingShiftRequest(new string('R', 501)))
            .ShouldHaveValidationErrorFor(x => x.Reason);

        var updateCountedValidator = new UpdateCountedCashRequestValidator();
        updateCountedValidator.TestValidate(new UpdateCountedCashRequest(1000m, "OK"))
            .ShouldNotHaveAnyValidationErrors();
        var updateInvalid = updateCountedValidator.TestValidate(new UpdateCountedCashRequest(-1m, new string('N', 501)));
        updateInvalid.ShouldHaveValidationErrorFor(x => x.CountedCash);
        updateInvalid.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void UpdateLoyaltyConfigRequestValidator_ShouldValidate()
    {
        var validator = new UpdateLoyaltyConfigRequestValidator();

        var valid = new UpdateLoyaltyConfigRequest(
            IsEnabled: true,
            EarningRateAmount: 10000m,
            EarningPoints: 1,
            RedemptionRatePoints: 100,
            RedemptionRateAmount: 10000m,
            MinRedemptionPoints: 50,
            AllowPointsWithPromotions: false
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpdateLoyaltyConfigRequest(
            IsEnabled: true,
            EarningRateAmount: 0m,
            EarningPoints: -1,
            RedemptionRatePoints: 0,
            RedemptionRateAmount: -5000m,
            MinRedemptionPoints: -10,
            AllowPointsWithPromotions: false
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.EarningRateAmount);
        res.ShouldHaveValidationErrorFor(x => x.EarningPoints);
        res.ShouldHaveValidationErrorFor(x => x.RedemptionRatePoints);
        res.ShouldHaveValidationErrorFor(x => x.RedemptionRateAmount);
        res.ShouldHaveValidationErrorFor(x => x.MinRedemptionPoints);
    }

    [Fact]
    public void PromotionValidators_Create_ShouldValidate()
    {
        var validator = new PromotionCreateRequestValidator();
        var startDate = DateTime.UtcNow.Date.AddDays(1);
        var endDate = startDate.AddDays(7);

        var valid = new PromotionCreateRequest(
            PromotionCode: "PROMO_SUMMER",
            PromotionName: "Summer Sale",
            Type: "PERCENT",
            DiscountValue: 15m,
            MinOrderValue: 50000m,
            MaxDiscountAmount: 100000m,
            UsageLimit: 100,
            StartDate: startDate,
            EndDate: endDate,
            IsActive: true,
            Description: "Summer promo",
            TargetBranches: "ALL",
            BudgetLimit: 5000000m,
            Conditions: new List<PromotionConditionDto>
            {
                new(null, Guid.NewGuid(), "Product A", "BUY", 2, null, null)
            }
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new PromotionCreateRequest(
            PromotionCode: "INVALID CODE@#",
            PromotionName: "",
            Type: "UNKNOWN_TYPE",
            DiscountValue: -1m,
            MinOrderValue: -10m,
            MaxDiscountAmount: -1m,
            UsageLimit: 0,
            StartDate: DateTime.UtcNow.Date.AddDays(-2), // Past date
            EndDate: DateTime.UtcNow.Date.AddDays(-5), // End < Start
            IsActive: true,
            Description: null,
            TargetBranches: new string('B', 501),
            BudgetLimit: -100m,
            Conditions: new List<PromotionConditionDto>
            {
                new(null, Guid.NewGuid(), "Product B", "INVALID_ROLE", -1, -5m, null)
            }
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.PromotionCode);
        res.ShouldHaveValidationErrorFor(x => x.PromotionName);
        res.ShouldHaveValidationErrorFor(x => x.Type);
        res.ShouldHaveValidationErrorFor(x => x.DiscountValue);
        res.ShouldHaveValidationErrorFor(x => x.MinOrderValue);
        res.ShouldHaveValidationErrorFor(x => x.MaxDiscountAmount);
        res.ShouldHaveValidationErrorFor(x => x.UsageLimit);
        res.ShouldHaveValidationErrorFor(x => x.StartDate);
        res.ShouldHaveValidationErrorFor(x => x.EndDate);
        res.ShouldHaveValidationErrorFor(x => x.TargetBranches);
        res.ShouldHaveValidationErrorFor(x => x.BudgetLimit);
        res.ShouldHaveValidationErrorFor("Conditions[0].ProductRole");
        res.ShouldHaveValidationErrorFor("Conditions[0].MinQty");
        res.ShouldHaveValidationErrorFor("Conditions[0].DiscountValue");

        var invalidPercent = valid with { Type = "PERCENT", DiscountValue = 120m };
        validator.TestValidate(invalidPercent).ShouldHaveValidationErrorFor(x => x.DiscountValue);
    }

    [Fact]
    public void PromotionValidators_Update_FixedAmount()
    {
        var validator = new PromotionUpdateRequestValidator();
        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(10);

        var valid = new PromotionUpdateRequest(
            PromotionName: "Fixed Discount",
            Type: "FIXED_AMOUNT",
            DiscountValue: 20000m,
            MinOrderValue: 100000m,
            MaxDiscountAmount: null,
            UsageLimit: null,
            StartDate: startDate,
            EndDate: endDate,
            IsActive: true
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalidZeroDiscount = valid with { DiscountValue = 0m };
        validator.TestValidate(invalidZeroDiscount).ShouldHaveValidationErrorFor(x => x.DiscountValue);
    }

    [Fact]
    public void VoucherValidators_BatchCreate_ShouldValidate()
    {
        var validator = new VoucherBatchCreateRequestValidator();
        var startDate = DateTime.UtcNow.Date.AddDays(1);
        var endDate = startDate.AddDays(30);

        var valid = new VoucherBatchCreateRequest(
            BatchCode: "BATCH_01",
            Name: "New Year Vouchers",
            DiscountType: "PERCENT",
            DiscountValue: 20m,
            MinOrderValue: 100000m,
            MaxDiscountAmount: 50000m,
            TotalQuantity: 500,
            StartDate: startDate,
            EndDate: endDate,
            Prefix: "NY2026",
            Description: "New Year Promo"
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new VoucherBatchCreateRequest(
            BatchCode: "invalid code!",
            Name: "",
            DiscountType: "INVALID_DISCOUNT",
            DiscountValue: -1m,
            MinOrderValue: -1m,
            MaxDiscountAmount: 0m,
            TotalQuantity: 10001, // > 10000
            StartDate: DateTime.UtcNow.Date.AddDays(-3),
            EndDate: DateTime.UtcNow.Date.AddDays(-4),
            Prefix: "INVALID PREFIX!",
            Description: new string('D', 1001)
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.BatchCode);
        res.ShouldHaveValidationErrorFor(x => x.Name);
        res.ShouldHaveValidationErrorFor(x => x.DiscountType);
        res.ShouldHaveValidationErrorFor(x => x.DiscountValue);
        res.ShouldHaveValidationErrorFor(x => x.MinOrderValue);
        res.ShouldHaveValidationErrorFor(x => x.MaxDiscountAmount);
        res.ShouldHaveValidationErrorFor(x => x.TotalQuantity);
        res.ShouldHaveValidationErrorFor(x => x.StartDate);
        res.ShouldHaveValidationErrorFor(x => x.EndDate);
        res.ShouldHaveValidationErrorFor(x => x.Prefix);
        res.ShouldHaveValidationErrorFor(x => x.Description);

        var invalidPercent = valid with { DiscountType = "PERCENT", DiscountValue = 120m };
        validator.TestValidate(invalidPercent).ShouldHaveValidationErrorFor(x => x.DiscountValue);
    }

    [Fact]
    public void VoucherValidators_ValidateRedeemAndGenerate_ShouldValidate()
    {
        var valValidator = new ValidateVoucherRequestValidator();
        var validVal = new ValidateVoucherRequest { Code = "VOUCHER1", CartTotal = 50000m, CustomerId = Guid.NewGuid().ToString() };
        valValidator.TestValidate(validVal).ShouldNotHaveAnyValidationErrors();

        var invalidVal = new ValidateVoucherRequest { Code = "", CartTotal = -10m, CustomerId = "invalid-guid" };
        var valRes = valValidator.TestValidate(invalidVal);
        valRes.ShouldHaveValidationErrorFor(x => x.Code);
        valRes.ShouldHaveValidationErrorFor(x => x.CartTotal);
        valRes.ShouldHaveValidationErrorFor(x => x.CustomerId);

        var genValidator = new GenerateVoucherCodesRequestValidator();
        var validGen = new GenerateVoucherCodesRequest(100, "PR_");
        genValidator.TestValidate(validGen).ShouldNotHaveAnyValidationErrors();

        var invalidGen = new GenerateVoucherCodesRequest(0, new string('P', 25));
        var genRes = genValidator.TestValidate(invalidGen);
        genRes.ShouldHaveValidationErrorFor(x => x.Quantity);
        genRes.ShouldHaveValidationErrorFor(x => x.Prefix);

        var redeemValidator = new RedeemVoucherRequestValidator();
        var validRedeem = new RedeemVoucherRequest("VOUCHER1", Guid.NewGuid(), "ORD-001");
        redeemValidator.TestValidate(validRedeem).ShouldNotHaveAnyValidationErrors();

        var invalidRedeem = new RedeemVoucherRequest("", Guid.Empty, "");
        var redeemRes = redeemValidator.TestValidate(invalidRedeem);
        redeemRes.ShouldHaveValidationErrorFor(x => x.Code);
        redeemRes.ShouldHaveValidationErrorFor(x => x.OrderId);
        redeemRes.ShouldHaveValidationErrorFor(x => x.OrderCode);
    }
}
