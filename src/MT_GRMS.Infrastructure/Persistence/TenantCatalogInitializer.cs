using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>Stages the global catalog snapshot inside the new tenant's registration transaction.</summary>
internal static class TenantCatalogInitializer
{
    public static async Task AddAsync(AppDbContext context, string tenantId, Guid branchId, CancellationToken cancellationToken)
    {
        var templates = await context.MasterProducts.AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted)
            .Include(x => x.Units.Where(u => !u.IsDeleted))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var categories = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in templates)
        {
            var categoryName = string.IsNullOrWhiteSpace(template.CategoryName) ? "Hàng hóa khác" : template.CategoryName.Trim();
            if (!categories.TryGetValue(categoryName, out var category))
            {
                category = new Category
                {
                    TenantId = tenantId,
                    CategoryCode = "CAT-" + Guid.NewGuid().ToString("N")[..24],
                    Name = categoryName
                };
                categories.Add(categoryName, category);
                context.Categories.Add(category);
            }

            var price = Math.Max(0, template.SuggestedRetailPrice ?? 0);
            var product = new Product
            {
                TenantId = tenantId,
                CategoryId = category.Id,
                ProductCode = "SP-" + template.Id.ToString("N"),
                Barcode = template.Barcode,
                // Older master records allow longer names/images than the tenant catalog columns.
                Name = template.Name[..Math.Min(template.Name.Length, 200)],
                BaseUnit = template.BaseUnit,
                SellingPrice = price,
                VatRate = template.VatRate,
                TaxCategory = template.TaxCategory,
                ImageUrl = template.ImageUrl?.Length > 500 ? null : template.ImageUrl,
                Description = template.Description,
                InternalNote = string.IsNullOrWhiteSpace(template.Brand) ? null : "Thương hiệu: " + template.Brand
            };
            var baseTemplate = template.Units.FirstOrDefault(x =>
                string.Equals(x.UnitName.Trim(), template.BaseUnit.Trim(), StringComparison.OrdinalIgnoreCase));
            product.Units.Add(new ProductUnit
            {
                TenantId = tenantId,
                ProductId = product.Id,
                UnitName = template.BaseUnit,
                Barcode = template.Barcode,
                ConversionRate = 1,
                Price = price,
                IsBaseUnit = true,
                IsPurchaseUnit = baseTemplate?.IsPurchaseUnit ?? true
            });

            foreach (var unit in template.Units.Where(x =>
                !string.IsNullOrWhiteSpace(x.UnitName) && x.ConversionRate > 0 &&
                !string.Equals(x.UnitName.Trim(), template.BaseUnit.Trim(), StringComparison.OrdinalIgnoreCase))
                .DistinctBy(x => x.UnitName.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                product.Units.Add(new ProductUnit
                {
                    TenantId = tenantId,
                    ProductId = product.Id,
                    UnitName = unit.UnitName.Trim(),
                    ConversionRate = unit.ConversionRate,
                    Barcode = unit.Barcode,
                    Price = Math.Round(price * unit.ConversionRate, 2),
                    IsPurchaseUnit = unit.IsPurchaseUnit
                });
            }

            context.Products.Add(product);
            context.BranchProducts.Add(new BranchProduct
            {
                TenantId = tenantId,
                BranchId = branchId,
                ProductId = product.Id
            });
        }
    }
}
