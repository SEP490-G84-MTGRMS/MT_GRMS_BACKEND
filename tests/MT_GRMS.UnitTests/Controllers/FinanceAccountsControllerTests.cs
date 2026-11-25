using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class FinanceAccountsControllerTests
{
    private const string TenantId = "tenant-fa-01";
    private readonly Mock<ITenantContext> _mockTenant;

    public FinanceAccountsControllerTests()
    {
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);
        _mockTenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"FinanceAccountsDb_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, _mockTenant.Object);
    }

    private FinanceAccountsController CreateController(AppDbContext db)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var dataProtection = DataProtectionProvider.Create("MT_GRMS.UnitTests");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        return new FinanceAccountsController(db, httpClientFactory.Object, dataProtection, config)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Theory]
    [InlineData("NotConfigured")]
    [InlineData("Error")]
    [InlineData("")]
    public async Task SetStatus_ActivatingBankAccountWithoutSepayConnected_Returns422(string sepayStatus)
    {
        using var db = CreateDbContext();
        var account = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            AccountName = "MB Unconnected",
            AccountType = FinanceAccountType.Bank,
            SepayConnectionStatus = sepayStatus,
            IsActive = false
        };
        db.FinanceAccounts.Add(account);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SetStatus(account.Id, new SetFinanceAccountStatusRequest(true), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<bool>>(unprocessable.Value);
        Assert.False(body.Success);
        Assert.Equal("SEPAY_NOT_CONNECTED", body.ErrorCode);
        Assert.False((await db.FinanceAccounts.FindAsync(account.Id))!.IsActive);
    }

    [Fact]
    public async Task SetStatus_ActivatingBankAccountWithSepayConnected_Returns200()
    {
        using var db = CreateDbContext();
        var account = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            AccountName = "MB Connected",
            AccountType = FinanceAccountType.Bank,
            SepayConnectionStatus = "Connected",
            IsActive = false
        };
        db.FinanceAccounts.Add(account);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SetStatus(account.Id, new SetFinanceAccountStatusRequest(true), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(body.Success);
        Assert.True((await db.FinanceAccounts.FindAsync(account.Id))!.IsActive);
    }

    [Fact]
    public async Task SetStatus_ActivatingAnotherSafeInSameBranch_DeactivatesExistingSafe()
    {
        using var db = CreateDbContext();
        var branchId = Guid.NewGuid();
        var safe1 = new FinanceAccount { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountName = "Safe 1", AccountType = FinanceAccountType.Safe, IsActive = true };
        var safe2 = new FinanceAccount { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountName = "Safe 2", AccountType = FinanceAccountType.Safe, IsActive = false };
        db.FinanceAccounts.AddRange(safe1, safe2);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SetStatus(safe2.Id, new SetFinanceAccountStatusRequest(true), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.False((await db.FinanceAccounts.FindAsync(safe1.Id))!.IsActive);
        Assert.True((await db.FinanceAccounts.FindAsync(safe2.Id))!.IsActive);
    }

    [Fact]
    public async Task SetStatus_DeactivatingTillWithOpenShift_Returns422()
    {
        using var db = CreateDbContext();
        var branchId = Guid.NewGuid();
        var till = new FinanceAccount { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountName = "Till 1", AccountType = FinanceAccountType.Till, IsActive = true };
        db.FinanceAccounts.Add(till);
        db.ShiftZReports.Add(new ShiftZReport { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, TillId = till.Id, Status = ShiftState.Open });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SetStatus(till.Id, new SetFinanceAccountStatusRequest(false), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<bool>>(unprocessable.Value);
        Assert.Equal("FINANCE_ACCOUNT_SHIFT_OPEN", body.ErrorCode);
    }

    [Fact]
    public async Task BusinessDayClosingRepository_GetPostedPosSaleOrderIdsAsync_IncludesPreOrderDepositAndPickup()
    {
        using var db = CreateDbContext();
        var branchId = Guid.NewGuid();
        var posOrderId = Guid.NewGuid();
        var preOrderId = Guid.NewGuid();
        var unpostedOrderId = Guid.NewGuid();

        db.CashbookEntries.AddRange(
            new CashbookEntry { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountType = FinanceAccountType.Safe, EntryType = CashbookEntryType.Receipt, SourceType = "POS_SALE", SourceId = posOrderId, Amount = 50_000m, EntryCode = "CB-1", Description = "POS" },
            new CashbookEntry { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountType = FinanceAccountType.Safe, EntryType = CashbookEntryType.Receipt, SourceType = "PREORDER_DEPOSIT", SourceId = preOrderId, Amount = 5_990m, EntryCode = "CB-2", Description = "Deposit" },
            new CashbookEntry { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountType = FinanceAccountType.Safe, EntryType = CashbookEntryType.Receipt, SourceType = "PREORDER_PICKUP", SourceId = preOrderId, Amount = 23_960m, EntryCode = "CB-3", Description = "Pickup" }
        );
        await db.SaveChangesAsync();

        var repo = new BusinessDayClosingRepository(db);
        var postedIds = await repo.GetPostedPosSaleOrderIdsAsync(TenantId, branchId, [posOrderId, preOrderId, unpostedOrderId]);

        Assert.Contains(posOrderId, postedIds);
        Assert.Contains(preOrderId, postedIds);
        Assert.DoesNotContain(unpostedOrderId, postedIds);
    }
}
