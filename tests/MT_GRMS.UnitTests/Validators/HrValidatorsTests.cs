using System;
using System.Collections.Generic;
using FluentValidation.TestHelper;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;
using MT_GRMS.Application.HR.AttendancePolicies.Validators;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Employees.Validators;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Payslips.Validators;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.Schedules.Validators;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs.Validators;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Validators;

public class HrValidatorsTests
{
    [Fact]
    public void AttendancePolicyValidators_Upsert_ShouldValidateStandardRules()
    {
        var validator = new UpsertAttendancePolicyRequestValidator();

        var valid = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8.0m,
            LateThresholdMinutes: 15,
            EarlyLeaveThresholdMinutes: 15,
            IsConvertHoursToDays: false
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpsertAttendancePolicyRequest(
            StandardDayHours: 0m,
            LateThresholdMinutes: -1,
            EarlyLeaveThresholdMinutes: 241,
            IsConvertHoursToDays: false
        );
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.StandardDayHours);
        res.ShouldHaveValidationErrorFor(x => x.LateThresholdMinutes);
        res.ShouldHaveValidationErrorFor(x => x.EarlyLeaveThresholdMinutes);

        var over24Hours = new UpsertAttendancePolicyRequest(
            StandardDayHours: 25m,
            LateThresholdMinutes: 10,
            EarlyLeaveThresholdMinutes: 10,
            IsConvertHoursToDays: false
        );
        validator.TestValidate(over24Hours).ShouldHaveValidationErrorFor(x => x.StandardDayHours);

        var excessiveDecimals = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8.123m,
            LateThresholdMinutes: 10,
            EarlyLeaveThresholdMinutes: 10,
            IsConvertHoursToDays: false
        );
        validator.TestValidate(excessiveDecimals).ShouldHaveValidationErrorFor(x => x.StandardDayHours);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("not-json", false)]
    [InlineData("{\"type\":\"range\"}", false)] // Not an array
    [InlineData("[]", false)] // Empty array
    [InlineData("[123]", false)] // Array item not an object
    [InlineData("[{\"type\":\"range\",\"fromHours\":-1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0.5}]", false)] // Invalid fromHours
    [InlineData("[{\"type\":\"range\",\"fromHours\":0,\"fromMinutes\":65,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0.5}]", false)] // Invalid fromMinutes
    [InlineData("[{\"type\":\"range\",\"fromHours\":0,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0}]", false)] // Invalid workUnits <= 0
    [InlineData("[{\"type\":\"range\",\"fromHours\":0,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":6}]", false)] // Invalid workUnits > 5
    [InlineData("[{\"type\":\"range\",\"fromHours\":0,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0.555}]", false)] // WorkUnits > 2 decimals
    [InlineData("[{\"type\":\"range\",\"fromHours\":4,\"fromMinutes\":0,\"toHours\":2,\"toMinutes\":0,\"workUnits\":0.5}]", false)] // toTotal <= fromTotal
    [InlineData("[{\"type\":\"unknown\",\"fromHours\":0,\"fromMinutes\":0,\"workUnits\":1}]", false)] // Unknown type
    [InlineData("[{\"type\":\"gte\",\"fromHours\":25,\"fromMinutes\":0,\"workUnits\":1}]", false)] // fromTotal >= 1440
    [InlineData("[{\"type\":\"gte\",\"fromHours\":8,\"fromMinutes\":0,\"workUnits\":1},{\"type\":\"gte\",\"fromHours\":10,\"fromMinutes\":0,\"workUnits\":1.5}]", false)] // Duplicate gte
    [InlineData("[{\"type\":\"range\",\"fromHours\":0,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0.5},{\"type\":\"range\",\"fromHours\":3,\"fromMinutes\":0,\"toHours\":6,\"toMinutes\":0,\"workUnits\":0.8}]", false)] // Overlapping ranges
    [InlineData("[{\"type\":\"range\",\"fromHours\":0,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0.5},{\"type\":\"range\",\"fromHours\":4,\"fromMinutes\":0,\"toHours\":8,\"toMinutes\":0,\"workUnits\":1.0},{\"type\":\"gte\",\"fromHours\":8,\"fromMinutes\":0,\"workUnits\":1.2}]", true)] // Valid
    public void AttendancePolicyValidators_ConversionRulesJson_ShouldValidate(string json, bool expectedValid)
    {
        var validator = new UpsertAttendancePolicyRequestValidator();
        var model = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8.0m,
            LateThresholdMinutes: 15,
            EarlyLeaveThresholdMinutes: 15,
            IsConvertHoursToDays: true,
            ConversionRulesJson: json
        );

        var res = validator.TestValidate(model);
        if (expectedValid)
            res.ShouldNotHaveAnyValidationErrors();
        else
            res.ShouldHaveValidationErrorFor(x => x.ConversionRulesJson);
    }

    [Fact]
    public void CopyAttendancePolicyRequestValidator_ShouldValidate()
    {
        var validator = new CopyAttendancePolicyRequestValidator();
        var sourceId = Guid.NewGuid();
        var target1 = Guid.NewGuid();
        var target2 = Guid.NewGuid();

        var valid = new CopyAttendancePolicyRequest(sourceId, new List<Guid> { target1, target2 });
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var emptySource = new CopyAttendancePolicyRequest(Guid.Empty, new List<Guid> { target1 });
        validator.TestValidate(emptySource).ShouldHaveValidationErrorFor(x => x.SourceBranchId);

        var nullTargets = new CopyAttendancePolicyRequest(sourceId, null!);
        validator.TestValidate(nullTargets).ShouldHaveValidationErrorFor(x => x.TargetBranchIds);

        var containsSource = new CopyAttendancePolicyRequest(sourceId, new List<Guid> { sourceId, target1 });
        validator.TestValidate(containsSource).ShouldHaveValidationErrorFor(x => x.TargetBranchIds);
    }

    [Fact]
    public void VietnameseTextNormalizer_ShouldNormalizeCorrectly()
    {
        Assert.Equal(string.Empty, VietnameseTextNormalizer.NormalizeForComparison(null));
        Assert.Equal(string.Empty, VietnameseTextNormalizer.NormalizeForComparison("   "));

        var normalized = VietnameseTextNormalizer.NormalizeForComparison("  Nguyễn   Văn   Đồng  ");
        Assert.Equal("nguyen van dong", normalized);

        var now = VietnameseTextNormalizer.GetVietnamNow();
        var today = VietnameseTextNormalizer.GetVietnamToday();
        Assert.Equal(today, now.Date);
    }

    [Fact]
    public void CreateEmployeeRequestValidator_ValidAndInvalidScenarios()
    {
        var validator = new CreateEmployeeRequestValidator();
        var branchId = Guid.NewGuid();
        var dob = VietnameseTextNormalizer.GetVietnamToday().AddYears(-25);

        var valid = new CreateEmployeeRequest(
            FullName: "Nguyen Van A",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: branchId,
            Email: "test@example.com",
            DateOfBirth: dob,
            StartDate: dob.AddYears(20),
            SalaryType: "monthly",
            BaseRate: 10000000m,
            Password: "Password123!"
        );
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalidName = valid with { FullName = "A" };
        validator.TestValidate(invalidName).ShouldHaveValidationErrorFor(x => x.FullName);

        var invalidNameChars = valid with { FullName = "Nguyen Van 123" };
        validator.TestValidate(invalidNameChars).ShouldHaveValidationErrorFor(x => x.FullName);

        var invalidYoung = valid with { DateOfBirth = VietnameseTextNormalizer.GetVietnamToday().AddYears(-14) };
        validator.TestValidate(invalidYoung).ShouldHaveValidationErrorFor(x => x.DateOfBirth!.Value);

        var invalidCitizenId = valid with { CitizenId = "12345" };
        validator.TestValidate(invalidCitizenId).ShouldHaveValidationErrorFor(x => x.CitizenId);

        var invalidPhone = valid with { Phone = "1234567890" };
        validator.TestValidate(invalidPhone).ShouldHaveValidationErrorFor(x => x.Phone);

        var invalidEmail = valid with { Email = "not-an-email" };
        validator.TestValidate(invalidEmail).ShouldHaveValidationErrorFor(x => x.Email);

        var invalidSalary = valid with { BaseRate = -100m };
        var resSalary = validator.TestValidate(invalidSalary);
        resSalary.ShouldHaveValidationErrorFor(x => x.BaseRate);

        var duplicateAllowances = valid with
        {
            Allowances = new List<EmployeeAllowanceDto>
            {
                new("Phụ cấp ăn trưa", 500000m),
                new("Phu cap an trua", 600000m)
            }
        };
        validator.TestValidate(duplicateAllowances).ShouldHaveValidationErrorFor(x => x.Allowances);

        var contractWithZeroInsurance = valid with
        {
            HasLaborContract = true,
            SalaryType = "monthly",
            BaseRate = 0m,
            InsuranceBaseSalary = 0m
        };
        validator.TestValidate(contractWithZeroInsurance).ShouldHaveValidationErrorFor(x => x.InsuranceBaseSalary);
    }

    [Fact]
    public void CreateIdentityAccountRequestValidator_ShouldValidate()
    {
        var validator = new CreateIdentityAccountRequestValidator();

        var valid = new CreateIdentityAccountRequest("username1", "Password123!", SystemRoles.Staff, "Display");
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new CreateIdentityAccountRequest("ab", "123", "", "Display");
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.Username);
        res.ShouldHaveValidationErrorFor(x => x.Password);
        res.ShouldHaveValidationErrorFor(x => x.Role);
    }

    [Fact]
    public void PayslipValidators_ShouldValidateAllRules()
    {
        var genValidator = new GeneratePayrollBatchRequestValidator();
        var validGen = new GeneratePayrollBatchRequest("2026-11", Note: "Notes");
        genValidator.TestValidate(validGen).ShouldNotHaveAnyValidationErrors();

        var invalidMonth = new GeneratePayrollBatchRequest("2026-13", Note: new string('N', 501));
        var genRes = genValidator.TestValidate(invalidMonth);
        genRes.ShouldHaveValidationErrorFor(x => x.PeriodMonth);
        genRes.ShouldHaveValidationErrorFor(x => x.Note);

        var payValidator = new PayPayslipRequestValidator();
        var validPay = new PayPayslipRequest("CASH", "Paid");
        payValidator.TestValidate(validPay).ShouldNotHaveAnyValidationErrors();

        var invalidPay = new PayPayslipRequest("CRYPTO", new string('N', 501));
        var payRes = payValidator.TestValidate(invalidPay);
        payRes.ShouldHaveValidationErrorFor(x => x.PaymentMethod);
        payRes.ShouldHaveValidationErrorFor(x => x.Note);

        var batchValidator = new BatchPayPayslipsRequestValidator();
        var validBatch = new BatchPayPayslipsRequest(new List<Guid> { Guid.NewGuid() }, "VIETQR", null);
        batchValidator.TestValidate(validBatch).ShouldNotHaveAnyValidationErrors();

        var invalidBatch = new BatchPayPayslipsRequest(new List<Guid>(), "UNKNOWN", null);
        var batchRes = batchValidator.TestValidate(invalidBatch);
        batchRes.ShouldHaveValidationErrorFor(x => x.PayslipIds);
        batchRes.ShouldHaveValidationErrorFor(x => x.PaymentMethod);
    }

    [Fact]
    public void ScheduleApprovalValidators_ShouldValidate()
    {
        var createValidator = new CreateScheduleApprovalRequestValidator();

        var validOt = new CreateScheduleApprovalRequestDto
        {
            Reason = "Stay late to assist with delivery",
            RequestType = ScheduleRequestType.Overtime,
            RequestedOvertimeMinutes = 60
        };
        createValidator.TestValidate(validOt).ShouldNotHaveAnyValidationErrors();

        var invalidOt = new CreateScheduleApprovalRequestDto
        {
            Reason = "Late",
            RequestType = ScheduleRequestType.Overtime,
            RequestedOvertimeMinutes = 10
        };
        var otRes = createValidator.TestValidate(invalidOt);
        otRes.ShouldHaveValidationErrorFor(x => x.Reason);
        otRes.ShouldHaveValidationErrorFor(x => x.RequestedOvertimeMinutes);

        var leaveReq = new CreateScheduleApprovalRequestDto
        {
            Reason = "Family emergency",
            RequestType = ScheduleRequestType.LeaveRequest,
            LeaveType = null
        };
        createValidator.TestValidate(leaveReq).ShouldHaveValidationErrorFor(x => x.LeaveType);

        var checkInReq = new CreateScheduleApprovalRequestDto
        {
            Reason = "Forgot to check in",
            RequestType = ScheduleRequestType.CheckInIncident
        };
        createValidator.TestValidate(checkInReq).ShouldHaveValidationErrorFor(x => x);

        var checkOutReq = new CreateScheduleApprovalRequestDto
        {
            Reason = "Forgot to check out",
            RequestType = ScheduleRequestType.CheckOutIncident,
            RequestedCheckOutTime = null
        };
        createValidator.TestValidate(checkOutReq).ShouldHaveValidationErrorFor(x => x.RequestedCheckOutTime);

        var invalidTimes = new CreateScheduleApprovalRequestDto
        {
            Reason = "Incident attendance",
            RequestType = ScheduleRequestType.CheckInIncident,
            RequestedCheckInTime = DateTime.UtcNow,
            RequestedCheckOutTime = DateTime.UtcNow.AddHours(-1)
        };
        createValidator.TestValidate(invalidTimes).ShouldHaveValidationErrorFor(x => x);

        var reviewValidator = new ReviewScheduleApprovalRequestValidator();
        var validReview = new ReviewScheduleApprovalRequestDto
        {
            IsApproved = true,
            ApprovedOvertimeMinutes = 60,
            ReviewNote = "Approved"
        };
        reviewValidator.TestValidate(validReview).ShouldNotHaveAnyValidationErrors();

        var invalidReject = new ReviewScheduleApprovalRequestDto
        {
            IsApproved = false,
            ReviewNote = ""
        };
        reviewValidator.TestValidate(invalidReject).ShouldHaveValidationErrorFor(x => x.ReviewNote);

        var invalidOtMinutes = new ReviewScheduleApprovalRequestDto
        {
            IsApproved = true,
            ApprovedOvertimeMinutes = 500
        };
        reviewValidator.TestValidate(invalidOtMinutes).ShouldHaveValidationErrorFor(x => x.ApprovedOvertimeMinutes!.Value);
    }

    [Fact]
    public void ScheduleValidators_ShouldValidate()
    {
        var itemValidator = new CreateScheduleItemRequestValidator();
        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var valid = new CreateScheduleItemRequest(
            EmployeeId: empId,
            BranchId: branchId,
            WorkDate: DateTime.UtcNow.Date,
            ShiftName: "Morning Shift",
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(16, 0, 0),
            DurationHours: 8m
        );
        itemValidator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new CreateScheduleItemRequest(
            EmployeeId: Guid.Empty,
            BranchId: Guid.Empty,
            WorkDate: default,
            ShiftConfigId: null,
            ShiftName: null,
            PlannedStartTime: TimeSpan.FromHours(25),
            PlannedEndTime: TimeSpan.FromHours(-1),
            DurationHours: 0m
        );
        var res = itemValidator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.EmployeeId);
        res.ShouldHaveValidationErrorFor(x => x.BranchId);
        res.ShouldHaveValidationErrorFor(x => x.WorkDate);
        res.ShouldHaveValidationErrorFor(x => x.ShiftName);
        res.ShouldHaveValidationErrorFor(x => x.PlannedStartTime!.Value);
        res.ShouldHaveValidationErrorFor(x => x.PlannedEndTime!.Value);
        res.ShouldHaveValidationErrorFor(x => x.DurationHours!.Value);

        var batchValidator = new BatchCreateScheduleRequestValidator();
        var invalidBatch = new BatchCreateScheduleRequest(Guid.Empty, new List<CreateScheduleItemRequest>());
        var batchRes = batchValidator.TestValidate(invalidBatch);
        batchRes.ShouldHaveValidationErrorFor(x => x.BranchId);
        batchRes.ShouldHaveValidationErrorFor(x => x.Schedules);

        var recurringValidator = new CreateRecurringScheduleRequestValidator();
        var invalidRecurring = new CreateRecurringScheduleRequest(
            EmployeeId: Guid.Empty,
            BranchId: Guid.Empty,
            StartDate: default,
            HasEndDate: true,
            EndDate: DateTime.UtcNow.Date.AddDays(-5)
        );
        var recurringRes = recurringValidator.TestValidate(invalidRecurring);
        recurringRes.ShouldHaveValidationErrorFor(x => x.EmployeeId);
        recurringRes.ShouldHaveValidationErrorFor(x => x.BranchId);
        recurringRes.ShouldHaveValidationErrorFor(x => x.StartDate);

        var publishValidator = new PublishSchedulesRequestValidator();
        var invalidPublish = new PublishSchedulesRequest(Guid.Empty, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(-1));
        var pubRes = publishValidator.TestValidate(invalidPublish);
        pubRes.ShouldHaveValidationErrorFor(x => x.BranchId);
        pubRes.ShouldHaveValidationErrorFor(x => x.ToDate);

        var resetValidator = new ResetAttendanceRequestValidator();
        var invalidReset = new ResetAttendanceRequest(new string('A', 256));
        resetValidator.TestValidate(invalidReset).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ShiftConfigValidators_ShouldValidate()
    {
        var createValidator = new CreateShiftConfigRequestValidator();
        var branchId = Guid.NewGuid();

        var valid = new CreateShiftConfigRequest(
            ShiftName: "Morning Shift",
            StartTime: new TimeSpan(8, 0, 0),
            EndTime: new TimeSpan(16, 0, 0),
            BranchId: branchId,
            ShiftCode: "SHIFT_01",
            DurationHours: 8m,
            Description: "Morning"
        );
        createValidator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new CreateShiftConfigRequest(
            ShiftName: "",
            StartTime: TimeSpan.FromHours(25),
            EndTime: TimeSpan.FromHours(-1),
            BranchIds: null,
            BranchId: null,
            ShiftCode: "A",
            DurationHours: -1m,
            Description: new string('D', 256)
        );
        var res = createValidator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.ShiftName);
        res.ShouldHaveValidationErrorFor(x => x.StartTime);
        res.ShouldHaveValidationErrorFor(x => x.EndTime);
        res.ShouldHaveValidationErrorFor(x => x.ShiftCode);
        res.ShouldHaveValidationErrorFor(x => x.DurationHours!.Value);
        res.ShouldHaveValidationErrorFor(x => x.Description);

        var updateValidator = new UpdateShiftConfigRequestValidator();
        var validUpdate = new UpdateShiftConfigRequest(
            ShiftName: "Evening",
            StartTime: new TimeSpan(16, 0, 0),
            EndTime: new TimeSpan(22, 0, 0),
            BranchId: branchId
        );
        updateValidator.TestValidate(validUpdate).ShouldNotHaveAnyValidationErrors();

        var invalidUpdate = new UpdateShiftConfigRequest(
            ShiftName: "",
            StartTime: new TimeSpan(8, 0, 0),
            EndTime: new TimeSpan(8, 0, 0), // Same time with no duration
            BranchId: branchId,
            DurationHours: 0m
        );
        var updateRes = updateValidator.TestValidate(invalidUpdate);
        updateRes.ShouldHaveValidationErrorFor(x => x.ShiftName);
    }
}
