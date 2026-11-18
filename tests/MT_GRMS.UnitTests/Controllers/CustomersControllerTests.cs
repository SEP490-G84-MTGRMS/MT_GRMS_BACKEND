using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class CustomersControllerTests
{
    private readonly Mock<ITenantContext> _mockTenant;
    private const string TenantId = "tenant-cust-01";

    public CustomersControllerTests()
    {
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomersDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, _mockTenant.Object);
    }

    [Fact]
    public async Task GetCustomers_ShouldReturnOk_WithFiltering()
    {
        using var context = CreateDbContext();
        context.Customers.AddRange(
            new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "KH01",
                Name = "Nguyen Van A",
                Phone = "0901234567",
                MembershipTier = "Silver",
                TotalDebt = 0m,
                DebtLimit = 1_000_000m
            },
            new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "KH02",
                Name = "Tran Thi B",
                Phone = "0909999999",
                MembershipTier = "Gold",
                TotalDebt = 500_000m,
                DebtLimit = 1_000_000m
            }
        );
        await context.SaveChangesAsync();

        var controller = new CustomersController(context, _mockTenant.Object);

        // Search by name
        var res1 = await controller.GetCustomers("Nguyen", null, null, CancellationToken.None);
        var ok1 = Assert.IsType<OkObjectResult>(res1.Result);
        var apiRes1 = Assert.IsType<ApiResponse<List<CustomerResponse>>>(ok1.Value);
        Assert.Single(apiRes1.Data!);

        // Filter by debtStatus = NO_DEBT
        var res2 = await controller.GetCustomers(null, null, "NO_DEBT", CancellationToken.None);
        var ok2 = Assert.IsType<OkObjectResult>(res2.Result);
        var apiRes2 = Assert.IsType<ApiResponse<List<CustomerResponse>>>(ok2.Value);
        Assert.Single(apiRes2.Data!);
    }

    [Fact]
    public async Task GetCustomerById_ShouldHandleFound_AndNotFound()
    {
        using var context = CreateDbContext();
        var id = Guid.NewGuid();
        context.Customers.Add(new Customer
        {
            Id = id,
            TenantId = TenantId,
            CustomerCode = "KH01",
            Name = "Nguyen Van A",
            Phone = "0901234567"
        });
        await context.SaveChangesAsync();

        var controller = new CustomersController(context, _mockTenant.Object);

        var resFound = await controller.GetCustomerById(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resFound.Result);
        var apiRes = Assert.IsType<ApiResponse<CustomerResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);

        var resNotFound = await controller.GetCustomerById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound.Result);
    }

    [Fact]
    public async Task CreateCustomer_ShouldHandleValidation_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = new CustomersController(context, _mockTenant.Object);

        // HTML tag in name
        var reqHtml = new CreateCustomerRequest("0901234567", "<b>Hacker</b>", null, null, null, 0m, "Standard", false);
        var resHtml = await controller.CreateCustomer(reqHtml, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resHtml.Result);

        // Empty phone
        var reqEmptyPhone = new CreateCustomerRequest("", "Nguyen Van C", null, null, null, 0m, "Standard", false);
        var resEmptyPhone = await controller.CreateCustomer(reqEmptyPhone, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmptyPhone.Result);

        // Negative debt limit
        var reqNegative = new CreateCustomerRequest("0901234567", "Nguyen Van C", null, null, null, -100m, "Standard", false);
        var resNegative = await controller.CreateCustomer(reqNegative, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resNegative.Result);

        // Success
        var reqSuccess = new CreateCustomerRequest("0901234567", "Nguyen Van C", "c@example.com", "Hanoi", null, 2_000_000m, "Standard", true);
        var resSuccess = await controller.CreateCustomer(reqSuccess, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resSuccess.Result);
        var apiRes = Assert.IsType<ApiResponse<CustomerResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("Nguyen Van C", apiRes.Data?.FullName);

        // Duplicate phone -> Conflict (409)
        var resDup = await controller.CreateCustomer(reqSuccess, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(resDup.Result);
    }

    [Fact]
    public async Task UpdateCustomer_ShouldHandleNotFound_Validation_AndSuccess()
    {
        using var context = CreateDbContext();
        var id = Guid.NewGuid();
        context.Customers.Add(new Customer
        {
            Id = id,
            TenantId = TenantId,
            CustomerCode = "KH-UPD",
            Name = "Old Name",
            Phone = "0911222333",
            TotalDebt = 1_500_000m,
            DebtLimit = 1_000_000m
        });
        await context.SaveChangesAsync();

        var controller = new CustomersController(context, _mockTenant.Object);

        // 1. NotFound
        var dummyReq = new UpdateCustomerRequest(null, null, null, null, null, null, null);
        var resNotFound = await controller.UpdateCustomer(Guid.NewGuid(), dummyReq, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound.Result);

        // 2. HTML tag in FullName -> BadRequest
        var reqHtml = new UpdateCustomerRequest("Name <script>", null, null, null, null, null, null);
        var resHtml = await controller.UpdateCustomer(id, reqHtml, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resHtml.Result);

        // 3. Negative DebtLimit -> BadRequest
        var reqNeg = new UpdateCustomerRequest(null, null, null, null, -500m, null, null);
        var resNeg = await controller.UpdateCustomer(id, reqNeg, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resNeg.Result);

        // 4. Non-owner trying to change AllowDebt -> UnauthorizedAccessException
        _mockTenant.SetupGet(t => t.CurrentRole).Returns("STAFF");
        var reqAllowDebt = new UpdateCustomerRequest(null, null, null, null, null, null, null, AllowDebt: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.UpdateCustomer(id, reqAllowDebt, CancellationToken.None));

        // 5. Owner role updating DebtLimit > TotalDebt (restoring GoodStanding)
        _mockTenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var reqSuccess = new UpdateCustomerRequest(
            FullName: "New Clean Name",
            Email: "new@test.com",
            Address: "New Address",
            TaxCode: null,
            DebtLimit: 2_000_000m,
            MembershipTier: "VIP",
            Status: "Active",
            AllowDebt: true
        );
        var resSuccess = await controller.UpdateCustomer(id, reqSuccess, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resSuccess.Result);
        var apiRes = Assert.IsType<ApiResponse<CustomerResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("New Clean Name", apiRes.Data?.FullName);
        Assert.Equal(2_000_000m, apiRes.Data?.DebtLimit);

        // 6. Lowering DebtLimit below TotalDebt -> triggers LimitExceededBlocked
        var reqLowerLimit = new UpdateCustomerRequest(null, null, null, null, 500_000m, null, null);
        var resLower = await controller.UpdateCustomer(id, reqLowerLimit, CancellationToken.None);
        var okLower = Assert.IsType<OkObjectResult>(resLower.Result);
        var apiLower = Assert.IsType<ApiResponse<CustomerResponse>>(okLower.Value);
        Assert.True(apiLower.Data?.IsBlockedForDebt);
    }

    [Fact]
    public async Task GetDebtsSummary_ShouldReturnCalculatedKpis()
    {
        using var context = CreateDbContext();
        context.Customers.AddRange(
            new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "C1",
                Name = "Cust 1",
                TotalDebt = 300_000m,
                DebtLimit = 100_000m,
                RewardPoints = 50
            },
            new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "C2",
                Name = "Cust 2",
                TotalDebt = 0m,
                DebtLimit = 500_000m,
                RewardPoints = 100
            }
        );
        await context.SaveChangesAsync();

        var controller = new CustomersController(context, _mockTenant.Object);
        var res = await controller.GetDebtsSummary(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<CustomerKpiSummaryResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(2, apiRes.Data?.TotalCustomers);
        Assert.Equal(300_000m, apiRes.Data?.TotalDebtAR);
        Assert.Equal(1, apiRes.Data?.OverLimitCount);
        Assert.Equal(150, apiRes.Data?.TotalLoyaltyPoints);
    }
}
