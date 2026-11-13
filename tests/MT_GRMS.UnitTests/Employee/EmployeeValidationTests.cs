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
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.HR;

public class EmployeeValidationTests
{
    private readonly CreateEmployeeRequestValidator _createEmpValidator = new();
    private readonly UpdateEmployeeRequestValidator _updateEmpValidator = new();
    private readonly CreateIdentityAccountRequestValidator _identityValidator = new();
    private readonly CreateScheduleItemRequestValidator _scheduleItemValidator = new();
    private readonly BatchCreateScheduleRequestValidator _batchScheduleValidator = new();
    private readonly CreateRecurringScheduleRequestValidator _recurringScheduleValidator = new();
    private readonly PublishSchedulesRequestValidator _publishScheduleValidator = new();
    private readonly CreateScheduleApprovalRequestValidator _approvalValidator = new();
    private readonly ReviewScheduleApprovalRequestValidator _reviewApprovalValidator = new();
    private readonly CreateShiftConfigRequestValidator _shiftConfigValidator = new();
    private readonly UpdateShiftConfigRequestValidator _updateShiftConfigValidator = new();
    private readonly UpsertAttendancePolicyRequestValidator _attendancePolicyValidator = new();
    private readonly CopyAttendancePolicyRequestValidator _copyAttendancePolicyValidator = new();
    private readonly GeneratePayrollBatchRequestValidator _generatePayrollValidator = new();
    private readonly PayPayslipRequestValidator _payPayslipValidator = new();
    private readonly BatchPayPayslipsRequestValidator _batchPayValidator = new();

    // =========================================================================
    // PHÂN HỆ 1: EMPLOYEE MANAGEMENT VALIDATION (EMP-VAL-01 .. EMP-VAL-05)
    // =========================================================================

    [Fact]
    public void CreateEmployee_EMP_VAL_01_HappyPath_ValidRequest_PassesValidation()
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            Email: "nguyenvana@mtmart.vn",
            Address: "123 Lê Lợi, Quận 1, TP.HCM",
            Note: "Nhân viên ca sáng",
            SalaryType: "monthly",
            BaseRate: 8000000m,
            Allowances: new List<EmployeeAllowanceDto>
            {
                new("Phụ cấp ăn trưa", 750000m),
                new("Phụ cấp xăng xe", 500000m)
            },
            BankAccountNumber: "190366778899",
            BankName: "Techcombank",
            BankAccountHolder: "NGUYEN VAN A"
        );

        var result = _createEmpValidator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateEmployee_EMP_VAL_02_EmptyFullName_FailsValidation(string invalidName)
    {
        var request = new CreateEmployeeRequest(
            FullName: invalidName,
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid()
        );

        var result = _createEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.FullName));
    }

    [Fact]
    public void CreateEmployee_FullNameExceeds100Chars_FailsValidation()
    {
        var longName = new string('A', 101);
        var request = new CreateEmployeeRequest(
            FullName: longName,
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid()
        );

        var result = _createEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(CreateEmployeeRequest.FullName) &&
            e.ErrorMessage.Contains("100 ký tự"));
    }

    [Fact]
    public void CreateEmployee_EMP_VAL_02_EmptyBranchId_FailsValidation()
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.Empty
        );

        var result = _createEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.BranchId));
    }

    [Theory]
    [InlineData("079201001234", true)]
    [InlineData("001199000111", true)]
    [InlineData("096200001122", true)]
    [InlineData("", false)]
    [InlineData("123", false)]
    [InlineData("07920100123", false)]
    [InlineData("0792010012345", false)]
    [InlineData("079abc001234", false)]
    [InlineData("079201 01234", false)]
    [InlineData("000201001234", false)] // Mã tỉnh 000 không tồn tại
    [InlineData("097201001234", false)] // Mã tỉnh 097 vượt quá 096
    public void CreateEmployee_EMP_VAL_03_CitizenIdFormat_ValidatesCorrectly(string citizenId, bool expectedValid)
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: citizenId,
            Phone: "0901234567",
            BranchId: Guid.NewGuid()
        );

        var result = _createEmpValidator.Validate(request);

        if (expectedValid)
        {
            Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.CitizenId));
        }
        else
        {
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.CitizenId));
        }
    }

    [Theory]
    [InlineData("0901234567", true)]
    [InlineData("0389998877", true)]
    [InlineData("0561234567", true)]
    [InlineData("0701234567", true)]
    [InlineData("0811234567", true)]
    [InlineData("", false)]
    [InlineData("0123456789", false)]
    [InlineData("0281234567", false)]
    [InlineData("0901234", false)]
    [InlineData("09012345678", false)]
    [InlineData("090abc4567", false)]
    public void CreateEmployee_EMP_VAL_04_PhoneFormat_ValidatesCorrectly(string phone, bool expectedValid)
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: phone,
            BranchId: Guid.NewGuid()
        );

        var result = _createEmpValidator.Validate(request);

        if (expectedValid)
        {
            Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Phone));
        }
        else
        {
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Phone));
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(8500000, true)]
    [InlineData(1000000000, true)]
    [InlineData(-1, false)]
    [InlineData(-500000, false)]
    [InlineData(1000000001, false)]
    public void CreateEmployee_EMP_VAL_05_BaseRateBounds_ValidatesCorrectly(decimal baseRate, bool expectedValid)
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            BaseRate: baseRate
        );

        var result = _createEmpValidator.Validate(request);

        if (expectedValid)
        {
            Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.BaseRate));
        }
        else
        {
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.BaseRate));
        }
    }

    [Fact]
    public void CreateEmployee_EMP_VAL_05_InvalidAllowances_FailsValidation()
    {
        var longAllowanceName = new string('P', 101);
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            Allowances: new List<EmployeeAllowanceDto>
            {
                new("", 200000m),
                new(longAllowanceName, 200000m),
                new("Ăn trưa", -10000m),
                new("Thưởng vượt trần", 500000001m)
            }
        );

        var result = _createEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4);
    }

    [Theory]
    [InlineData("Phụ cấp ăn trưa", "  phụ cấp ĂN TRƯA  ")]
    [InlineData("Phụ cấp ăn trưa", "phu cap an trua")]
    [InlineData("Phụ cấp đi lại", "PHU CAP DI LAI")]
    [InlineData("Hỗ trợ điện thoại", "ho tro dien thoai")]
    public void CreateEmployee_EMP_VAL_05_DuplicateAllowanceNames_CaseAndDiacriticInsensitive_FailsValidation(string name1, string name2)
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            Allowances: new List<EmployeeAllowanceDto>
            {
                new(name1, 500000m),
                new(name2, 600000m)
            }
        );

        var result = _createEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(CreateEmployeeRequest.Allowances) &&
            e.ErrorMessage.Contains("không được trùng"));
    }

    [Fact]
    public void UpdateEmployee_InvalidBaseRateAndAllowances_FailsValidation()
    {
        var request = new UpdateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            BaseRate: 1_500_000_000m,
            Allowances: new List<EmployeeAllowanceDto>
            {
                new("Phụ cấp âm", -5000m)
            }
        );

        var result = _updateEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeRequest.BaseRate));
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Allowances"));
    }

    [Fact]
    public void CreateEmployee_OptionalFieldsMaxLengthAndEmailFormat_ValidatesCorrectly()
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            Email: "not-an-email",
            Address: new string('A', 256),
            Note: new string('N', 501),
            BankAccountNumber: new string('1', 31),
            BankName: new string('B', 101),
            BankAccountHolder: new string('H', 101)
        );

        var result = _createEmpValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Email));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Address));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Note));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.BankAccountNumber));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.BankName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.BankAccountHolder));
    }

    [Theory]
    [InlineData("NV-001", true)]
    [InlineData("EMP_2026", true)]
    [InlineData("NV 001", false)]
    [InlineData("NHÂN_VIÊN_01", false)]
    [InlineData("NV@001", false)]
    public void CreateEmployee_EmployeeCodeFormat_ValidatesCorrectly(string employeeCode, bool expectedValid)
    {
        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            EmployeeCode: employeeCode
        );

        var result = _createEmpValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("staff_01", "123456", "STAFF", true)]
    [InlineData("cashier_01", "123456", "CASHIER", true)]
    [InlineData("ab", "123456", "STAFF", false)] // Username < 3 ký tự
    [InlineData("staff_01", "123", "STAFF", false)] // Password < 6 ký tự
    [InlineData("staff_01", "123456", "SUPER_HACKER", false)] // Role không hợp lệ
    public void CreateIdentityAccount_ValidatesUsernamePasswordAndRole(
        string username, string password, string role, bool expectedValid)
    {
        var request = new CreateIdentityAccountRequest(username, password, role);
        var result = _identityValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("Trần Thị Mai", true)]
    [InlineData("H'Hen Niê", true)]
    [InlineData("Lê Văn-An", true)]
    [InlineData("A", false)] // Quá ngắn (< 2 ký tự)
    [InlineData("Nguyễn Văn A 123", false)] // Chứa chữ số
    [InlineData("Trần @ Mai #", false)] // Chứa ký tự đặc biệt
    public void CreateEmployee_EMP_VAL_08_FullNameRealisticFormat_ValidatesCorrectly(string fullName, bool expectedValid)
    {
        var request = new CreateEmployeeRequest(
            FullName: fullName,
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid()
        );

        var result = _createEmpValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void CreateEmployee_EMP_VAL_09_WorkingAgeAndStartDate_ValidatesCorrectly()
    {
        // Case 1: Đủ 24 tuổi -> Hợp lệ
        var validRequest = new CreateEmployeeRequest(
            FullName: "Trần Thị Mai",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            DateOfBirth: DateTime.UtcNow.Date.AddYears(-24),
            StartDate: DateTime.UtcNow.Date
        );
        Assert.True(_createEmpValidator.Validate(validRequest).IsValid);

        // Case 2: Chưa đủ 15 tuổi (12 tuổi) -> Lỗi DateOfBirth
        var underageRequest = validRequest with
        {
            DateOfBirth = DateTime.UtcNow.Date.AddYears(-12)
        };
        var underageResult = _createEmpValidator.Validate(underageRequest);
        Assert.False(underageResult.IsValid);
        Assert.Contains(underageResult.Errors, e => e.PropertyName.Contains(nameof(CreateEmployeeRequest.DateOfBirth)));

        // Case 3: Ngày sinh đủ tuổi (25 tuổi) nhưng StartDate lại lúc mới 10 tuổi -> Lỗi StartDate
        var dob = new DateTime(2000, 5, 10);
        var invalidStartDateRequest = validRequest with
        {
            DateOfBirth = dob,
            StartDate = dob.AddYears(10)
        };
        var invalidStartResult = _createEmpValidator.Validate(invalidStartDateRequest);
        Assert.False(invalidStartResult.IsValid);
        Assert.Contains(invalidStartResult.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.StartDate));
    }

    [Theory]
    [InlineData("19036688999012", true)]
    [InlineData("VN99887766", true)]
    [InlineData("1903 6688 999", false)] // Chứa khoảng trắng
    [InlineData("1903-6688@999", false)] // Chứa ký tự đặc biệt
    public void CreateEmployee_EMP_VAL_10_BankAccountNumberFormat_ValidatesCorrectly(string bankAccount, bool expectedValid)
    {
        var request = new CreateEmployeeRequest(
            FullName: "Trần Thị Mai",
            CitizenId: "079201001234",
            Phone: "0901234567",
            BranchId: Guid.NewGuid(),
            BankAccountNumber: bankAccount
        );

        var result = _createEmpValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    // =========================================================================
    // PHÂN HỆ 2: WORK SCHEDULE & ATTENDANCE VALIDATION (SCH-VAL-01 .. SCH-VAL-03)
    // =========================================================================

    [Fact]
    public void ScheduleItem_SCH_VAL_01_WithShiftConfigId_PassesValidation()
    {
        var request = new CreateScheduleItemRequest(
            EmployeeId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            WorkDate: DateTime.Today,
            ShiftConfigId: Guid.NewGuid()
        );

        var result = _scheduleItemValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ScheduleItem_SCH_VAL_02_CustomShiftMissingNameOrTimes_FailsValidation()
    {
        var request = new CreateScheduleItemRequest(
            EmployeeId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            WorkDate: DateTime.Today,
            ShiftConfigId: null,
            ShiftName: "",
            PlannedStartTime: null,
            PlannedEndTime: null
        );

        var result = _scheduleItemValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateScheduleItemRequest.ShiftName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateScheduleItemRequest.PlannedStartTime));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateScheduleItemRequest.PlannedEndTime));
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(8, true)]
    [InlineData(24, true)]
    [InlineData(0, false)]
    [InlineData(-2, false)]
    [InlineData(25, false)]
    public void ScheduleItem_SCH_VAL_03_DurationHoursBounds_ValidatesCorrectly(decimal duration, bool expectedValid)
    {
        var request = new CreateScheduleItemRequest(
            EmployeeId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            WorkDate: DateTime.Today,
            ShiftConfigId: Guid.NewGuid(),
            DurationHours: duration
        );

        var result = _scheduleItemValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void BatchCreateSchedule_EmptySchedulesList_FailsValidation()
    {
        var request = new BatchCreateScheduleRequest(
            BranchId: Guid.NewGuid(),
            Schedules: Array.Empty<CreateScheduleItemRequest>()
        );

        var result = _batchScheduleValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void RecurringSchedule_EndDateBeforeStartDate_FailsValidation()
    {
        var request = new CreateRecurringScheduleRequest(
            EmployeeId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            StartDate: new DateTime(2026, 11, 15),
            HasEndDate: true,
            EndDate: new DateTime(2026, 11, 10)
        );

        var result = _recurringScheduleValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PublishSchedules_ToDateBeforeFromDate_FailsValidation()
    {
        var request = new PublishSchedulesRequest(
            BranchId: Guid.NewGuid(),
            FromDate: new DateTime(2026, 11, 20),
            ToDate: new DateTime(2026, 11, 10)
        );

        var result = _publishScheduleValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ScheduleApproval_OvertimeAndRejectRules_ValidateCorrectly()
    {
        var invalidOtRequest = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.Overtime,
            Reason: "Tăng ca kiểm kê kho cuối tháng",
            RequestedOvertimeMinutes: 10 // < 15 phút tối thiểu
        );

        var otResult = _approvalValidator.Validate(invalidOtRequest);
        Assert.False(otResult.IsValid);

        var invalidRejectReview = new ReviewScheduleApprovalRequestDto(
            IsApproved: false,
            ReviewNote: "" // Từ chối bắt buộc nhập lý do
        );

        var reviewResult = _reviewApprovalValidator.Validate(invalidRejectReview);
        Assert.False(reviewResult.IsValid);
    }

    [Fact]
    public void ShiftConfig_MissingBranchScope_FailsValidation()
    {
        var request = new CreateShiftConfigRequest(
            ShiftName: "Ca Sáng",
            StartTime: new TimeSpan(7, 0, 0),
            EndTime: new TimeSpan(15, 0, 0),
            BranchId: null,
            BranchIds: null
        );

        var result = _shiftConfigValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("CA_SANG_01", 8, 12, true)]
    [InlineData("CA-DEM", 22, 6, true)]
    [InlineData("A", 8, 12, false)]           // Mã ca < 2 ký tự
    [InlineData("CA SÁNG", 8, 12, false)]     // Mã ca chứa dấu cách / tiếng Việt
    [InlineData("CA_SANG", 8, 8, false)]      // StartTime == EndTime (thời lượng 0h)
    public void ShiftConfig_SHF_VAL_01_03_CodeFormatAndZeroDuration_ValidatesCorrectly(
        string shiftCode, int startHour, int endHour, bool expectedValid)
    {
        var request = new CreateShiftConfigRequest(
            ShiftName: "Ca Làm Việc",
            ShiftCode: shiftCode,
            StartTime: new TimeSpan(startHour, 0, 0),
            EndTime: new TimeSpan(endHour, 0, 0),
            BranchId: Guid.NewGuid()
        );

        var result = _shiftConfigValidator.Validate(request);
        Assert.Equal(expectedValid, result.IsValid);

        var updateRequest = new UpdateShiftConfigRequest(
            ShiftName: "Ca Làm Việc",
            ShiftCode: shiftCode,
            StartTime: new TimeSpan(startHour, 0, 0),
            EndTime: new TimeSpan(endHour, 0, 0),
            BranchId: Guid.NewGuid()
        );
        var updateResult = _updateShiftConfigValidator.Validate(updateRequest);
        Assert.Equal(expectedValid, updateResult.IsValid);
    }

    [Fact]
    public void AttendancePolicy_POL_VAL_01_04_ValidatesStandardHoursThresholdsAndConversionRules()
    {
        // Happy path
        var validPolicy = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8m,
            IsConvertHoursToDays: true,
            ConversionRulesJson: "[{\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5},{\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1}]",
            EnableLateThreshold: true,
            LateThresholdMinutes: 15,
            EnableEarlyLeaveThreshold: true,
            EarlyLeaveThresholdMinutes: 15
        );
        Assert.True(_attendancePolicyValidator.Validate(validPolicy).IsValid);

        // Invalid StandardDayHours & LateThresholdMinutes
        var invalidBounds = validPolicy with
        {
            StandardDayHours = 0m,
            LateThresholdMinutes = 300,
            EarlyLeaveThresholdMinutes = -5
        };
        var boundsResult = _attendancePolicyValidator.Validate(invalidBounds);
        Assert.False(boundsResult.IsValid);
        Assert.Contains(boundsResult.Errors, e => e.PropertyName == nameof(UpsertAttendancePolicyRequest.StandardDayHours));
        Assert.Contains(boundsResult.Errors, e => e.PropertyName == nameof(UpsertAttendancePolicyRequest.LateThresholdMinutes));
        Assert.Contains(boundsResult.Errors, e => e.PropertyName == nameof(UpsertAttendancePolicyRequest.EarlyLeaveThresholdMinutes));

        // Invalid ConversionRulesJson (from >= to in range rule)
        var invalidRules = validPolicy with
        {
            ConversionRulesJson = "[{\"type\":\"range\",\"fromHours\":6,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":0,\"workUnits\":0.5}]"
        };
        Assert.False(_attendancePolicyValidator.Validate(invalidRules).IsValid);

        // CopyAttendancePolicyRequest: TargetBranchIds contains SourceBranchId
        var sourceBranchId = Guid.NewGuid();
        var invalidCopy = new CopyAttendancePolicyRequest(
            SourceBranchId: sourceBranchId,
            TargetBranchIds: new List<Guid> { sourceBranchId, Guid.NewGuid() }
        );
        Assert.False(_copyAttendancePolicyValidator.Validate(invalidCopy).IsValid);
    }

    [Fact]
    public void ScheduleApproval_APP_VAL_01_05_AllRequestTypesAndEvidenceLimit_ValidateCorrectly()
    {
        // 1. ShiftSwap lý do ngắn (< 5 ký tự) -> Fail
        var invalidShiftSwap = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.ShiftSwap,
            Reason: "Đổi"
        );
        Assert.False(_approvalValidator.Validate(invalidShiftSwap).IsValid);

        // 2. LeaveRequest thiếu LeaveType -> Fail
        var invalidLeave = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Xin nghỉ phép giải quyết việc gia đình",
            LeaveType: null
        );
        Assert.False(_approvalValidator.Validate(invalidLeave).IsValid);

        // 3. CheckOutIncident thiếu RequestedCheckOutTime -> Fail
        var invalidCheckOut = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.CheckOutIncident,
            Reason: "Quên chấm công ra ca do mất điện",
            RequestedCheckOutTime: null
        );
        Assert.False(_approvalValidator.Validate(invalidCheckOut).IsValid);

        // 4. EvidenceImageUrls vượt quá 8 ảnh -> Fail
        var tooManyImages = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.Overtime,
            Reason: "Tăng ca kiểm kê hàng hóa cuối kỳ",
            RequestedOvertimeMinutes: 120,
            EvidenceImageUrls: Enumerable.Range(1, 9).Select(i => $"https://cdn.mtmart.vn/ev{i}.jpg").ToList()
        );
        Assert.False(_approvalValidator.Validate(tooManyImages).IsValid);
    }

    // =========================================================================
    // PHÂN HỆ 3: PAYROLLS & PAYOUTS VALIDATION (PAY-VAL-01, PAY-VAL-02, PAY-VAL-04)
    // =========================================================================

    [Theory]
    [InlineData("2026-01", true)]
    [InlineData("2026-11", true)]
    [InlineData("2026-12", true)]
    [InlineData("", false)]
    [InlineData("2026/11", false)]
    [InlineData("11-2026", false)]
    [InlineData("2026-00", false)]
    [InlineData("2026-13", false)]
    [InlineData("2026-1", false)]
    public void GeneratePayrollBatch_PAY_VAL_01_02_PeriodMonthFormat_ValidatesCorrectly(string periodMonth, bool expectedValid)
    {
        var request = new GeneratePayrollBatchRequest(PeriodMonth: periodMonth);
        var result = _generatePayrollValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("CASH", true)]
    [InlineData("VIETQR", true)]
    [InlineData("BANK_TRANSFER", true)]
    [InlineData("cash", true)]
    [InlineData("", false)]
    [InlineData("BITCOIN", false)]
    [InlineData("CREDIT_CARD", false)]
    public void PayPayslip_PAY_VAL_04_PaymentMethod_ValidatesCorrectly(string paymentMethod, bool expectedValid)
    {
        var request = new PayPayslipRequest(PaymentMethod: paymentMethod);
        var result = _payPayslipValidator.Validate(request);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void BatchPayPayslips_EmptyPayslipIds_FailsValidation()
    {
        var request = new BatchPayPayslipsRequest(
            PayslipIds: new List<Guid>(),
            PaymentMethod: "CASH"
        );

        var result = _batchPayValidator.Validate(request);

        Assert.False(result.IsValid);
    }
}

