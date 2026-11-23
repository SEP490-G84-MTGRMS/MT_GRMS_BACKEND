using MT_GRMS.Infrastructure.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class TenantStoreControllerTests
{
    private readonly AppDbContext _dbContext;
    private readonly MockTenantService _tenantService;
    private readonly MockPlanService _planService;
    private readonly TenantStoreController _controller;

    public TenantStoreControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_TenantStoreTest_{Guid.NewGuid()}")
            .Options;

        _dbContext = new AppDbContext(options);

        // Seed demo tenant
        _dbContext.Tenants.Add(new Tenant
        {
            Id = "tenant-01",
            Name = "Hà Nội Mart",
            Subdomain = "hanoi-mart",
            Code = "HN-MART",
            Status = "ACTIVE",
            CurrentPlan = "PRO",
            WalletBalance = 20000000m
        });
        _dbContext.Users.Add(new User
        {
            Id = "user-owner-01", UserName = "owner", FullName = "Owner",
            Email = "owner@example.com", NormalizedEmail = "owner@example.com",
            PasswordHash = "test", IsEmailVerified = true
        });
        _dbContext.TenantUsers.Add(new TenantUser
        {
            TenantId = "tenant-01", UserId = "user-owner-01", Role = "STORE_OWNER", IsActive = true
        });
        _dbContext.SaveChanges();

        _tenantService = new MockTenantService();
        _planService = new MockPlanService();

        _planService.Plans.Add(new PlanSummaryResponse(
            Id: "plan-pro",
            Code: "PRO",
            Name: "Gói Chuyên Nghiệp",
            Description: "Dành cho chuỗi cửa hàng",
            MonthlyPrice: 330000m,
            YearlyPrice: 3300000m,
            Status: "ACTIVE",
            ActiveTenantCount: 5,
            Quotas: new PlanQuotaDto(5, 20, 10, "Daily"),
            Features: new PlanFeatureDto(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        ));

        _controller = new TenantStoreController(_tenantService, _planService, _dbContext, new TenantSubscriptionService(_dbContext));

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "StoreOwner"),
            new Claim(ClaimTypes.NameIdentifier, "user-owner-01"),
            new Claim("tenant_id", "tenant-01"),
            new Claim(ClaimTypes.Role, "STORE_OWNER")
        }, "mock_auth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetStoreInfo_WithTenantIdClaim_Returns200Ok_WithDetail()
    {
        var result = await _controller.GetStoreInfo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("hanoi-mart", resp.Data!.Subdomain);
        Assert.Equal("HN-MART", resp.Data!.Code);
        Assert.Equal("Gói Pro", resp.Data!.PlanName);
    }

    [Fact]
    public async Task UpdateStoreInfo_ValidRequest_Returns200Ok_WithUpdated()
    {
        var req = new UpdateTenantRequest("Hà Nội Mart Cập Nhật", "010999888", "0919001122", "456 Cầu Giấy", "Hà Nội");
        var result = await _controller.UpdateStoreInfo(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Hà Nội Mart Cập Nhật", resp.Data!.Name);
    }

    [Fact]
    public async Task ActivateTrial_EligibleTenant_StartsOnceForExactly72Hours()
    {
        var tenant = await _dbContext.Tenants.FindAsync("tenant-01");
        tenant!.CurrentPlan = null;
        tenant.SubscriptionStatus = "NO_PLAN";
        tenant.FirstPaidAt = null;
        await _dbContext.SaveChangesAsync();

        var first = Assert.IsType<OkObjectResult>(await _controller.ActivateTrialSubscription(CancellationToken.None));
        Assert.NotNull(first.Value);
        Assert.Equal("TRIAL", tenant.SubscriptionStatus);
        Assert.Equal(TimeSpan.FromHours(72), tenant.TrialExpiresAt!.Value - tenant.TrialStartedAt!.Value);

        var originalExpiry = tenant.TrialExpiresAt;
        Assert.IsType<OkObjectResult>(await _controller.ActivateTrialSubscription(CancellationToken.None));
        Assert.Equal(originalExpiry, tenant.TrialExpiresAt);
    }

    [Fact]
    public async Task GetStoreInfo_IgnoresSpoofedTenantHeader()
    {
        _controller.Request.Headers["X-Tenant-Id"] = "tenant-other";
        var result = await _controller.GetStoreInfo(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetPricingPlans_Returns200Ok_WithActivePlans()
    {
        var result = await _controller.GetPricingPlans(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<PlanSummaryResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(3, resp.Data!.Count);
        Assert.Contains(resp.Data!, p => p.Code == "STANDARD");
        Assert.Contains(resp.Data!, p => p.Code == "TRIAL");
        var proPlan = Assert.Single(resp.Data!, p => p.Code == "PRO");
        Assert.Equal(330000m, proPlan.MonthlyPrice);
    }

    [Fact]
    public async Task PurchaseSubscription_ValidRequest_ExtendsSubscriptionAndReturns200Ok()
    {
        var tenant = await _dbContext.Tenants.FindAsync("tenant-01");
        if (tenant != null)
        {
            tenant.WalletBalance = 10000000m;
        }

        _dbContext.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Gói Chuyên Nghiệp",
            PriceMonthly = 330000m,
            PriceYearly = 3300000m,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();

        var req = new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: 12,
            ExtraBranches: 1,
            ExtraWarehouses: 0,
            PaymentMethod: "Wallet"
        );

        var result = await _controller.PurchaseSubscription(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("PRO", resp.Data!.PlanCode);
        Assert.True(resp.Data.DaysRemaining > 300);
        Assert.True(resp.Data.TotalAmount > 0);

        var updatedTenant = await _dbContext.Tenants.FindAsync("tenant-01");
        Assert.NotNull(updatedTenant);
        Assert.Equal("PRO", updatedTenant!.CurrentPlan);
        Assert.NotNull(updatedTenant.SubscriptionExpiresAt);
    }

    [Fact]
    public async Task PurchaseSubscription_FromProToStandard_Returns400DowngradeNotAllowed()
    {
        var tenant = await _dbContext.Tenants.FindAsync("tenant-01");
        Assert.NotNull(tenant);
        tenant!.CurrentPlan = "PRO";
        tenant.SubscriptionStatus = "ACTIVE";
        tenant.WalletBalance = 10000000m;
        await _dbContext.SaveChangesAsync();

        var req = new PurchaseSubscriptionRequest(
            PlanCode: "STANDARD",
            Months: 1,
            ExtraBranches: 0,
            ExtraWarehouses: 0,
            PaymentMethod: "Wallet"
        );

        var result = await _controller.PurchaseSubscription(req, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(bad.Value);
        Assert.False(resp.Success);
        Assert.Equal("DOWNGRADE_NOT_ALLOWED", resp.ErrorCode);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(3, 30)]
    [InlineData(0, 1)]
    public async Task PurchaseSubscription_FromStandardToPro_RecordsSuccessfulUpgrade(int months, int daysRemaining)
    {
        var tenant = await _dbContext.Tenants.FindAsync("tenant-01");
        Assert.NotNull(tenant);
        tenant!.CurrentPlan = "STANDARD";
        tenant.SubscriptionStatus = "ACTIVE";
        tenant.WalletBalance = 10000000m;
        var expiry = DateTime.UtcNow.AddDays(daysRemaining);
        tenant.SubscriptionExpiresAt = expiry;
        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            TenantId = tenant.Id,
            TransactionCode = "SUB-legacy",
            Type = "PURCHASE_PLAN",
            Amount = 326700m,
            Description = "Thanh toán gói dịch vụ Gói Thường (3 tháng)",
            PaymentMethod = "WALLET",
            Status = "SUCCESS",
            CreatedAt = expiry.AddMonths(-3)
        });
        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            TenantId = tenant.Id,
            TransactionCode = "STAFF-legacy",
            Type = "PURCHASE_ADDON",
            Amount = 33000m,
            Description = "Mua thêm Quản lý nhân viên",
            PaymentMethod = "WALLET",
            Status = "SUCCESS",
            CreatedAt = expiry.AddDays(-daysRemaining - 1)
        });
        _dbContext.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Gói Pro",
            PriceMonthly = 249000m,
            PriceYearly = 2988000m,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();

        var request = new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: months,
            ExtraBranches: 0,
            HasStaffManagementAddon: false,
            PaymentMethod: "Wallet",
            RequestId: Guid.NewGuid());

        Assert.IsType<ConflictObjectResult>(await _controller.PurchaseSubscription(request, CancellationToken.None));
        var quoteResult = Assert.IsType<OkObjectResult>(await _controller.QuoteProUpgrade(request, CancellationToken.None));
        var quote = Assert.IsType<ApiResponse<ProUpgradeQuote>>(quoteResult.Value).Data!;
        Assert.True(quote.Credit > 326700m * daysRemaining / (expiry - expiry.AddMonths(-3)).Days);
        Assert.Equal(expiry.AddMonths(months), quote.ExpiresAt);
        request = request with { QuoteAt = quote.QuoteAt, ExpectedExpiry = expiry, ExpectedTotal = quote.Total };
        Assert.IsType<ConflictObjectResult>(await _controller.PurchaseSubscription(
            request with { ExpectedTotal = quote.Total - 1 }, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.PurchaseSubscription(request, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.PurchaseSubscription(request, CancellationToken.None));
        var transaction = Assert.Single(_dbContext.WalletTransactions.Where(w => w.Type == "UPGRADE_PLAN"));
        Assert.Equal("UPGRADE_PLAN", transaction.Type);
        Assert.Equal("SUCCESS", transaction.Status);
        Assert.Equal(10000000m - quote.Total, tenant.WalletBalance);
        Assert.Equal(quote.ExpiresAt, tenant.SubscriptionExpiresAt);
    }

    [Fact]
    public async Task PurchaseSubscription_ExpiredStandard_ChargesFullProTerm()
    {
        var tenant = (await _dbContext.Tenants.FindAsync("tenant-01"))!;
        tenant.CurrentPlan = "STANDARD";
        tenant.SubscriptionStatus = "ACTIVE";
        tenant.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _dbContext.SaveChangesAsync();

        var started = DateTime.UtcNow;
        var result = Assert.IsType<OkObjectResult>(await _controller.PurchaseSubscription(
            new PurchaseSubscriptionRequest("PRO", 3), CancellationToken.None));
        var purchase = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(result.Value).Data!;
        Assert.Equal(821700m, purchase.TotalAmount);
        Assert.True(purchase.SubscriptionExpiresAt >= started.AddMonths(3));
        Assert.Equal("PURCHASE_PLAN", Assert.Single(_dbContext.WalletTransactions).Type);
    }

    [Fact]
    public async Task PurchaseSubscription_SamePlanRenewal_DoesNotRecordUpgrade()
    {
        var tenant = await _dbContext.Tenants.FindAsync("tenant-01");
        Assert.NotNull(tenant);
        tenant!.CurrentPlan = "PRO";
        tenant.SubscriptionStatus = "ACTIVE";
        tenant.WalletBalance = 10000000m;
        _dbContext.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Gói Pro",
            PriceMonthly = 249000m,
            PriceYearly = 2988000m,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();

        var request = new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: 1,
            ExtraBranches: 0,
            HasStaffManagementAddon: false,
            PaymentMethod: "Wallet");

        Assert.IsType<OkObjectResult>(await _controller.PurchaseSubscription(request, CancellationToken.None));
        Assert.Equal("PURCHASE_PLAN", Assert.Single(_dbContext.WalletTransactions).Type);
    }
}
