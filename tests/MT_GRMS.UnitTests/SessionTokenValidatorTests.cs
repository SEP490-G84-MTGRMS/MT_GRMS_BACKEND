using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MT_GRMS.Api.Authorization;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.UnitTests;

public class SessionTokenValidatorTests
{
    [Theory]
    [InlineData(EmployeeStatus.Active, false)]
    [InlineData(EmployeeStatus.Inactive, true)]
    public async Task EmployeeStatusChange_IsCheckedWithoutStampRotation(EmployeeStatus status, bool shouldReject)
    {
        var branch = Guid.NewGuid();
        var user = new User { Id = "staff", IsActive = true, SecurityStamp = "unchanged" };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-a", UserId = user.Id, Role = "STAFF", IsActive = true });
        var users = new Mock<IUserRepository>();
        users.Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        users.Setup(repository => repository.GetEmployeeBranchIdAsync(user.Id, "tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(repository => repository.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { UserId = user.Id, Status = status });
        using var services = new ServiceCollection().AddSingleton(users.Object).AddSingleton(employees.Object).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var context = new TokenValidatedContext(httpContext,
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Role, "STAFF"),
                new Claim("tenant_id", "tenant-a"),
                new Claim("branch_id", branch.ToString()),
                new Claim("security_stamp", "unchanged")
            }, "Bearer"))
        };

        await SessionTokenValidator.ValidateAsync(context);

        Assert.Equal(shouldReject, context.Result?.Failure != null);
    }
}
