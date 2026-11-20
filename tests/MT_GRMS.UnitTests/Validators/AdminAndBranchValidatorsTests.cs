using System;
using System.Collections.Generic;
using FluentValidation.TestHelper;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Batches.Validators;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Branches.Validators;
using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.BusinessDays.Validators;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Cashbook.Validators;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Application.Inventory.Validators;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Validators;

public class AdminAndBranchValidatorsTests
{
    [Fact]
    public void CreatePlanRequestValidator_ValidRequest_ShouldNotHaveErrors()
    {
        var validator = new CreatePlanRequestValidator();
        var model = new CreatePlanRequest(
            Code: "PRO_PLAN_01",
            Name: "Professional Plan",
            Description: "Great plan",
            MonthlyPrice: 100000m,
            YearlyPrice: 1000000m,
            Quotas: new PlanQuotaDto(MaxBranches: 5, MaxEmployees: 10, MaxStorageGb: 10),
            Features: new PlanFeatureDto()
        );

        var result = validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("", "Mã gói không được để trống")]
    [InlineData("plan-lower", "Mã gói chỉ gồm chữ cái in hoa, số và dấu gạch dưới (_)")]
    [InlineData("PLAN@123", "Mã gói chỉ gồm chữ cái in hoa, số và dấu gạch dưới (_)")]
    public void CreatePlanRequestValidator_InvalidCode_ShouldHaveError(string code, string expectedMessage)
    {
        var validator = new CreatePlanRequestValidator();
        var model = new CreatePlanRequest(
            Code: code,
            Name: "Plan",
            Description: null,
            MonthlyPrice: 0,
            YearlyPrice: 0,
            Quotas: null,
            Features: null
        );

        var result = validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Code).WithErrorMessage(expectedMessage);
    }

    [Fact]
    public void CreatePlanRequestValidator_InvalidNameAndPrices_ShouldHaveErrors()
    {
        var validator = new CreatePlanRequestValidator();
        var model = new CreatePlanRequest(
            Code: "VALID_CODE",
            Name: new string('A', 101),
            Description: null,
            MonthlyPrice: -1m,
            YearlyPrice: -50m,
            Quotas: null,
            Features: null
        );

        var result = validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.MonthlyPrice);
        result.ShouldHaveValidationErrorFor(x => x.YearlyPrice);
    }

    [Fact]
    public void CreatePlanRequestValidator_InvalidQuotas_ShouldHaveErrors()
    {
        var validator = new CreatePlanRequestValidator();
        var model = new CreatePlanRequest(
            Code: "PLAN_01",
            Name: "Standard",
            Description: null,
            MonthlyPrice: 0,
            YearlyPrice: 0,
            Quotas: new PlanQuotaDto(MaxBranches: 0, MaxEmployees: -2, MaxStorageGb: 0),
            Features: null
        );

        var result = validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Quotas!.MaxBranches);
        result.ShouldHaveValidationErrorFor(x => x.Quotas!.MaxEmployees);
        result.ShouldHaveValidationErrorFor(x => x.Quotas!.MaxStorageGb);
    }

    [Fact]
    public void CreatePlanRequestValidator_QuotasInfinite_ShouldPass()
    {
        var validator = new CreatePlanRequestValidator();
        var model = new CreatePlanRequest(
            Code: "UNLIMITED",
            Name: "Unlimited Plan",
            Description: null,
            MonthlyPrice: 500000m,
            YearlyPrice: 5000000m,
            Quotas: new PlanQuotaDto(MaxBranches: -1, MaxEmployees: -1, MaxStorageGb: 100),
            Features: null
        );

        var result = validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdatePlanRequestValidator_ValidAndInvalidScenarios()
    {
        var validator = new UpdatePlanRequestValidator();
        var valid = new UpdatePlanRequest(
            Name: "Valid Plan",
            Description: "Desc",
            MonthlyPrice: 200000m,
            YearlyPrice: 2000000m,
            Quotas: new PlanQuotaDto(MaxBranches: 2, MaxEmployees: 4, MaxStorageGb: 5),
            Features: null
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpdatePlanRequest(
            Name: "",
            Description: null,
            MonthlyPrice: -10m,
            YearlyPrice: -20m,
            Quotas: new PlanQuotaDto(MaxBranches: 0, MaxEmployees: 0, MaxStorageGb: 0),
            Features: null
        );
        var invalidResult = validator.TestValidate(invalid);
        invalidResult.ShouldHaveValidationErrorFor(x => x.Name);
        invalidResult.ShouldHaveValidationErrorFor(x => x.MonthlyPrice);
        invalidResult.ShouldHaveValidationErrorFor(x => x.YearlyPrice);
        invalidResult.ShouldHaveValidationErrorFor(x => x.Quotas!.MaxBranches);
        invalidResult.ShouldHaveValidationErrorFor(x => x.Quotas!.MaxEmployees);
        invalidResult.ShouldHaveValidationErrorFor(x => x.Quotas!.MaxStorageGb);
    }

    [Theory]
    [InlineData("soft", "Maintenance", true)]
    [InlineData("HARD", "Violations", true)]
    [InlineData("medium", "Invalid", false)]
    [InlineData("", "No type", false)]
    [InlineData("soft", "", false)]
    public void LockTenantRequestValidator_ShouldValidate(string lockType, string reason, bool isValid)
    {
        var validator = new LockTenantRequestValidator();
        var model = new LockTenantRequest(lockType, reason);
        var result = validator.TestValidate(model);

        if (isValid)
            result.ShouldNotHaveAnyValidationErrors();
        else
            Assert.False(result.IsValid);
    }

    [Fact]
    public void ExtendGraceRequestValidator_ShouldValidateBoundaries()
    {
        var validator = new ExtendGraceRequestValidator();

        var valid = new ExtendGraceRequest(30, "Customer request");
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalidZero = new ExtendGraceRequest(0, "Reason");
        validator.TestValidate(invalidZero).ShouldHaveValidationErrorFor(x => x.AdditionalDays);

        var invalidOver365 = new ExtendGraceRequest(366, "Reason");
        validator.TestValidate(invalidOver365).ShouldHaveValidationErrorFor(x => x.AdditionalDays);

        var invalidEmptyReason = new ExtendGraceRequest(10, "");
        validator.TestValidate(invalidEmptyReason).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void AssignBranchProductsValidator_ShouldValidate()
    {
        var validator = new AssignBranchProductsValidator();

        var valid = new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { Guid.NewGuid() },
            DefaultRetailPrice = 10000m,
            DefaultMinStock = 5
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var emptyList = new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid>()
        };
        validator.TestValidate(emptyList).ShouldHaveValidationErrorFor(x => x.ProductIds);

        var negativeValues = new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { Guid.Empty },
            DefaultRetailPrice = -1m,
            DefaultMinStock = -2
        };
        var res = validator.TestValidate(negativeValues);
        res.ShouldHaveValidationErrorFor("ProductIds[0]");
        res.ShouldHaveValidationErrorFor(x => x.DefaultRetailPrice);
        res.ShouldHaveValidationErrorFor(x => x.DefaultMinStock);
    }

    [Theory]
    [InlineData(10000, 5, "Active", true)]
    [InlineData(10000, null, "Inactive", true)]
    [InlineData(10000, 0, "", true)]
    [InlineData(0, 5, "Active", false)]
    [InlineData(-100, 5, "Active", false)]
    [InlineData(10000, -1, "Active", false)]
    [InlineData(10000, 5, "Suspended", false)]
    public void UpdateBranchProductValidator_ShouldValidate(decimal retailPrice, int? minStock, string status, bool isValid)
    {
        var validator = new UpdateBranchProductValidator();
        var model = new UpdateBranchProductCommand
        {
            RetailPrice = retailPrice,
            MinStockLevel = minStock,
            Status = status
        };

        var result = validator.TestValidate(model);
        if (isValid)
            result.ShouldNotHaveAnyValidationErrors();
        else
            Assert.False(result.IsValid);
    }

    [Fact]
    public void CloseBusinessDayRequestValidator_ShouldValidate()
    {
        var validator = new CloseBusinessDayRequestValidator();
        var fundId = Guid.NewGuid();

        var valid = new CloseBusinessDayRequest(
            BranchId: Guid.NewGuid(),
            BusinessDate: new DateOnly(2026, 11, 15),
            FundCounts: new List<BusinessDayFundCount>
            {
                new(fundId, 500000m)
            },
            PreviewToken: "valid-token-123",
            Note: "All good"
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new CloseBusinessDayRequest(
            BranchId: Guid.Empty,
            BusinessDate: default,
            FundCounts: new List<BusinessDayFundCount>
            {
                new(fundId, -100m),
                new(fundId, 1_000_000_000_000m)
            },
            PreviewToken: "",
            Note: new string('A', 501)
        );
        var result = validator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor(x => x.BranchId);
        result.ShouldHaveValidationErrorFor(x => x.BusinessDate);
        result.ShouldHaveValidationErrorFor(x => x.PreviewToken);
        result.ShouldHaveValidationErrorFor(x => x.Note);
        result.ShouldHaveValidationErrorFor(x => x.FundCounts); // Duplicate fund id
    }

    [Fact]
    public void EmptyValidators_ShouldExecuteSuccessfully()
    {
        var batchValidator = new BatchCreateRequestValidator();
        var batchReq = new BatchCreateRequest(Guid.NewGuid(), Guid.NewGuid(), "B01", DateTime.UtcNow, DateTime.UtcNow.AddDays(10), 10, 5000);
        batchValidator.TestValidate(batchReq).ShouldNotHaveAnyValidationErrors();

        var cashbookValidator = new CashbookCreateRequestValidator();
        var cashbookReq = new CashbookCreateRequest(Guid.NewGuid(), CashbookEntryType.Receipt, "Cat", 1000m, "REF01", "Desc");
        cashbookValidator.TestValidate(cashbookReq).ShouldNotHaveAnyValidationErrors();

        var stockAdjValidator = new StockAdjustmentRequestValidator();
        var stockAdjReq = new StockAdjustmentRequest(Guid.NewGuid(), Guid.NewGuid(), null, 15m, "Reason");
        stockAdjValidator.TestValidate(stockAdjReq).ShouldNotHaveAnyValidationErrors();
    }
}
