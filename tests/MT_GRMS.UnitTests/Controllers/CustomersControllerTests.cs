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
                TotalDebt = 0m
            },
            new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "KH02",
                Name = "Tran Thi B",
                Phone = "0909999999",
                MembershipTier = "Gold",
                TotalDebt = 500_000m
            }
        );
        await context.SaveChangesAsync();

        var controller = new CustomersController(context, _mockTenant.Object);

        // Search by name
        var res1 = await controller.GetCustomers("Nguyen", null, null, CancellationToken.None);
        var ok1 = Assert.IsType<OkObjectResult>(res1.Result);
        var apiRes1 = Assert.IsType<ApiResponse<List<CustomerResponse>>>(ok1.Value);
        Assert.Equal("KH01", Assert.Single(apiRes1.Data!).CustomerCode);

        // Filter by debtStatus = NO_DEBT
        var res2 = await controller.GetCustomers(null, null, "NO_DEBT", CancellationToken.None);
        var ok2 = Assert.IsType<OkObjectResult>(res2.Result);
        var apiRes2 = Assert.IsType<ApiResponse<List<CustomerResponse>>>(ok2.Value);
        Assert.Equal("KH01", Assert.Single(apiRes2.Data!).CustomerCode);

        var res3 = await controller.GetCustomers(null, "Gold", "DEBT", CancellationToken.None);
        var ok3 = Assert.IsType<OkObjectResult>(res3.Result);
        var apiRes3 = Assert.IsType<ApiResponse<List<CustomerResponse>>>(ok3.Value);
        Assert.Equal("KH02", Assert.Single(apiRes3.Data!).CustomerCode);

        // Case-insensitive tier filtering (e.g. lowercase "gold" matches "Gold")
        var res4 = await controller.GetCustomers(null, "gold", "DEBT", CancellationToken.None);
        var ok4 = Assert.IsType<OkObjectResult>(res4.Result);
        var apiRes4 = Assert.IsType<ApiResponse<List<CustomerResponse>>>(ok4.Value);
        Assert.Equal("KH02", Assert.Single(apiRes4.Data!).CustomerCode);
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

        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns("tenant-cust-02");
        var resOtherTenant = await controller.GetCustomerById(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resOtherTenant.Result);
    }

    [Fact]
    public async Task CreateCustomer_ShouldHandleValidation_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = new CustomersController(context, _mockTenant.Object);

        // HTML tag in name
        var reqHtml = new CreateCustomerRequest("0901234567", "<b>Customer</b>", null, null, "Standard");
        var resHtml = await controller.CreateCustomer(reqHtml, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resHtml.Result);

        // Empty phone
        var reqEmptyPhone = new CreateCustomerRequest("", "Nguyen Van C", null, null, "Standard");
        var resEmptyPhone = await controller.CreateCustomer(reqEmptyPhone, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmptyPhone.Result);

        var reqEmptyName = new CreateCustomerRequest("0901234567", "  ", null, null, "Standard");
        var resEmptyName = await controller.CreateCustomer(reqEmptyName, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmptyName.Result);

        // Success
        var reqSuccess = new CreateCustomerRequest(" 0901234567 ", " Nguyen Van C ", "c@example.com", "Hanoi", null);
        var resSuccess = await controller.CreateCustomer(reqSuccess, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resSuccess.Result);
        var apiRes = Assert.IsType<ApiResponse<CustomerResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("Nguyen Van C", apiRes.Data?.FullName);
        Assert.Equal("0901234567", apiRes.Data?.PhoneNumber);
        Assert.Equal("Standard", apiRes.Data?.MembershipTier);
        Assert.Equal(0m, apiRes.Data?.CurrentDebtAR);

        // Duplicate phone -> Conflict (409)
        var resDup = await controller.CreateCustomer(reqSuccess, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(resDup.Result);
    }

    [Fact]
    public async Task CreateCustomer_StaffCannotAssignNonStandardTier_AndOwnerCanonicalizesTier()
    {
        using var context = CreateDbContext();
        _mockTenant.SetupGet(t => t.CurrentRole).Returns("STAFF");
        var controller = new CustomersController(context, _mockTenant.Object);

        // Staff attempting to create VIP customer -> 403 Forbidden
        var staffVipReq = new CreateCustomerRequest("0903111222", "Khach VIP", null, null, "VIP");
        var staffVipRes = await controller.CreateCustomer(staffVipReq, CancellationToken.None);
        var forbidden = Assert.IsType<ObjectResult>(staffVipRes.Result);
        Assert.Equal(403, forbidden.StatusCode);

        // Staff creating Standard customer (case-insensitive) -> 200 OK with canonical "Standard"
        var staffStdReq = new CreateCustomerRequest("0903111223", "Khach Standard", null, null, "standard");
        var staffStdRes = await controller.CreateCustomer(staffStdReq, CancellationToken.None);
        var stdOk = Assert.IsType<OkObjectResult>(staffStdRes.Result);
        var stdApi = Assert.IsType<ApiResponse<CustomerResponse>>(stdOk.Value);
        Assert.Equal("Standard", stdApi.Data?.MembershipTier);

        // StoreOwner creating with lowercase "vip" -> 200 OK with canonical "VIP"
        _mockTenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var ownerVipReq = new CreateCustomerRequest("0903111224", "Khach Owner VIP", null, null, "vip");
        var ownerVipRes = await controller.CreateCustomer(ownerVipReq, CancellationToken.None);
        var ownerOk = Assert.IsType<OkObjectResult>(ownerVipRes.Result);
        var ownerApi = Assert.IsType<ApiResponse<CustomerResponse>>(ownerOk.Value);
        Assert.Equal("VIP", ownerApi.Data?.MembershipTier);
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
            TotalDebt = 1_500_000m
        });
        await context.SaveChangesAsync();

        var controller = new CustomersController(context, _mockTenant.Object);

        // 1. NotFound
        var dummyReq = new UpdateCustomerRequest(null, null, null, null);
        var resNotFound = await controller.UpdateCustomer(Guid.NewGuid(), dummyReq, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound.Result);

        // 2. HTML tag in FullName -> BadRequest
        var reqHtml = new UpdateCustomerRequest("Name <script>", null, null, null);
        var resHtml = await controller.UpdateCustomer(id, reqHtml, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resHtml.Result);

        var reqBlank = new UpdateCustomerRequest("  ", null, null, null);
        var resBlank = await controller.UpdateCustomer(id, reqBlank, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBlank.Result);

        var reqSuccess = new UpdateCustomerRequest(
            FullName: "New Clean Name",
            Email: " new@test.com ",
            Address: " <b>New Address</b> ",
            MembershipTier: "gold"
        );
        var resSuccess = await controller.UpdateCustomer(id, reqSuccess, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resSuccess.Result);
        var apiRes = Assert.IsType<ApiResponse<CustomerResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("New Clean Name", apiRes.Data?.FullName);
        Assert.Equal("new@test.com", apiRes.Data?.Email);
        Assert.Equal("New Address", apiRes.Data?.Address);
        Assert.Equal("Gold", apiRes.Data?.MembershipTier);
        Assert.Equal(1_500_000m, apiRes.Data?.CurrentDebtAR);
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
                Phone = "0901234567",
                TotalDebt = 300_000m,
                RewardPoints = 50
            },
            new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "C2",
                Name = "Cust 2",
                Phone = "0901234568",
                TotalDebt = 0m,
                RewardPoints = 100
            },
            new Customer
            {
                TenantId = "tenant-cust-02",
                CustomerCode = "C3",
                Name = "Other tenant",
                Phone = "0901234569",
                TotalDebt = 300_000m,
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
        Assert.Equal(1, apiRes.Data?.DebtorCount);
        Assert.Equal(150, apiRes.Data?.TotalLoyaltyPoints);
    }
}
