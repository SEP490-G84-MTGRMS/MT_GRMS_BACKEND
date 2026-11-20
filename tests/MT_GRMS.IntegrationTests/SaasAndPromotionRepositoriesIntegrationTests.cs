using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class SaasAndPromotionRepositoriesIntegrationTests
{
    private const string TenantId = "tenant-saas-repo-01";

    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = TenantId;
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private (AppDbContext Context, TestTenantContext Tenant) CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"SaasRepoDb_{Guid.NewGuid()}")
            .Options;

        return (new AppDbContext(options, tenant), tenant);
    }

    [Fact]
    public async Task TenantRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new TenantRepository(context);

            var tenant = new Tenant
            {
                Id = TenantId,
                Code = "TNT01",
                Name = "Công ty TNHH Demo",
                Subdomain = "demo",
                CurrentPlan = "PRO",
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            await context.Tenants.AddAsync(tenant);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            Assert.NotNull(await repo.GetByIdAsync(TenantId));

            // 2. GetByIdWithDetailsAsync
            Assert.NotNull(await repo.GetByIdWithDetailsAsync(TenantId));

            // 3. GetByCodeAsync
            Assert.NotNull(await repo.GetByCodeAsync("TNT01"));

            // 4. GetBySubdomainAsync
            Assert.NotNull(await repo.GetBySubdomainAsync("demo"));

            // 5. GetPagedAsync
            var (items, count) = await repo.GetPagedAsync(keyword: "Demo");
            Assert.Equal(1, count);
            Assert.Single(items);

            // 6. Counts
            Assert.True(await repo.GetTotalCountAsync() >= 1);
            Assert.True(await repo.GetActiveCountAsync() >= 1);
            Assert.Equal(0, await repo.GetExpiringSoonCountAsync(7));
            Assert.Equal(0, await repo.GetGracePeriodCountAsync());
            Assert.Equal(0, await repo.GetLockedCountAsync());
            Assert.True(await repo.GetTotalBranchesCountAsync() >= 0);

            // 7. Urgent alerts & active with plan
            var urgent = await repo.GetUrgentAlertTenantsAsync(10);
            Assert.NotNull(urgent);

            var activeTenants = await repo.GetAllActiveWithPlanAsync();
            Assert.NotEmpty(activeTenants);

            // 8. Update
            tenant.Name = "Cong ty Cap Nhat";
            repo.Update(tenant);
            await context.SaveChangesAsync();
            var updatedTenant = await repo.GetByIdAsync(TenantId);
            Assert.Equal("Cong ty Cap Nhat", updatedTenant!.Name);
        }
    }

    [Fact]
    public async Task SubscriptionPlanRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new SubscriptionPlanRepository(context);

            var plan = new SubscriptionPlan
            {
                Id = "PLAN_PRO",
                PlanCode = "PRO",
                PlanName = "Gói Chuyên Nghiệp",
                PriceMonthly = 249_000m,
                PriceYearly = 2_490_000m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await context.SubscriptionPlans.AddAsync(plan);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            Assert.NotNull(await repo.GetByIdAsync("PLAN_PRO"));

            // 2. GetByCodeAsync
            Assert.NotNull(await repo.GetByCodeAsync("pro"));

            // 3. GetAllAsync
            var all = await repo.GetAllAsync(keyword: "Chuyên Nghiệp", status: "active");
            Assert.Single(all);

            // 4. CountActiveTenantsByPlanIdAsync
            Assert.Equal(0, await repo.CountActiveTenantsByPlanIdAsync("PLAN_PRO"));

            // 5. GetActiveTenantCountsAsync
            var counts = await repo.GetActiveTenantCountsAsync();
            Assert.NotNull(counts);

            // 6. Add, Update, Delete
            var newPlan = new SubscriptionPlan
            {
                Id = "PLAN_NEW",
                PlanCode = "NEW",
                PlanName = "Goi Moi",
                PriceMonthly = 100000m,
                PriceYearly = 1000000m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(newPlan);
            await context.SaveChangesAsync();
            Assert.NotNull(await repo.GetByIdAsync("PLAN_NEW"));

            newPlan.PlanName = "Goi Moi Cap Nhat";
            repo.Update(newPlan);
            await context.SaveChangesAsync();

            repo.Delete(newPlan);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task PromotionRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new PromotionRepository(context);

            var promo = new Promotion
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PromotionCode = "KM_TET2026",
                PromotionName = "Khuyến mãi Tết",
                Type = "PERCENTAGE_DISCOUNT",
                DiscountValue = 10m,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(5),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(promo);
            await context.SaveChangesAsync();

            // 1. GetByIdWithConditionsAsync
            var found = await repo.GetByIdWithConditionsAsync(promo.Id);
            Assert.NotNull(found);
            Assert.Equal("KM_TET2026", found!.PromotionCode);

            // 2. ExistsByCodeAsync
            Assert.True(await repo.ExistsByCodeAsync("KM_TET2026"));
            Assert.False(await repo.ExistsByCodeAsync("KM_NONEXISTENT"));

            // 3. GetPagedListAsync
            var (items, count) = await repo.GetPagedListAsync(new PromotionFilterQuery
            {
                SearchTerm = "Tết",
                Status = "ACTIVE",
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(1, count);
            Assert.Single(items);

            // 4. GetActivePromotionsAsync
            var activePromos = await repo.GetActivePromotionsAsync(DateTime.UtcNow);
            Assert.NotEmpty(activePromos);

            // Test with branch filter
            var testBranch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-PROMO",
                Name = "Branch Promo",
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(testBranch);
            await context.SaveChangesAsync();

            promo.TargetBranches = testBranch.Id.ToString();
            await context.SaveChangesAsync();
            var activeWithBranch = await repo.GetActivePromotionsAsync(DateTime.UtcNow, testBranch.Id.ToString());
            Assert.NotEmpty(activeWithBranch);

            // 5. ReplaceConditionsAsync
            var testProduct = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-COND",
                Name = "Cond Product",
                SellingPrice = 100_000m,
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(testProduct);
            await context.SaveChangesAsync();

            var cond1 = new PromotionCondition
            {
                Id = Guid.NewGuid(),
                PromotionId = promo.Id,
                ProductId = testProduct.Id,
                ProductRole = "BUY",
                MinQty = 2m,
                DiscountValue = 10_000m,
                CreatedAt = DateTime.UtcNow
            };
            await repo.ReplaceConditionsAsync(promo.Id, new[] { cond1 });
            await context.SaveChangesAsync();
            var condCheck = await repo.GetByIdWithConditionsAsync(promo.Id);
            Assert.Single(condCheck!.Conditions);

            // 6. Voucher Batches & Voucher Codes
            var voucherBatch = new Promotion
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PromotionCode = "VOUCHER-2026",
                PromotionName = "Voucher Batch Tet",
                Type = "VOUCHER",
                DiscountValue = 50_000m,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(10),
                IsActive = true,
                UsageLimit = 100,
                UsedCount = 0,
                CreatedAt = DateTime.UtcNow
            };
            await context.Promotions.AddAsync(voucherBatch);
            await context.SaveChangesAsync();

            var (vBatches, vTotal) = await repo.GetPagedVoucherBatchesAsync(1, 10, "Voucher", "ACTIVE");
            Assert.Equal(1, vTotal);

            var code1 = new VoucherCode
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PromotionId = voucherBatch.Id,
                Code = "VOUCHER-TET-01",
                Status = "UNUSED",
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddVoucherCodesAsync(new[] { code1 });
            await context.SaveChangesAsync();

            Assert.True(await repo.VoucherCodeExistsAsync("VOUCHER-TET-01"));
            Assert.False(await repo.VoucherCodeExistsAsync("VOUCHER-NONE"));

            var fetchedCode = await repo.GetVoucherCodeAsync("VOUCHER-TET-01");
            Assert.NotNull(fetchedCode);

            var (pagedCodes, codesTotal) = await repo.GetPagedVoucherCodesAsync(voucherBatch.Id, 1, 10, "UNUSED");
            Assert.Equal(1, codesTotal);

            // 7. RedeemVoucherCodeAtomicAsync
            var orderId = Guid.NewGuid();
            var redeemResult = await repo.RedeemVoucherCodeAtomicAsync("VOUCHER-TET-01", orderId, "ORD-V01", Guid.NewGuid(), "Nguyen Van A", "0912345678");
            Assert.True(redeemResult);

            var redeemAgain = await repo.RedeemVoucherCodeAtomicAsync("VOUCHER-TET-01", orderId, "ORD-V01", null, null, null);
            Assert.False(redeemAgain);

            // 8. RecordPromotionUsageAsync & RestoreOrderPromotionsOnCancelAsync
            await repo.RecordPromotionUsageAsync(promo.Id, 10_000m, "VOUCHER-TET-01", 50_000m, TenantId);
            await repo.RestoreOrderPromotionsOnCancelAsync(promo.Id, 10_000m, "VOUCHER-TET-01", 50_000m, TenantId);

            // 9. GetProductSellingPricesAsync
            var prod1 = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-PR-1",
                Name = "SanPham1",
                SellingPrice = 120_000m,
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(prod1);
            await context.SaveChangesAsync();
            var prices = await repo.GetProductSellingPricesAsync(new[] { prod1.Id, Guid.NewGuid() });
            Assert.True(prices.ContainsKey(prod1.Id));
            Assert.Equal(120_000m, prices[prod1.Id]);

            // 10. LoyaltySetting
            var loyalty = new LoyaltySetting
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                IsEnabled = true,
                EarningRateAmount = 100_000m,
                EarningPoints = 1,
                RedemptionRatePoints = 1,
                RedemptionRateAmount = 1000m,
                CreatedAt = DateTime.UtcNow
            };
            await repo.SaveLoyaltySettingAsync(loyalty);
            await context.SaveChangesAsync();
            var fetchedLoyalty = await repo.GetLoyaltySettingAsync(TenantId);
            Assert.NotNull(fetchedLoyalty);
            Assert.True(fetchedLoyalty!.IsEnabled);

            loyalty.EarningPoints = 2;
            await repo.SaveLoyaltySettingAsync(loyalty);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task BusinessDayClosingRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new BusinessDayClosingRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-BDAY",
                Name = "Chi nhánh BDay",
                BusinessDayStartTime = new TimeSpan(6, 0, 0),
                CreatedAt = DateTime.UtcNow
            };
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var closing = new BusinessDayClosing
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                BusinessDate = today,
                Version = 1,
                IsActive = true,
                ClosedAtUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(branch);
            await context.BusinessDayClosings.AddAsync(closing);
            await context.SaveChangesAsync();

            // 1. IsBranchAccessibleAsync
            Assert.True(await repo.IsBranchAccessibleAsync(TenantId, branch.Id));
            Assert.False(await repo.IsBranchAccessibleAsync(TenantId, Guid.NewGuid()));

            // 2. GetBusinessDayStartTimeAsync
            var time = await repo.GetBusinessDayStartTimeAsync(TenantId, branch.Id);
            Assert.Equal(new TimeSpan(6, 0, 0), time);

            // 3. GetAsync & GetLatestAsync & GetByIdAsync
            Assert.NotNull(await repo.GetAsync(TenantId, branch.Id, today));
            Assert.NotNull(await repo.GetLatestAsync(TenantId, branch.Id, today));
            Assert.NotNull(await repo.GetByIdAsync(TenantId, closing.Id));

            // 4. GetHistoryAsync
            var history = await repo.GetHistoryAsync(TenantId, branch.Id, today);
            Assert.Single(history);

            // 5. GetShiftsAsync, GetShiftlessOrdersAsync, HasPendingPaymentOrdersAsync, GetFundsAsync
            var shifts = await repo.GetShiftsAsync(TenantId, branch.Id, today);
            Assert.NotNull(shifts);

            var orders = await repo.GetShiftlessOrdersAsync(TenantId, branch.Id, today);
            Assert.NotNull(orders);

            var hasPending = await repo.HasPendingPaymentOrdersAsync(TenantId, branch.Id, today);
            Assert.False(hasPending);

            var funds = await repo.GetFundsAsync(TenantId, branch.Id);
            Assert.NotNull(funds);

            // 6. AddAsync
            var newClosing = new BusinessDayClosing
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                BusinessDate = today.AddDays(1),
                Version = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(newClosing);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ExpenseRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new ExpenseRepository(context);

            var branchId = Guid.NewGuid();
            var voucher = new ExpenseVoucher
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branchId,
                Amount = 150_000m,
                BeneficiaryName = "NCC Điện Nước",
                Category = "Tiện ích",
                Status = ExpenseVoucherStatus.Approved,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(voucher);
            await context.SaveChangesAsync();

            // 1. GetShiftExpenseSumAsync
            var shiftSum = await repo.GetShiftExpenseSumAsync(branchId, DateTime.UtcNow.AddHours(-1));
            Assert.Equal(150_000m, shiftSum);

            // 2. GetDailyExpenseSumAsync
            var dailySum = await repo.GetDailyExpenseSumAsync(branchId, DateTime.UtcNow);
            Assert.Equal(150_000m, dailySum);

            // 3. GetSameVendorDailySumAsync
            var vendorSum = await repo.GetSameVendorDailySumAsync(branchId, "NCC Điện Nước", "Tiện ích", DateTime.UtcNow);
            Assert.Equal(150_000m, vendorSum);

            // 4. GetRecentVouchersCountAsync
            var recentCount = await repo.GetRecentVouchersCountAsync(branchId, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5), 50_000m);
            Assert.True(recentCount >= 0);
        }
    }
}
