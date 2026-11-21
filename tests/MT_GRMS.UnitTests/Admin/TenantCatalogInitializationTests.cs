using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;

namespace MT_GRMS.UnitTests.Admin;

public class TenantCatalogInitializationTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Task<User> RegisterAsync(AppDbContext context, string tenantId)
    {
        var user = new User { Id = "user-" + tenantId, UserName = tenantId, Email = tenantId + "@example.com", PasswordHash = "hash" };
        var tenant = new Tenant { Id = tenantId, Name = tenantId, Subdomain = tenantId, Code = tenantId };
        var branch = new Branch { TenantId = tenantId, Name = "Main", BranchCode = "BR-01" };
        return new UserRepository(context).RegisterStoreOwnerAsync(user, tenant, branch);
    }

    [Fact]
    public async Task RegistrationCopiesOnlyActiveTemplatesAndCreatesBaseUnitsAndBranchAssignments()
    {
        await using var context = CreateContext();
        var template = new MasterProduct
        {
            Name = "Sữa tươi", Barcode = "8930000000001", CategoryName = "Đồ uống",
            BaseUnit = "Hộp", SuggestedRetailPrice = 10000, VatRate = 8, Brand = "Mẫu",
            Units = new List<MasterProductUnit> { new() { UnitName = "Thùng", ConversionRate = 24, IsPurchaseUnit = true } }
        };
        context.MasterProducts.AddRange(template,
            new MasterProduct { Name = "Inactive", Barcode = "8930000000002", IsActive = false },
            new MasterProduct { Name = "Deleted", Barcode = "8930000000003", IsDeleted = true });
        await context.SaveChangesAsync();

        await RegisterAsync(context, "new-store");

        var product = Assert.Single(await context.Products.IgnoreQueryFilters().Include(x => x.Units).ToListAsync());
        Assert.Equal("new-store", product.TenantId);
        Assert.Equal(10000m, product.SellingPrice);
        Assert.Equal("Đồ uống", (await context.Categories.IgnoreQueryFilters().SingleAsync()).Name);
        Assert.Equal(1m, Assert.Single(product.Units.Where(x => x.IsBaseUnit)).ConversionRate);
        Assert.Equal(240000m, Assert.Single(product.Units.Where(x => !x.IsBaseUnit)).Price);
        Assert.All(product.Units, x => Assert.Equal("new-store", x.TenantId));
        Assert.Equal(product.Id, (await context.BranchProducts.IgnoreQueryFilters().SingleAsync()).ProductId);
        Assert.Empty(await context.BranchStocks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task TenantsReceiveIndependentSnapshotsAndLaterRegistrationsUseUpdatedGlobalData()
    {
        await using var context = CreateContext();
        var template = new MasterProduct { Name = "Bản đầu", Barcode = "8930000000001", CategoryName = "Thực phẩm", BaseUnit = "Gói" };
        context.MasterProducts.Add(template);
        await context.SaveChangesAsync();
        await RegisterAsync(context, "store-one");
        template.Name = "Bản mới";
        await context.SaveChangesAsync();
        await RegisterAsync(context, "store-two");

        var products = await context.Products.IgnoreQueryFilters().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(2, products.Count);
        Assert.Equal("Bản đầu", products.Single(x => x.TenantId == "store-one").Name);
        Assert.Equal("Bản mới", products.Single(x => x.TenantId == "store-two").Name);
        Assert.NotEqual(products[0].Id, products[1].Id);
        Assert.NotEqual(products[0].CategoryId, products[1].CategoryId);
    }
}
