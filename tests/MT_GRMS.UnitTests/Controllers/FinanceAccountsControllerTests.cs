using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.UnitTests.Controllers;

public class FinanceAccountsControllerTests
{
    [Fact]
    public async Task StaticVietQr_CanBeActivatedWithoutChangingPosAccountOrSepay()
    {
        var tenant = new TenantContext();
        var branchId = Guid.NewGuid();
        var existingPosAccountId = Guid.NewGuid();
        tenant.SetContext("qa-tenant", Guid.NewGuid(), branchId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, tenant);
        var branch = new Branch
        {
            Id = branchId,
            TenantId = tenant.CurrentTenantId,
            BranchCode = "QA",
            Name = "Chi nhánh QA",
            CurrentBankFinanceAccountId = existingPosAccountId
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var controller = new FinanceAccountsController(db, Mock.Of<IHttpClientFactory>(),
            new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build());
        var request = new SaveFinanceAccountRequest("Ngân hàng thu nợ", "Bank", branchId,
            "VCB", "Vietcombank", "0123456789", "CHU CUA HANG", true, false);

        var response = await controller.Create(request, CancellationToken.None);
        var created = Assert.IsType<ApiResponse<FinanceAccountDto>>(Assert.IsType<OkObjectResult>(response.Result).Value).Data!;
        Assert.False(created.IsActive);
        Assert.IsType<OkObjectResult>((await controller.SetStatus(created.Id, new(true), CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Update(created.Id, request, CancellationToken.None)).Result);

        var account = await db.FinanceAccounts.SingleAsync();
        Assert.True(account.IsActive);
        Assert.True(account.IsDefaultForVietQr);
        Assert.Equal("NotConfigured", account.SepayConnectionStatus);
        Assert.Equal(existingPosAccountId, branch.CurrentBankFinanceAccountId);
    }
}
