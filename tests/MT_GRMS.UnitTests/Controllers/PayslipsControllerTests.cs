using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class PayslipsControllerTests
{
    private readonly Mock<IPayslipService> _mockService;
    private readonly Mock<IEmployeeRepository> _mockEmpRepo;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly PayslipsController _controller;

    public PayslipsControllerTests()
    {
        _mockService = new Mock<IPayslipService>();
        _mockEmpRepo = new Mock<IEmployeeRepository>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns("tenant-01");
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.NewGuid());
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.StoreOwner);

        _controller = new PayslipsController(_mockService.Object, _mockEmpRepo.Object, _mockTenant.Object);
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _mockTenant.Object.CurrentUserId.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.StoreOwner)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static PayslipResponse CreateSamplePayslip(Guid id, string? userId = null)
    {
        return new PayslipResponse(
            Id: id,
            PayslipCode: "PAY-001",
            PeriodMonth: "2026-11",
            BranchId: Guid.NewGuid(),
            BranchName: "Main Branch",
            EmployeeId: Guid.NewGuid(),
            EmployeeCode: "EMP-01",
            EmployeeName: "Test Staff",
            SalaryType: SalaryType.Monthly,
            BaseRate: 10000000m,
            StandardDays: 26m,
            StandardDayHours: 8m,
            WorkedDays: 26m,
            TotalWorkHours: 208m,
            TotalShifts: 26,
            OtHours: 0m,
            PaidLeaveDays: 0m,
            UnpaidLeaveDays: 0m,
            TotalLateMinutes: 0,
            TotalEarlyLeaveMinutes: 0,
            BaseSalaryEarned: 10000000m,
            OvertimePay: 0m,
            TotalAllowance: 0m,
            AllowancesDetailJson: null,
            TotalEarnings: 10000000m,
            AdvanceDeduction: 0m,
            RemainingDebt: 0m,
            SocialInsurance: 0m,
            BhxhAmount: 0m,
            BhytAmount: 0m,
            BhtnAmount: 0m,
            TotalDeductions: 0m,
            NetSalary: 10000000m,
            Status: PayslipStatus.Unpaid,
            PaidAt: null,
            PaymentMethod: null,
            PaidBy: null,
            BankAccountNumber: null,
            BankName: null,
            BankAccountHolder: null,
            CashbookEntryId: null,
            CashbookVoucherCode: null,
            Note: null,
            ApprovedBy: null,
            ApprovedAt: null,
            CreatedAt: DateTime.UtcNow,
            UserId: userId ?? Guid.NewGuid().ToString()
        );
    }

    [Fact]
    public async Task GetPayslipsPaged_AsOwner_ShouldReturnOk()
    {
        var paged = new PagedResult<PayslipSummaryDto>
        {
            Items = new List<PayslipSummaryDto>(),
            Page = 1,
            PageSize = 20,
            TotalItems = 0
        };
        _mockService.Setup(s => s.GetPayslipsPagedAsync(It.IsAny<PayslipQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPayslipsPaged(null, null, null, null, 1, 20, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PagedResult<PayslipSummaryDto>>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetPayslipsPaged_AsStaff_WithLinkedEmployee_ShouldReturnOk()
    {
        var httpContext = new DefaultHttpContext();
        var staffUserId = Guid.NewGuid().ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staffUserId),
            new Claim(ClaimTypes.Role, SystemRoles.Staff)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var emp = new MT_GRMS.Domain.Entities.HR.Employee
        {
            Id = Guid.NewGuid(),
            UserId = staffUserId,
            FullName = "Staff Member",
            EmployeeCode = "EMP-02"
        };
        _mockEmpRepo.Setup(r => r.GetByUserIdAsync(staffUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emp);

        var paged = new PagedResult<PayslipSummaryDto>
        {
            Items = new List<PayslipSummaryDto>(),
            Page = 1,
            PageSize = 20,
            TotalItems = 0
        };
        _mockService.Setup(s => s.GetPayslipsPagedAsync(It.Is<PayslipQueryFilter>(f => f.EmployeeId == emp.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPayslipsPaged(null, null, null, null, 1, 20, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PagedResult<PayslipSummaryDto>>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetPayslipsPaged_AsStaff_NoLinkedEmployee_ShouldReturnEmpty()
    {
        var httpContext = new DefaultHttpContext();
        var staffUserId = Guid.NewGuid().ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staffUserId),
            new Claim(ClaimTypes.Role, SystemRoles.Staff)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        _mockEmpRepo.Setup(r => r.GetByUserIdAsync(staffUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MT_GRMS.Domain.Entities.HR.Employee?)null);

        var result = await _controller.GetPayslipsPaged(null, null, null, null, 1, 20, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PagedResult<PayslipSummaryDto>>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetPayslipById_AsOwner_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var payslip = CreateSamplePayslip(id);
        _mockService.Setup(s => s.GetPayslipByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payslip);

        var result = await _controller.GetPayslipById(id, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PayslipResponse>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetPayslipById_AsStaff_ForbiddenOtherUser_ShouldReturn403()
    {
        var httpContext = new DefaultHttpContext();
        var staffUserId = Guid.NewGuid().ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staffUserId),
            new Claim(ClaimTypes.Role, SystemRoles.Staff)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var id = Guid.NewGuid();
        var payslip = CreateSamplePayslip(id, userId: Guid.NewGuid().ToString()); // other
        _mockService.Setup(s => s.GetPayslipByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payslip);

        var result = await _controller.GetPayslipById(id, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PayslipResponse>>>(result);
        var status = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task GeneratePayrollBatch_ShouldReturnOk()
    {
        var request = new GeneratePayrollBatchRequest("2026-11");
        _mockService.Setup(s => s.GenerateMonthlyPayrollAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PayslipResponse>());

        var result = await _controller.GeneratePayrollBatch(request, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<IReadOnlyList<PayslipResponse>>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task RecalculatePayroll_ShouldReturnOk()
    {
        var request = new GeneratePayrollBatchRequest("2026-11");
        _mockService.Setup(s => s.GenerateMonthlyPayrollAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PayslipResponse>());

        var result = await _controller.RecalculatePayroll(request, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<IReadOnlyList<PayslipResponse>>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task AdvanceSalary_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new SalaryAdvanceRequest(1000000m);
        var payslip = CreateSamplePayslip(id);
        _mockService.Setup(s => s.AdvanceSalaryAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payslip);

        var result = await _controller.AdvanceSalary(id, request, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PayslipResponse>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task PayPayslip_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new PayPayslipRequest("BANK_TRANSFER");
        var payslip = CreateSamplePayslip(id);
        _mockService.Setup(s => s.PayPayslipAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payslip);

        var result = await _controller.PayPayslip(id, request, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<PayslipResponse>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task BatchPayPayslips_ShouldReturnOk()
    {
        var request = new BatchPayPayslipsRequest(new List<Guid> { Guid.NewGuid() }, "BANK_TRANSFER");
        var batchResult = new BatchPayResultDto(1, 1, 0, new List<string> { "PAY-001" }, new List<string>());
        _mockService.Setup(s => s.BatchPayPayslipsAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var result = await _controller.BatchPayPayslips(request, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<BatchPayResultDto>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task DeletePayslip_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.DeletePayslipAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.DeletePayslip(id, CancellationToken.None);
        var actionResult = Assert.IsType<ActionResult<ApiResponse<bool>>>(result);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.NotNull(ok.Value);
    }
}
