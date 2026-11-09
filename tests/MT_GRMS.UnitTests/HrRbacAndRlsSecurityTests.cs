using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

public class HrRbacAndRlsSecurityTests
{
    private static ControllerContext CreateContextWithUser(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
            new("role", role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public async Task EmployeesController_GetById_WhenEmployeeViewsOwnRecord_Returns200Ok()
    {
        // Arrange
        var employeeId = Guid.NewGuid();
        var currentUserId = "user_emp_001";
        var mockService = new Mock<IEmployeeService>();
        var dummyResponse = new EmployeeResponse(
            Id: employeeId,
            EmployeeCode: "NV0001",
            FullName: "Nguyễn Văn A",
            CitizenId: "012345678901",
            DateOfBirth: DateTime.UtcNow.AddYears(-25),
            Gender: "Nam",
            Phone: "0901234567",
            Email: "nva@mtgrms.vn",
            Address: "Hà Nội",
            StartDate: DateTime.UtcNow.AddMonths(-6),
            Note: null,
            AvatarUrl: null,
            BranchId: Guid.NewGuid(),
            BranchName: "Chi nhánh 1",
            UserId: currentUserId,
            UserName: "nva",
            Role: "STAFF",
            SalaryType: "Monthly",
            BaseRate: 10_000_000m,
            OtWeekday: "1.5",
            OtSunday: "2.0",
            Allowances: new List<EmployeeAllowanceDto>(),
            DebtAmount: 0m,
            Status: "Active",
            BankAccountNumber: null,
            BankName: null,
            BankAccountHolder: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        );

        mockService.Setup(s => s.GetEmployeeByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyResponse);

        var controller = new EmployeesController(mockService.Object)
        {
            ControllerContext = CreateContextWithUser(currentUserId, SystemRoles.Employee)
        };

        // Act
        var result = await controller.GetById(employeeId, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<EmployeeResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(employeeId, apiResponse.Data?.Id);
    }

    [Fact]
    public async Task EmployeesController_GetById_WhenEmployeeViewsOtherEmployeeRecord_Returns403Forbidden()
    {
        // Arrange
        var otherEmployeeId = Guid.NewGuid();
        var currentUserId = "user_emp_001";
        var otherUserId = "user_emp_002";

        var mockService = new Mock<IEmployeeService>();
        var otherEmployeeResponse = new EmployeeResponse(
            Id: otherEmployeeId,
            EmployeeCode: "NV0002",
            FullName: "Trần Thị B",
            CitizenId: "012345678902",
            DateOfBirth: DateTime.UtcNow.AddYears(-23),
            Gender: "Nữ",
            Phone: "0909876543",
            Email: "ttb@mtgrms.vn",
            Address: "Hà Nội",
            StartDate: DateTime.UtcNow.AddMonths(-3),
            Note: null,
            AvatarUrl: null,
            BranchId: Guid.NewGuid(),
            BranchName: "Chi nhánh 1",
            UserId: otherUserId, // Thuộc nhân viên khác
            UserName: "ttb",
            Role: "STAFF",
            SalaryType: "Hourly",
            BaseRate: 30_000m,
            OtWeekday: "1.5",
            OtSunday: "2.0",
            Allowances: new List<EmployeeAllowanceDto>(),
            DebtAmount: 0m,
            Status: "Active",
            BankAccountNumber: null,
            BankName: null,
            BankAccountHolder: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        );

        mockService.Setup(s => s.GetEmployeeByIdAsync(otherEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherEmployeeResponse);

        var controller = new EmployeesController(mockService.Object)
        {
            ControllerContext = CreateContextWithUser(currentUserId, SystemRoles.Employee)
        };

        // Act
        var result = await controller.GetById(otherEmployeeId, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(objectResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal(ErrorCodes.Forbidden, apiResponse.ErrorCode);
    }

    [Fact]
    public async Task PayslipsController_GetById_WhenEmployeeViewsOtherEmployeePayslip_Returns403Forbidden()
    {
        // Arrange
        var payslipId = Guid.NewGuid();
        var currentUserId = "user_emp_001";
        var otherUserId = "user_emp_002";

        var mockService = new Mock<IPayslipService>();
        var dummyPayslip = new PayslipResponse(
            Id: payslipId,
            PayslipCode: "PL-202610-002",
            PeriodMonth: "2026-10",
            BranchId: Guid.NewGuid(),
            BranchName: "Chi nhánh 1",
            EmployeeId: Guid.NewGuid(),
            EmployeeCode: "NV0002",
            EmployeeName: "Trần Thị B",
            SalaryType: SalaryType.Monthly,
            BaseRate: 12_000_000m,
            StandardDays: 26,
            StandardDayHours: 8,
            WorkedDays: 26,
            TotalWorkHours: 208,
            TotalShifts: 26,
            OtHours: 0,
            PaidLeaveDays: 0,
            UnpaidLeaveDays: 0,
            TotalLateMinutes: 0,
            TotalEarlyLeaveMinutes: 0,
            BaseSalaryEarned: 12_000_000m,
            OvertimePay: 0,
            TotalAllowance: 0,
            AllowancesDetailJson: null,
            TotalEarnings: 12_000_000m,
            AdvanceDeduction: 0,
            RemainingDebt: 0,
            SocialInsurance: 0,
            BhxhAmount: 0,
            BhytAmount: 0,
            BhtnAmount: 0,
            TotalDeductions: 0,
            NetSalary: 12_000_000m,
            Status: PayslipStatus.Unpaid,
            PaidAt: null,
            PaymentMethod: null,
            PaidBy: null,
            BankAccountNumber: null,
            BankName: null,
            BankAccountHolder: null,
            CashbookTransactionId: null,
            CashbookVoucherCode: null,
            Note: null,
            ApprovedBy: null,
            ApprovedAt: null,
            CreatedAt: DateTime.UtcNow,
            UserId: otherUserId // Thuộc nhân viên khác
        );

        mockService.Setup(s => s.GetPayslipByIdAsync(payslipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyPayslip);

        var controller = new PayslipsController(mockService.Object)
        {
            ControllerContext = CreateContextWithUser(currentUserId, SystemRoles.Employee)
        };

        // Act
        var result = await controller.GetPayslipById(payslipId, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(objectResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal(ErrorCodes.Forbidden, apiResponse.ErrorCode);
    }

    [Fact]
    public async Task SchedulesController_GetById_WhenEmployeeViewsOtherEmployeeSchedule_Returns403Forbidden()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var currentUserId = "user_emp_001";
        var otherUserId = "user_emp_002";

        var mockService = new Mock<IScheduleService>();
        var dummySchedule = new ScheduleResponse(
            Id: scheduleId,
            EmployeeId: Guid.NewGuid(),
            EmployeeCode: "NV0002",
            EmployeeName: "Trần Thị B",
            EmployeeAvatarUrl: null,
            BranchId: Guid.NewGuid(),
            BranchName: "Chi nhánh 1",
            ShiftConfigId: null,
            WorkDate: DateTime.UtcNow.Date,
            ShiftCode: "CS",
            ShiftName: "Ca sáng",
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(16),
            DurationHours: 8,
            AllowedCheckInStart: TimeSpan.FromHours(7.5),
            AllowedCheckInEnd: TimeSpan.FromHours(8.5),
            IsCheckInNextDay: false,
            CheckInTime: null,
            CheckOutTime: null,
            CheckInMethod: null,
            CheckInFaceImageUrl: null,
            FaceMatchScore: null,
            CheckOutMethod: null,
            CheckOutFaceImageUrl: null,
            LateMinutes: 0,
            EarlyLeaveMinutes: 0,
            OvertimeMinutes: 0,
            ActualWorkHours: 8,
            Status: ScheduleStatus.Published,
            ApprovalType: null,
            ApprovalStatus: ScheduleApprovalStatus.None,
            ApprovalReason: null,
            EvidenceImageUrl: null,
            ApprovedBy: null,
            ApprovedAt: null,
            CreatedAt: DateTime.UtcNow,
            UserId: otherUserId // Thuộc nhân viên khác
        );

        mockService.Setup(s => s.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummySchedule);

        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(c => c.CurrentRole).Returns(SystemRoles.Employee);

        var controller = new SchedulesController(mockService.Object, mockTenantContext.Object)
        {
            ControllerContext = CreateContextWithUser(currentUserId, SystemRoles.Employee)
        };

        // Act
        var result = await controller.GetById(scheduleId, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(objectResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal(ErrorCodes.Forbidden, apiResponse.ErrorCode);
    }

    [Theory]
    [InlineData(typeof(EmployeesController), "GetEmployees", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(EmployeesController), "Create", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(EmployeesController), "Delete", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(PayslipsController), "GetPayslipsPaged", SystemRoles.AllStaffAndOwner)]
    [InlineData(typeof(PayslipsController), "GeneratePayrollBatch", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(PayslipsController), "PayPayslip", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(ShiftConfigsController), "Create", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(ShiftConfigsController), "Delete", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(SchedulesController), "BatchCreate", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(SchedulesController), "Publish", SystemRoles.OwnerOrAdmin)]
    [InlineData(typeof(BranchAttendancePoliciesController), "UpsertPolicy", SystemRoles.OwnerOrAdmin)]
    public void ControllerActions_HaveExpectedAuthorizeRoles(Type controllerType, string actionName, string expectedRoles)
    {
        var method = controllerType.GetMethod(actionName);
        Assert.NotNull(method);

        var authorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal(expectedRoles, authorizeAttr.Roles);
    }
}
