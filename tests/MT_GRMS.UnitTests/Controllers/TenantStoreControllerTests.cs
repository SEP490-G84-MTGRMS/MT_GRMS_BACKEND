using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class TenantStoreControllerTests
{
    private readonly Mock<ITenantService> _mockTenantService;
    private readonly Mock<ISubscriptionPlanService> _mockPlanService;
    private const string TenantId = "tenant-store-01";
    private readonly string _userId = Guid.NewGuid().ToString();

    public TenantStoreControllerTests()
    {
        _mockTenantService = new Mock<ITenantService>();
        _mockPlanService = new Mock<ISubscriptionPlanService>();
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = TenantId;
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantStoreDb_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, new FakeTenantContext());
    }

    private ControllerContext CreateControllerContext(string? tenantId = TenantId, string? userId = null, string role = "STORE_OWNER")
    {
        var effectiveUserId = userId ?? _userId;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role),
            new(ClaimTypes.NameIdentifier, effectiveUserId),
            new("sub", effectiveUserId)
        };
        if (!string.IsNullOrEmpty(tenantId))
        {
            claims.Add(new Claim("tenant_id", tenantId));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetStoreInfo_WhenTenantNotFound_ReturnsNotFound()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.GetStoreInfo(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetStoreInfo_WhenTenantExists_ReturnsOk()
    {
        using var context = CreateDbContext();
        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            SubscriptionStatus = "ACTIVE"
        });
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            IsActive = true
        });
        await context.SaveChangesAsync();

        _mockTenantService.Setup(s => s.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantDetailResponse(TenantId, "mymart", "My Mart", "M01", null, null, null, null, "ACTIVE", null, "Gói Chuẩn", null, null, null, null, null, null, 1, 1, 1, 3, new List<TenantDetailBranchSummaryDto>(), DateTime.UtcNow, null, 0m));

        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.GetStoreInfo(CancellationToken.None);
        var okObj = Assert.IsType<OkObjectResult>(result);
        var apiResp = Assert.IsAssignableFrom<ApiResponse<TenantDetailResponse>>(okObj.Value);
        Assert.True(apiResp.Success);
    }

    [Fact]
    public async Task UpdateStoreInfo_WhenValid_ReturnsOk()
    {
        using var context = CreateDbContext();
        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            SubscriptionStatus = "ACTIVE"
        });
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var req = new UpdateTenantRequest("Updated Mart", "0123456789", "0909090909", "123 Street", "HCM");
        _mockTenantService.Setup(s => s.UpdateTenantAsync(TenantId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantDetailResponse(TenantId, "mymart", "Updated Mart", "M01", null, null, null, null, "ACTIVE", null, "Gói Chuẩn", null, null, null, null, null, null, 1, 1, 1, 3, new List<TenantDetailBranchSummaryDto>(), DateTime.UtcNow, null, 0m));

        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.UpdateStoreInfo(req, CancellationToken.None);
        var okObj = Assert.IsType<OkObjectResult>(result);
        var apiResp = Assert.IsAssignableFrom<ApiResponse<TenantDetailResponse>>(okObj.Value);
        Assert.True(apiResp.Success);
    }

    [Fact]
    public async Task GetPricingPlans_ReturnsOfficialPlansWithTrial()
    {
        using var context = CreateDbContext();
        _mockPlanService.Setup(s => s.GetPlansAsync(It.IsAny<PlanFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanSummaryResponse>
            {
                new("1", "STANDARD", "Gói Chuẩn", "Desc", 99000m, 990000m, "ACTIVE", 1, new PlanQuotaDto(), new PlanFeatureDto(), DateTime.UtcNow, null),
                new("2", "PRO", "Gói Pro", "Desc", 249000m, 2490000m, "ACTIVE", 1, new PlanQuotaDto(), new PlanFeatureDto(), DateTime.UtcNow, null)
            });

        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.GetPricingPlans(CancellationToken.None);
        var okObj = Assert.IsType<OkObjectResult>(result);
        var apiResp = Assert.IsAssignableFrom<ApiResponse<IReadOnlyList<PlanSummaryResponse>>>(okObj.Value);
        Assert.True(apiResp.Success);
        // Automatically ensured TRIAL plan is prepended
        Assert.Contains(apiResp.Data!, p => p.Code == "TRIAL");
    }

    [Fact]
    public async Task PurchaseSubscription_Validations()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        // 1. Invalid ExtraStorageGb
        var resStorage = await controller.PurchaseSubscription(new PurchaseSubscriptionRequest("STANDARD", 1, ExtraStorageGb: 150), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resStorage);

        // 2. Tenant not found
        var resNotFound = await controller.PurchaseSubscription(new PurchaseSubscriptionRequest("STANDARD", 1), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // Seed tenant
        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            CurrentPlan = "PRO",
            SubscriptionStatus = "ACTIVE"
        });
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            IsActive = true
        });
        await context.SaveChangesAsync();

        // 3. PlanCode TRIAL -> BadRequest
        var resTrial = await controller.PurchaseSubscription(new PurchaseSubscriptionRequest("TRIAL", 1), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resTrial);

        // 4. Invalid PlanCode -> BadRequest
        var resInvalidCode = await controller.PurchaseSubscription(new PurchaseSubscriptionRequest("CUSTOM", 1), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInvalidCode);

        // 5. Downgrade from PRO to STANDARD -> BadRequest
        var resDowngrade = await controller.PurchaseSubscription(new PurchaseSubscriptionRequest("STANDARD", 1), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resDowngrade);
    }

    [Fact]
    public async Task PurchaseSubscription_StandardPlanWithExtraBranches_ReturnsInvalidPlanAddon()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            CurrentPlan = "STANDARD",
            SubscriptionStatus = "ACTIVE"
        });
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var request = new PurchaseSubscriptionRequest("STANDARD", 1, ExtraBranches: 2);
        var result = await controller.PurchaseSubscription(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var apiResp = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(badRequest.Value);
        Assert.Equal("INVALID_PLAN_ADDON", apiResp.ErrorCode);
    }

    [Fact]
    public async Task ActivateTrialSubscription_InvalidTenant_ReturnsForbidden()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext(tenantId: "DEFAULT")
        };

        var result = await controller.ActivateTrialSubscription(CancellationToken.None);
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ActivateTrialSubscription_NoMembership_ReturnsForbidden()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        // User is not in TenantUsers
        var result = await controller.ActivateTrialSubscription(CancellationToken.None);
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ActivateTrialSubscription_UserNotVerified_ReturnsForbidden()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            SubscriptionStatus = "NO_PLAN"
        });
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            Role = "STORE_OWNER",
            IsActive = true
        });
        context.Users.Add(new User
        {
            Id = _userId,
            UserName = "owner",
            IsEmailVerified = false // Not verified
        });
        await context.SaveChangesAsync();

        var result = await controller.ActivateTrialSubscription(CancellationToken.None);
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ActivateTrialSubscription_TenantTrialAlreadyUsed_ReturnsConflict()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            SubscriptionStatus = "EXPIRED",
            TrialStartedAt = DateTime.UtcNow.AddDays(-10),
            TrialExpiresAt = DateTime.UtcNow.AddDays(-7)
        });
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            Role = "STORE_OWNER",
            IsActive = true
        });
        context.Users.Add(new User
        {
            Id = _userId,
            UserName = "owner",
            IsEmailVerified = true
        });
        await context.SaveChangesAsync();

        var result = await controller.ActivateTrialSubscription(CancellationToken.None);
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ActivateTrialSubscription_Success_ActivatesTrial()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        var tenant = new Tenant
        {
            Id = TenantId,
            Name = "My Mart",
            SubscriptionStatus = "NO_PLAN"
        };
        var user = new User
        {
            Id = _userId,
            UserName = "owner",
            IsEmailVerified = true
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = TenantId,
            UserId = _userId,
            Role = "STORE_OWNER",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var result = await controller.ActivateTrialSubscription(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        Assert.Equal("TRIAL", tenant.CurrentPlan);
        Assert.Equal("TRIAL", tenant.SubscriptionStatus);
        Assert.NotNull(tenant.TrialStartedAt);
        Assert.NotNull(user.TrialConsumedAt);
    }

    [Fact]
    public async Task GetSubscriptionOrderHistory_TenantNotFound_ReturnsNotFound()
    {
        using var context = CreateDbContext();
        var controller = new TenantStoreController(_mockTenantService.Object, _mockPlanService.Object, context)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.GetSubscriptionOrderHistory(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }
}
