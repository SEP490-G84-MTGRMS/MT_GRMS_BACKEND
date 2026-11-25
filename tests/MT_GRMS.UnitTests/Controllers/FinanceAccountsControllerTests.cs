using System.Security.Claims;
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

namespace MT_GRMS.UnitTests.Controllers;

public class FinanceAccountsControllerTests
{
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
