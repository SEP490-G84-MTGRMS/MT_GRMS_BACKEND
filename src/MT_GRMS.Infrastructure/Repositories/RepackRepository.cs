using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Repacks;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class RepackRepository : IRepackRepository
{
    private readonly AppDbContext _db;

    public RepackRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<BranchStock?> GetBranchStockAsync(string tenantId, Guid branchStockId, CancellationToken ct = default)
        => _db.BranchStocks
            .FirstOrDefaultAsync(x => x.Id == branchStockId && x.TenantId == tenantId && !x.IsDeleted, ct);

    public Task<Product?> GetProductWithUnitsAsync(string tenantId, Guid productId, CancellationToken ct = default)
        => _db.Products
            .Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId && !p.IsDeleted, ct);

    public async Task<decimal> GetActivePackageWeightAsync(string tenantId, Guid branchStockId, CancellationToken ct = default)
    {
        return await _db.Packages.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && !p.IsDeleted
                && p.BranchStockId == branchStockId
                && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"))
            .SumAsync(p => p.NetWeight, ct);
    }

    public async Task<IReadOnlyList<Package>> GetPackagesByBranchStockAsync(
        string tenantId,
        Guid branchStockId,
        CancellationToken ct = default)
    {
        return await _db.Packages.AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted && p.BranchStockId == branchStockId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<Package?> GetPackageByIdAsync(string tenantId, Guid packageId, CancellationToken ct = default)
        => _db.Packages.FirstOrDefaultAsync(p => p.Id == packageId && p.TenantId == tenantId && !p.IsDeleted, ct);

    public Task<int> CountPackagesCreatedTodayAsync(string tenantId, DateTime utcDate, CancellationToken ct = default)
    {
        var start = utcDate.Date;
        var end = start.AddDays(1);
        return _db.Packages.AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId && !p.IsDeleted && p.CreatedAt >= start && p.CreatedAt < end, ct);
    }

    public Task<int> CountRepackOrdersTodayAsync(string tenantId, DateTime utcDate, CancellationToken ct = default)
    {
        var start = utcDate.Date;
        var end = start.AddDays(1);
        return _db.RepackOrders.AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId && !p.IsDeleted && p.CreatedAt >= start && p.CreatedAt < end, ct);
    }

    public async Task AddRepackOrderAsync(RepackOrder order, CancellationToken ct = default)
        => await _db.RepackOrders.AddAsync(order, ct);

    public async Task AddPackagesAsync(IEnumerable<Package> packages, CancellationToken ct = default)
        => await _db.Packages.AddRangeAsync(packages, ct);

    public async Task<bool> IsCodeConflictAsync(
        string tenantId,
        string? internalCode,
        string? barcode,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(internalCode))
        {
            var code = internalCode.Trim();
            var existsPkg = await _db.Packages.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId && !p.IsDeleted && p.InternalCode == code, ct);
            if (existsPkg) return true;

            var existsProduct = await _db.Products.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId && !p.IsDeleted
                    && (p.Barcode == code || p.ProductCode == code), ct);
            if (existsProduct) return true;

            var existsUnit = await _db.ProductUnits.AsNoTracking()
                .AnyAsync(u => u.TenantId == tenantId && !u.IsDeleted && u.Barcode == code, ct);
            if (existsUnit) return true;
        }

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var code = barcode.Trim();
            var existsPkg = await _db.Packages.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId && !p.IsDeleted && p.Barcode == code, ct);
            if (existsPkg) return true;

            var existsProduct = await _db.Products.AsNoTracking()
                .AnyAsync(p => p.TenantId == tenantId && !p.IsDeleted
                    && (p.Barcode == code || p.ProductCode == code), ct);
            if (existsProduct) return true;

            var existsUnit = await _db.ProductUnits.AsNoTracking()
                .AnyAsync(u => u.TenantId == tenantId && !u.IsDeleted && u.Barcode == code, ct);
            if (existsUnit) return true;
        }

        return false;
    }
}
