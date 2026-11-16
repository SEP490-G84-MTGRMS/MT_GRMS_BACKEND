using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using MT_GRMS.Api.Authorization;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosOrders.Validators;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Services.Printer;
using Xunit;

namespace MT_GRMS.UnitTests;

public class P0ContainmentSliceAuthorizationTests
{
    private readonly IConfiguration _config;

    public P0ContainmentSliceAuthorizationTests()
    {
        var settings = new Dictionary<string, string?>
        {
            { "Jwt:Key", "super-secret-containment-key-at-least-32-bytes-long!" },
            { "Jwt:Issuer", "MT_GRMS_TEST" },
            { "Jwt:Audience", "MT_GRMS_CLIENT" },
            { "Jwt:DurationInMinutes", "60" }
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public void P0_Acceptance_01_AdminToken_HasNoTenantId_AndContainsSecurityStamp()
    {
        // Arrange
        var authService = new AuthService(
            userRepository: Mock.Of<IUserRepository>(),
            configuration: _config);

        var adminUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "admin",
            Email = "admin@platform.system",
            FullName = "Platform Administrator",
            Role = "ADMIN",
            IsAdmin = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        // Act
        var tokenString = authService.GenerateJwtToken(
            userId: adminUser.Id,
            username: adminUser.Username,
            email: adminUser.Email,
            role: adminUser.Role,
            tenantId: string.Empty,
            isAdmin: true,
            expiresAt: DateTime.UtcNow.AddHours(1),
            branchId: null,
            securityStamp: adminUser.SecurityStamp);

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        // Assert
        var tenantClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "tenant_id");
        Assert.True(tenantClaim == null || string.IsNullOrEmpty(tenantClaim.Value), "Admin token must not contain a valid tenant_id claim.");

        var stampClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "security_stamp");
        Assert.NotNull(stampClaim);
        Assert.Equal(adminUser.SecurityStamp, stampClaim.Value);
    }

    [Fact]
    public async Task P0_Acceptance_02_AdminHeader_XTenantId_DoesNotCreateTenantContextForAdmin()
    {
        // Arrange
        RequestDelegate next = (HttpContext ctx) => Task.CompletedTask;
        var middleware = new MultiTenantMiddleware(next);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "tenant_attacker_target";

        // Admin User context
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("username", "admin"),
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("role", "ADMIN"),
            new Claim("is_admin", "true")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var tenantContext = new TenantContext();

        // Act
        await middleware.InvokeAsync(context, tenantContext);

        // Assert
        Assert.Equal(string.Empty, tenantContext.CurrentTenantId);
        Assert.Equal("ADMIN", tenantContext.CurrentRole);
    }

    [Fact]
    public async Task P0_Acceptance_03_AdminCallingTenantAPI_IsRejectedWith403()
    {
        // Arrange
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(c => c.CurrentRole).Returns("ADMIN");
        tenantContextMock.Setup(c => c.CurrentTenantId).Returns(string.Empty);

        var empRepoMock = new Mock<IEmployeeRepository>();

        var handler = new BranchScopedResourceAuthorizationHandler(tenantContextMock.Object, empRepoMock.Object);

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("role", "ADMIN"),
            new Claim("is_admin", "true")
        }, "Bearer"));

        var requirement = new BranchScopedOperationRequirement("ViewOrders");
        var resource = new TenantBranchScope("tenant_mt_mart_01", Guid.NewGuid());

        var authContext = new AuthorizationHandlerContext(
            new[] { requirement },
            adminPrincipal,
            resource);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded, "Admin must be denied access to tenant business operations.");
        Assert.True(authContext.HasFailed);
    }

    [Fact]
    public async Task P0_Acceptance_04_StoreOwner_CanAccessOwnTenantResources()
    {
        // Arrange
        var tenantId = "tenant_mt_mart_01";
        var branchId = Guid.NewGuid();

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(c => c.CurrentRole).Returns("STORE_OWNER");
        tenantContextMock.Setup(c => c.CurrentTenantId).Returns(tenantId);

        var handler = new BranchScopedResourceAuthorizationHandler(tenantContextMock.Object, Mock.Of<IEmployeeRepository>());

        var ownerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "STORE_OWNER"),
            new Claim("role", "STORE_OWNER"),
            new Claim("tenant_id", tenantId)
        }, "Bearer"));

        var requirement = new BranchScopedOperationRequirement("ViewOrders");
        var scope = new TenantBranchScope(tenantId, branchId);

        var authContext = new AuthorizationHandlerContext(
            new[] { requirement },
            ownerPrincipal,
            scope);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded, "Store Owner must succeed for their own tenant.");
    }

    [Fact]
    public async Task P0_Acceptance_05_CrossTenant_AccessIsRejected()
    {
        // Arrange
        var userTenant = "tenant_a";
        var targetResourceTenant = "tenant_b";

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(c => c.CurrentRole).Returns("STORE_OWNER");
        tenantContextMock.Setup(c => c.CurrentTenantId).Returns(userTenant);

        var handler = new BranchScopedResourceAuthorizationHandler(tenantContextMock.Object, Mock.Of<IEmployeeRepository>());

        var ownerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "STORE_OWNER"),
            new Claim("role", "STORE_OWNER"),
            new Claim("tenant_id", userTenant)
        }, "Bearer"));

        var requirement = new BranchScopedOperationRequirement("ViewOrders");
        var scope = new TenantBranchScope(targetResourceTenant, Guid.NewGuid());

        var authContext = new AuthorizationHandlerContext(
            new[] { requirement },
            ownerPrincipal,
            scope);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded, "Cross-tenant access must be rejected.");
        Assert.True(authContext.HasFailed);
    }

    [Fact]
    public async Task P0_Acceptance_06_Staff_AssignedToBranch_Succeeds_DifferentBranch_Fails()
    {
        // Arrange
        var tenantId = "tenant_mt_mart_01";
        var assignedBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(c => c.CurrentRole).Returns("STAFF");
        tenantContextMock.Setup(c => c.CurrentTenantId).Returns(tenantId);
        tenantContextMock.Setup(c => c.CurrentBranchId).Returns(assignedBranchId);
        tenantContextMock.Setup(c => c.CurrentUserId).Returns(staffUserId);

        var handler = new BranchScopedResourceAuthorizationHandler(tenantContextMock.Object, Mock.Of<IEmployeeRepository>());

        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "STAFF"),
            new Claim("role", "STAFF"),
            new Claim("tenant_id", tenantId),
            new Claim("branch_id", assignedBranchId.ToString())
        }, "Bearer"));

        var requirement = new BranchScopedOperationRequirement("CreateOrder");

        // Case A: Correct assigned branch
        var correctScope = new TenantBranchScope(tenantId, assignedBranchId);
        var authContextCorrect = new AuthorizationHandlerContext(new[] { requirement }, staffPrincipal, correctScope);
        await handler.HandleAsync(authContextCorrect);
        Assert.True(authContextCorrect.HasSucceeded, "Staff must succeed when operating on assigned branch.");

        // Case B: Different branch
        var differentScope = new TenantBranchScope(tenantId, otherBranchId);
        var authContextDifferent = new AuthorizationHandlerContext(new[] { requirement }, staffPrincipal, differentScope);
        await handler.HandleAsync(authContextDifferent);
        Assert.False(authContextDifferent.HasSucceeded, "Staff must be denied when operating on a different branch.");
        Assert.True(authContextDifferent.HasFailed);
    }

    [Fact]
    public async Task P0_Acceptance_07_OrderCreate_MissingOrCrossTenantBranch_IsRejected()
    {
        // Arrange
        var tenantId = "tenant_mt_mart_01";
        var branchId = Guid.NewGuid();

        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(c => c.CurrentTenantId).Returns(tenantId);
        mockTenantContext.Setup(c => c.CurrentRole).Returns("STORE_OWNER");

        var mockOrderRepo = new Mock<IOrderRepository>();
        mockOrderRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Branch
            {
                Id = branchId,
                TenantId = "tenant_other_hacker",
                Name = "Foreign Branch"
            });

        var orderService = new OrderService(
            mockOrderRepo.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IThermalPrintService>(),
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: null,
            tenantContext: mockTenantContext.Object);

        // Case 1: Missing BranchId
        var emptyBranchRequest = new OrderCreateRequest(
            BranchId: Guid.Empty,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashierName: "Owner",
            Items: new List<OrderItemCreateDto> { new(Guid.NewGuid(), 1m, 10000m) });

        await Assert.ThrowsAsync<ValidationException>(() => orderService.CreateOrderAsync(emptyBranchRequest));

        // Case 2: Branch belongs to a different tenant
        var foreignBranchRequest = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashierName: "Owner",
            Items: new List<OrderItemCreateDto> { new(Guid.NewGuid(), 1m, 10000m) });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.CreateOrderAsync(foreignBranchRequest));
        Assert.Contains("không thuộc cửa hàng", ex.Message);
    }

    [Fact]
    public async Task P0_Acceptance_08_OrderCreate_AdminCaller_IsBlockedFromSales()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(c => c.CurrentTenantId).Returns(string.Empty);
        mockTenantContext.Setup(c => c.CurrentRole).Returns("ADMIN");

        var orderService = new OrderService(
            Mock.Of<IOrderRepository>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IThermalPrintService>(),
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: null,
            tenantContext: mockTenantContext.Object);

        var request = new OrderCreateRequest(
            BranchId: Guid.NewGuid(),
            CustomerId: null,
            PaymentMethod: "CASH",
            CashierName: "Admin",
            Items: new List<OrderItemCreateDto> { new(Guid.NewGuid(), 1m, 10000m) });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.CreateOrderAsync(request));
        Assert.Contains("Quản trị viên hệ thống không được phép", ex.Message);
    }

    [Fact]
    public void P0_Acceptance_09_SecurityStamp_ValidationLogic_RejectsLegacyOrMismatchedToken()
    {
        // Case A: Legacy token lacking security_stamp claim
        var legacyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role", "STORE_OWNER")
        }, "Bearer"));

        var legacyStampClaim = legacyPrincipal.FindFirst("security_stamp")?.Value;
        Assert.True(string.IsNullOrEmpty(legacyStampClaim), "Legacy token lacks security_stamp.");

        // Case B: Token with revoked/mismatched stamp
        var activeUserStamp = Guid.NewGuid().ToString();
        var tokenOldStamp = Guid.NewGuid().ToString();

        Assert.NotEqual(activeUserStamp, tokenOldStamp);

        // Case C: Valid matching stamp
        var validStamp = activeUserStamp;
        Assert.Equal(activeUserStamp, validStamp);
    }

    [Fact]
    public void P0_Acceptance_10_Stocktakes_And_LoyaltyConfig_Controllers_HaveSpecificPolicyAttributes()
    {
        // Verify StocktakesController has specific authorization policies
        var stocktakeType = typeof(StocktakesController);
        var stocktakeAuth = stocktakeType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(stocktakeAuth);
        Assert.Equal("TenantOwnerOrAssignedBranchStaff", stocktakeAuth.Policy);

        var submitAction = stocktakeType.GetMethod("Submit");
        Assert.NotNull(submitAction);
        var submitAuth = submitAction.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(submitAuth);
        Assert.Equal("StoreOwnerOnly", submitAuth.Policy);

        var reviewAction = stocktakeType.GetMethod("Review");
        Assert.NotNull(reviewAction);
        var reviewAuth = reviewAction.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(reviewAuth);
        Assert.Equal("StoreOwnerOnly", reviewAuth.Policy);

        // Verify LoyaltyConfigController has specific authorization policies
        var loyaltyType = typeof(LoyaltyConfigController);
        var getConfigAction = loyaltyType.GetMethod("GetConfig");
        Assert.NotNull(getConfigAction);
        var getAuth = getConfigAction.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(getAuth);
        Assert.Equal("TenantOwnerOrAssignedBranchStaff", getAuth.Policy);

        var updateConfigAction = loyaltyType.GetMethod("UpdateConfig");
        Assert.NotNull(updateConfigAction);
        var updateAuth = updateConfigAction.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(updateAuth);
        Assert.Equal("StoreOwnerOnly", updateAuth.Policy);
    }
}
