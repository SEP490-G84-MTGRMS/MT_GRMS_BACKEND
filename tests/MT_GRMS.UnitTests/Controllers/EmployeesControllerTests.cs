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
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class EmployeesControllerTests
{
    private readonly Mock<IEmployeeService> _mockService;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly EmployeesController _controller;

    public EmployeesControllerTests()
    {
        _mockService = new Mock<IEmployeeService>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns("tenant-01");
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.NewGuid());
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.StoreOwner);

        _controller = new EmployeesController(_mockService.Object, _mockTenant.Object);
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _mockTenant.Object.CurrentUserId.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.StoreOwner)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static EmployeeResponse SampleEmployeeResponse(Guid id, string? userId = null)
    {
        return new EmployeeResponse(
            Id: id,
            EmployeeCode: "EMP-001",
            FullName: "Test Employee",
            CitizenId: "012345678901",
            DateOfBirth: new DateTime(1995, 1, 1),
            Gender: "Male",
            Phone: "0901234567",
            Email: "test@example.com",
            Address: "123 Street",
            StartDate: DateTime.UtcNow.Date,
            Note: "Note",
            AvatarUrl: "http://avatar",
            BranchId: Guid.NewGuid(),
            BranchName: "Main Branch",
            UserId: userId ?? Guid.NewGuid().ToString(),
            UserName: "testemp",
            Role: SystemRoles.Staff,
            SalaryType: "monthly",
            BaseRate: 10000000m,
            OtWeekday: "1.5",
            OtSunday: "2.0",
            Allowances: new List<EmployeeAllowanceDto>(),
            DebtAmount: 0m,
            Status: "Active",
            BankAccountNumber: "123456",
            BankName: "VCB",
            BankAccountHolder: "Test Employee",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            FaceDescriptor: null,
            FaceImageUrls: null,
            FaceRegisteredAt: null,
            HasLaborContract: true,
            InsuranceBaseSalary: 5000000m
        );
    }

    [Fact]
    public async Task GetEmployees_ShouldReturnOk()
    {
        var filter = new EmployeeQueryFilter();
        var paged = new PagedResult<EmployeeSummaryDto>
        {
            Items = new List<EmployeeSummaryDto>(),
            Page = 1,
            PageSize = 20,
            TotalItems = 0
        };
        _mockService.Setup(s => s.GetEmployeesPagedAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetEmployees(filter, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetMyProfile_WhenFound_ShouldReturnOk()
    {
        var userId = _mockTenant.Object.CurrentUserId.ToString();
        var response = SampleEmployeeResponse(Guid.NewGuid(), userId);
        _mockService.Setup(s => s.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetMyProfile(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetMyProfile_WhenNotFound_ShouldReturnNotFound()
    {
        var userId = _mockTenant.Object.CurrentUserId.ToString();
        _mockService.Setup(s => s.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeResponse?)null);

        var result = await _controller.GetMyProfile(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetById_WhenAdminOrOwner_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var response = SampleEmployeeResponse(id);
        _mockService.Setup(s => s.GetEmployeeByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetById(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetById_WhenNotFoundException_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetEmployeeByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Employee not found"));

        var result = await _controller.GetById(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Create_ShouldReturnCreatedAtAction()
    {
        var request = new CreateEmployeeRequest(
            FullName: "New Emp",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: Guid.NewGuid()
        );
        var response = SampleEmployeeResponse(Guid.NewGuid());
        _mockService.Setup(s => s.CreateEmployeeAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Create(request, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(created.Value);
    }

    [Fact]
    public async Task Update_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new UpdateEmployeeRequest(
            FullName: "Updated Emp",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: Guid.NewGuid()
        );
        var response = SampleEmployeeResponse(id);
        _mockService.Setup(s => s.UpdateEmployeeAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Update(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task UpdateStatus_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new UpdateEmployeeStatusRequest("Inactive");
        _mockService.Setup(s => s.UpdateStatusAsync(id, "Inactive", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.UpdateStatus(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task CreateIdentityAccount_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new CreateIdentityAccountRequest("empuser", "Pass@123");
        _mockService.Setup(s => s.CreateIdentityAccountAsync(id, It.IsAny<CreateIdentityAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-id-123");

        var result = await _controller.CreateIdentityAccount(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task UpdateIdentityAccount_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new CreateIdentityAccountRequest("empuser2", "Pass@456");
        _mockService.Setup(s => s.UpdateIdentityAccountAsync(id, It.IsAny<CreateIdentityAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-id-123");

        var result = await _controller.UpdateIdentityAccount(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Delete_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.DeleteEmployeeAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.Delete(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Delete_WhenHasWorkHistory_ShouldReturnOkDeactivated()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.DeleteEmployeeAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("EMPLOYEE_HAS_WORK_HISTORY", "Has history"));

        var result = await _controller.Delete(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task RegisterFaceDescriptor_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new RegisterFaceDescriptorRequest("[0.1, 0.2, 0.3]");
        _mockService.Setup(s => s.RegisterFaceDescriptorAsync(id, "[0.1, 0.2, 0.3]", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.RegisterFaceDescriptor(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task RegisterFaceDescriptor_EmptyDescriptor_ShouldReturnBadRequest()
    {
        var id = Guid.NewGuid();
        var request = new RegisterFaceDescriptorRequest("");

        var result = await _controller.RegisterFaceDescriptor(id, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetMyProfile_WhenMissingUserId_ShouldReturnUnauthorized()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Empty);

        var result = await _controller.GetMyProfile(CancellationToken.None);
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.NotNull(unauth.Value);
    }

    [Fact]
    public async Task GetById_WhenStaffAccessingOtherProfile_ShouldReturnForbidden()
    {
        var id = Guid.NewGuid();
        var staffUserId = Guid.NewGuid().ToString();
        var otherUserId = Guid.NewGuid().ToString();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staffUserId),
            new Claim(ClaimTypes.Role, SystemRoles.Staff)
        };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.Staff);
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Parse(staffUserId));

        var response = SampleEmployeeResponse(id, otherUserId);
        _mockService.Setup(s => s.GetEmployeeByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetById(id, CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);
    }

    [Fact]
    public async Task Create_Exceptions_ShouldReturnAppropriateStatuses()
    {
        var request = new CreateEmployeeRequest(
            FullName: "New Emp",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: Guid.NewGuid()
        );

        _mockService.Setup(s => s.CreateEmployeeAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid arg"));
        var resArg = await _controller.Create(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resArg);

        _mockService.Setup(s => s.CreateEmployeeAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Biz error"));
        var resBiz = await _controller.Create(request, CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resBiz);
        Assert.Equal(422, objRes.StatusCode);
    }

    [Fact]
    public async Task Update_Exceptions_ShouldReturnAppropriateStatuses()
    {
        var id = Guid.NewGuid();
        var request = new UpdateEmployeeRequest(
            FullName: "Updated Emp",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: Guid.NewGuid()
        );

        _mockService.Setup(s => s.UpdateEmployeeAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        var resNotFound = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        _mockService.Setup(s => s.UpdateEmployeeAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid arg"));
        var resArg = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resArg);

        _mockService.Setup(s => s.UpdateEmployeeAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Biz error"));
        var resBiz = await _controller.Update(id, request, CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resBiz);
        Assert.Equal(422, objRes.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_Exceptions_ShouldReturnAppropriateStatuses()
    {
        var id = Guid.NewGuid();
        var request = new UpdateEmployeeStatusRequest("Inactive");

        _mockService.Setup(s => s.UpdateStatusAsync(id, "Inactive", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        var resNotFound = await _controller.UpdateStatus(id, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        _mockService.Setup(s => s.UpdateStatusAsync(id, "Inactive", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid status"));
        var resArg = await _controller.UpdateStatus(id, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resArg);
    }

    [Fact]
    public async Task IdentityAccount_Exceptions_ShouldReturnAppropriateStatuses()
    {
        var id = Guid.NewGuid();
        var request = new CreateIdentityAccountRequest("empuser", "Pass@123");

        // Create
        _mockService.Setup(s => s.CreateIdentityAccountAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.CreateIdentityAccount(id, request, CancellationToken.None));

        _mockService.Setup(s => s.CreateIdentityAccountAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid arg"));
        Assert.IsType<BadRequestObjectResult>(await _controller.CreateIdentityAccount(id, request, CancellationToken.None));

        _mockService.Setup(s => s.CreateIdentityAccountAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Biz error"));
        var resBizCreate = Assert.IsType<ObjectResult>(await _controller.CreateIdentityAccount(id, request, CancellationToken.None));
        Assert.Equal(422, resBizCreate.StatusCode);

        // Update
        _mockService.Setup(s => s.UpdateIdentityAccountAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.UpdateIdentityAccount(id, request, CancellationToken.None));

        _mockService.Setup(s => s.UpdateIdentityAccountAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid arg"));
        Assert.IsType<BadRequestObjectResult>(await _controller.UpdateIdentityAccount(id, request, CancellationToken.None));

        _mockService.Setup(s => s.UpdateIdentityAccountAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Biz error"));
        var resBizUpdate = Assert.IsType<ObjectResult>(await _controller.UpdateIdentityAccount(id, request, CancellationToken.None));
        Assert.Equal(422, resBizUpdate.StatusCode);
    }

    private static Microsoft.EntityFrameworkCore.DbUpdateException CreatePostgresDbUpdateException(string constraintName)
    {
        var pgEx = (Npgsql.PostgresException)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Npgsql.PostgresException));
        var bindingFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        var sqlStateField = typeof(Npgsql.PostgresException).GetField("_sqlState", bindingFlags)
            ?? typeof(Npgsql.PostgresException).GetField("<SqlState>k__BackingField", bindingFlags);
        sqlStateField?.SetValue(pgEx, "23505");

        var constraintField = typeof(Npgsql.PostgresException).GetField("_constraintName", bindingFlags)
            ?? typeof(Npgsql.PostgresException).GetField("<ConstraintName>k__BackingField", bindingFlags);
        constraintField?.SetValue(pgEx, constraintName);

        return new Microsoft.EntityFrameworkCore.DbUpdateException("Conflict", pgEx);
    }

    [Fact]
    public async Task Create_DbUpdateException_AllConstraints_ShouldReturnConflict()
    {
        var req = new CreateEmployeeRequest("Emp", "012345678901", "0912345678", Guid.NewGuid());

        var constraints = new[] { "IX_CitizenId", "IX_EmployeeCode", "IX_UserName", "IX_Email", "IX_Phone", "IX_Unknown" };
        foreach (var c in constraints)
        {
            _mockService.Setup(s => s.CreateEmployeeAsync(req, It.IsAny<CancellationToken>()))
                .ThrowsAsync(CreatePostgresDbUpdateException(c));
            var res = await _controller.Create(req, CancellationToken.None);
            Assert.IsType<ConflictObjectResult>(res);
        }

        // BusinessException with DUPLICATE
        _mockService.Setup(s => s.CreateEmployeeAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("DUPLICATE_CODE", "Already exists"));
        var dupRes = await _controller.Create(req, CancellationToken.None);
        var objDup = Assert.IsType<ObjectResult>(dupRes);
        Assert.Equal(409, objDup.StatusCode);
    }

    [Fact]
    public async Task Update_DbUpdateException_AllConstraints_ShouldReturnConflict()
    {
        var id = Guid.NewGuid();
        var req = new UpdateEmployeeRequest("Emp", "012345678901", "0912345678", Guid.NewGuid());

        var constraints = new[] { "IX_CitizenId", "IX_EmployeeCode", "IX_UserName", "IX_Email", "IX_Phone", "IX_Unknown" };
        foreach (var c in constraints)
        {
            _mockService.Setup(s => s.UpdateEmployeeAsync(id, req, It.IsAny<CancellationToken>()))
                .ThrowsAsync(CreatePostgresDbUpdateException(c));
            var res = await _controller.Update(id, req, CancellationToken.None);
            Assert.IsType<ConflictObjectResult>(res);
        }
    }

    [Fact]
    public async Task Delete_DbUpdateException_AndWorkHistory_ShouldWork()
    {
        var id = Guid.NewGuid();

        // Work history
        _mockService.Setup(s => s.DeleteEmployeeAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("EMPLOYEE_HAS_WORK_HISTORY", "Cannot delete active employee"));
        var resHistory = await _controller.Delete(id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resHistory);

        // DbUpdateException constraints
        var constraints = new[] { "IX_UserName", "IX_Email", "IX_Phone", "IX_Unknown" };
        foreach (var c in constraints)
        {
            _mockService.Setup(s => s.DeleteEmployeeAsync(id, It.IsAny<CancellationToken>()))
                .ThrowsAsync(CreatePostgresDbUpdateException(c));
            var res = await _controller.Delete(id, CancellationToken.None);
            Assert.IsType<ConflictObjectResult>(res);
        }
    }

    [Fact]
    public async Task RegisterFaceDescriptor_Empty_ShouldReturnBadRequest()
    {
        var id = Guid.NewGuid();
        var reqEmpty = new RegisterFaceDescriptorRequest("");
        var resEmpty = await _controller.RegisterFaceDescriptor(id, reqEmpty, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmpty);
    }

    [Fact]
    public async Task Delete_And_RegisterFace_Exceptions_ShouldReturnAppropriateStatuses()
    {
        var id = Guid.NewGuid();

        // Delete NotFound
        _mockService.Setup(s => s.DeleteEmployeeAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.Delete(id, CancellationToken.None));

        // Delete generic BusinessException
        _mockService.Setup(s => s.DeleteEmployeeAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("GENERIC_ERROR", "Biz error"));
        Assert.IsType<BadRequestObjectResult>(await _controller.Delete(id, CancellationToken.None));

        // RegisterFace exceptions
        var faceReq = new RegisterFaceDescriptorRequest("[0.1, 0.2]");
        _mockService.Setup(s => s.RegisterFaceDescriptorAsync(id, "[0.1, 0.2]", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.RegisterFaceDescriptor(id, faceReq, CancellationToken.None));

        _mockService.Setup(s => s.RegisterFaceDescriptorAsync(id, "[0.1, 0.2]", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Face recognition failed"));
        Assert.IsType<BadRequestObjectResult>(await _controller.RegisterFaceDescriptor(id, faceReq, CancellationToken.None));
    }
}
