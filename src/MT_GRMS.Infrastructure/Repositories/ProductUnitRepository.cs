using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ProductUnitRepository : IProductUnitRepository
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ProductUnitRepository(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<ProductUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;

        return await _context.ProductUnits
            .Include(u => u.Product)
            .FirstOrDefaultAsync(u => u.Id == id &&
                                      !u.IsDeleted &&
                                      u.Product != null &&
                                      !u.Product.IsDeleted &&
                                      u.Product.TenantId == tenantId,
                                 cancellationToken);
    }

    public async Task<IReadOnlyList<ProductUnit>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;

        return await _context.ProductUnits
            .Where(u => u.ProductId == productId &&
                        !u.IsDeleted &&
                        u.Product != null &&
                        !u.Product.IsDeleted &&
                        u.Product.TenantId == tenantId)
            .OrderByDescending(u => u.IsBaseUnit)
            .ThenBy(u => u.ConversionRate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(Guid productId, string unitName, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalizedName = unitName.Trim().ToLower();

        return await _context.ProductUnits
            .AnyAsync(u => u.ProductId == productId &&
                           !u.IsDeleted &&
                           u.UnitName.ToLower() == normalizedName &&
                           (excludeId == null || u.Id != excludeId),
                      cancellationToken);
    }

    public async Task<bool> ExistsByBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return false;

        var cleanBarcode = barcode.Trim();
        var tenantId = _tenantContext.CurrentTenantId;

        // Kiểm tra trong bảng ProductUnits của cùng Tenant
        var existsInUnits = await _context.ProductUnits
            .AnyAsync(u => !u.IsDeleted &&
                           u.Barcode == cleanBarcode &&
                           u.Product != null &&
                           !u.Product.IsDeleted &&
                           u.Product.TenantId == tenantId &&
                           (excludeId == null || u.Id != excludeId),
                      cancellationToken);

        if (existsInUnits) return true;

        // Kiểm tra với Barcode của Products trong cùng Tenant
        var existsInProducts = await _context.Products
            .AnyAsync(p => !p.IsDeleted &&
                           p.TenantId == tenantId &&
                           p.Barcode == cleanBarcode,
                      cancellationToken);

        return existsInProducts;
    }

    public async Task<ProductUnit?> GetBaseUnitByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.ProductUnits
            .FirstOrDefaultAsync(u => u.ProductId == productId &&
                                      !u.IsDeleted &&
                                      u.IsBaseUnit,
                                 cancellationToken);
    }

    public async Task<int> CountByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.ProductUnits
            .CountAsync(u => u.ProductId == productId && !u.IsDeleted, cancellationToken);
    }

    public async Task AddAsync(ProductUnit entity, CancellationToken cancellationToken = default)
    {
        await _context.ProductUnits.AddAsync(entity, cancellationToken);
    }

    public void Update(ProductUnit entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.ProductUnits.Update(entity);
    }

    public void Delete(ProductUnit entity)
    {
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.ProductUnits.Update(entity);
    }
}

