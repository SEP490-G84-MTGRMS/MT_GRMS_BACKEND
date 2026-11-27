using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
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
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
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
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.StoreOwner);
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

        return new FinanceAccountsController(db, httpClientFactory.Object, dataProtection, config, _mockTenant.Object)
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
            BankCode = "MB",
            AccountNumber = "0123456789",
            AccountHolderName = "NGUYEN VAN A",
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
        Assert.Equal("SEPAY_CONNECTION_REQUIRED", body.ErrorCode);
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
            BankCode = "MB",
            AccountNumber = "0123456789",
            AccountHolderName = "NGUYEN VAN A",
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
    public async Task SetStatus_DeactivatingTillWithOpenShift_Returns409()
    {
        using var db = CreateDbContext();
        var branchId = Guid.NewGuid();
        var till = new FinanceAccount { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, AccountName = "Till 1", AccountType = FinanceAccountType.Till, IsActive = true };
        db.FinanceAccounts.Add(till);
        db.ShiftZReports.Add(new ShiftZReport { Id = Guid.NewGuid(), TenantId = TenantId, BranchId = branchId, TillId = till.Id, Status = ShiftState.Open });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SetStatus(till.Id, new SetFinanceAccountStatusRequest(false), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<bool>>(conflict.Value);
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

    private static (AppDbContext Db, TenantContext Tenant, Guid BranchId) CreateSeededContext()
    {
        var tenant = new TenantContext();
        var branchId = Guid.NewGuid();
        tenant.SetContext("qa-tenant", Guid.NewGuid(), branchId);
        tenant.SetRole(SystemRoles.StoreOwner);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, tenant);
        db.Tenants.Add(new Tenant
        {
            Id = tenant.CurrentTenantId,
            Name = "QA Store",
            Code = "QAS",
            Subdomain = "qa",
            IsSepayEnabled = true
        });
        db.Branches.Add(new Branch
        {
            Id = branchId,
            TenantId = tenant.CurrentTenantId,
            BranchCode = "QA",
            Name = "Chi nhánh QA"
        });
        db.SaveChanges();
        return (db, tenant, branchId);
    }

    [Fact]
    public async Task CreateBank_ForcesInactiveAndBlocksActivationWithoutSepay_RuleVerify02()
    {
        var (db, tenant, branchId) = CreateSeededContext();
        await using var _ = db;
        var controller = new FinanceAccountsController(db, Mock.Of<IHttpClientFactory>(),
            new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build(), tenant);
        var request = new SaveFinanceAccountRequest("Ngân hàng thu nợ", "Bank", branchId,
            "VCB", "Vietcombank", "0123456789", "CHU CUA HANG", true, true);

        var createResponse = await controller.Create(request, CancellationToken.None);
        var created = Assert.IsType<ApiResponse<FinanceAccountDto>>(Assert.IsType<OkObjectResult>(createResponse.Result).Value).Data!;
        Assert.False(created.IsActive);

        var activateResult = await controller.SetStatus(created.Id, new(true), CancellationToken.None);
        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(activateResult.Result);
        var errorBody = Assert.IsType<ApiResponse<bool>>(unprocessable.Value);
        Assert.Equal("SEPAY_CONNECTION_REQUIRED", errorBody.ErrorCode);
        Assert.Equal(422, errorBody.StatusCode);

        var account = await db.FinanceAccounts.SingleAsync();
        Assert.False(account.IsActive);
        Assert.Equal("NotConfigured", account.SepayConnectionStatus);
    }

    [Fact]
    public async Task CreateTill_ClearsBankMetadataAndRejectsWhenActiveShiftOpen()
    {
        var (db, tenant, branchId) = CreateSeededContext();
        await using var _ = db;
        var controller = new FinanceAccountsController(db, Mock.Of<IHttpClientFactory>(),
            new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build(), tenant);

        var tillRequest = new SaveFinanceAccountRequest("Két quầy 1", "Till", branchId,
            "VCB", "Vietcombank", "999999", "OWNER", true, true);
        var createResp = await controller.Create(tillRequest, CancellationToken.None);
        var created = Assert.IsType<ApiResponse<FinanceAccountDto>>(Assert.IsType<OkObjectResult>(createResp.Result).Value).Data!;
        Assert.Null(created.BankCode);
        Assert.Null(created.BankName);
        Assert.Null(created.MaskedAccountNumber);
        Assert.Null(created.AccountHolderName);
        Assert.False(created.IsDefaultForVietQr);

        var savedTill = await db.FinanceAccounts.SingleAsync(x => x.Id == created.Id);
        savedTill.ActiveShiftId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var secondTillResp = await controller.Create(
            new SaveFinanceAccountRequest("Két quầy 2", "Till", branchId, null, null, null, null, false, true),
            CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(secondTillResp.Result);
        var conflictBody = Assert.IsType<ApiResponse<FinanceAccountDto>>(conflict.Value);
        Assert.Equal("FINANCE_ACCOUNT_SHIFT_OPEN", conflictBody.ErrorCode);
    }

    [Fact]
    public async Task GetVietQrDefault_AllowsStaffForAssignedBranch_AndForbidsOtherBranch()
    {
        var (db, tenant, branchId) = CreateSeededContext();
        await using var _ = db;
        var otherBranchId = Guid.NewGuid();
        var bankAccount = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchId,
            AccountType = FinanceAccountType.Bank,
            AccountName = "VCB Chi nhánh QA",
            BankCode = "VCB",
            BankName = "Vietcombank",
            AccountNumber = "0123456789",
            AccountHolderName = "CHU CUA HANG",
            IsActive = true,
            SepayConnectionStatus = "Connected"
        };
        db.FinanceAccounts.Add(bankAccount);
        var branch = await db.Branches.FirstAsync(b => b.Id == branchId);
        branch.CurrentBankFinanceAccountId = bankAccount.Id;
        await db.SaveChangesAsync();

        tenant.SetRole(SystemRoles.Staff);
        var controller = new FinanceAccountsController(db, Mock.Of<IHttpClientFactory>(),
            new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build(), tenant)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Role, SystemRoles.Staff),
                        new Claim("branchId", branchId.ToString())
                    }, "Test"))
                }
            }
        };

        var allowed = await controller.GetVietQrDefault(branchId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(allowed.Result);
        var dto = Assert.IsType<ApiResponse<VietQrAccountDto>>(ok.Value).Data!;
        Assert.Equal("VCB", dto.BankCode);

        var forbidden = await controller.GetVietQrDefault(otherBranchId, CancellationToken.None);
        Assert.IsType<ForbidResult>(forbidden.Result);
    }
}
