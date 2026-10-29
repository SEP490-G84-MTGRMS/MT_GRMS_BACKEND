using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;

using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class SupplierRepository : GenericRepository<Supplier>, ISupplierRepository
{
    private readonly ITenantContext _tenantContext;

    public SupplierRepository(AppDbContext context, ITenantContext tenantContext) : base(context)
    {
        _tenantContext = tenantContext;
    }

    public async Task<Supplier?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .Include(s => s.Category)
            .Include(s => s.DeliveryConfig)
            .Include(s => s.BlackoutDates)
            .Include(s => s.BranchAssignments)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers.AnyAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<int> CountTodayAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        return await _context.Suppliers
            .IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == _tenantContext.CurrentTenantId && s.CreatedAt.Date == today, cancellationToken);
    }

    public async Task<(IReadOnlyList<Supplier> Items, int TotalCount)> GetPagedListAsync(
        string? searchTerm,
        SupplierType? supplierType,
        SupplierStatus? status,
        Guid? categoryId,
        int pageIndex,
        int pageSize,
        string? code = null,
        string? name = null,
        string? contactPerson = null,
        string? phone = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Suppliers
            .Include(s => s.Category)
            .Include(s => s.BranchAssignments)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(code))
        {
            var codeTerm = code.Trim().ToLower();
            query = query.Where(s => s.Code.ToLower().Contains(codeTerm));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var nameTerm = name.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(nameTerm));
        }

        if (!string.IsNullOrWhiteSpace(contactPerson))
        {
            var contactTerm = contactPerson.Trim().ToLower();
            query = query.Where(s => s.ContactPerson.ToLower().Contains(contactTerm));
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var phoneTerm = phone.Trim();
            query = query.Where(s => s.Phone.Contains(phoneTerm));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                s.Code.ToLower().Contains(term) ||
                s.Phone.Contains(term) ||
                (s.TaxCode != null && s.TaxCode.ToLower().Contains(term)) ||
                s.ContactPerson.ToLower().Contains(term));
        }

        if (supplierType.HasValue)
        {
            query = query.Where(s => s.SupplierType == supplierType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(s => s.CategoryId == categoryId.Value);
        }

        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        var isStoreOwner = string.IsNullOrEmpty(role) || role is "STORE_OWNER" or "ADMIN";
        if (!isStoreOwner && _tenantContext.CurrentBranchId is Guid branchId)
        {
            query = query.Where(s =>
                !s.BranchAssignments.Any() ||
                s.BranchAssignments.Any(a => a.BranchId == branchId));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> HasLedgerTransactionsAsync(Guid supplierId, APDocType docType, CancellationToken cancellationToken = default)
    {
        return await _context.SupplierAPLedgers
            .AnyAsync(l => l.SupplierId == supplierId && l.DocType == docType, cancellationToken);
    }

    public async Task<SupplierAPLedger?> GetLedgerByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _context.SupplierAPLedgers
            .FirstOrDefaultAsync(l => l.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task AddLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default)
    {
        await _context.SupplierAPLedgers.AddAsync(ledger, cancellationToken);
    }

    public async Task<(IReadOnlyList<SupplierAPLedger> Items, int TotalCount)> GetPagedLedgerAsync(
        Guid supplierId,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SupplierAPLedgers
            .Where(l => l.SupplierId == supplierId)
            .AsNoTracking();

        if (fromDate.HasValue)
        {
            query = query.Where(l => l.TransactionDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(l => l.TransactionDate <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(l => l.TransactionDate)
            .ThenByDescending(l => l.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<SupplierDeliveryConfig?> GetDeliveryConfigAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        return await _context.SupplierDeliveryConfigs
            .Include(c => c.BlackoutDates)
            .FirstOrDefaultAsync(c => c.SupplierId == supplierId, cancellationToken);
    }

    public async Task AddDeliveryConfigAsync(SupplierDeliveryConfig config, CancellationToken cancellationToken = default)
    {
        await _context.SupplierDeliveryConfigs.AddAsync(config, cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierBlackoutDate>> GetBlackoutDatesAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        return await _context.SupplierBlackoutDates
            .Where(b => b.SupplierId == supplierId)
            .OrderBy(b => b.StartDate)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceBlackoutDatesAsync(
        Guid supplierId,
        Guid configId,
        IEnumerable<SupplierBlackoutDate> blackoutDates,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.SupplierBlackoutDates
            .Where(b => b.SupplierId == supplierId)
            .ToListAsync(cancellationToken);

        _context.SupplierBlackoutDates.RemoveRange(existing);

        foreach (var bo in blackoutDates)
        {
            bo.SupplierDeliveryConfigId = configId;
            await _context.SupplierBlackoutDates.AddAsync(bo, cancellationToken);
        }
    }

    // Supplier Product Management (REQ-SP01 đến REQ-SP06)
    public async Task<SupplierProduct?> GetSupplierProductAsync(Guid supplierId, Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.SupplierProducts
            .Include(sp => sp.Supplier)
            .Include(sp => sp.Product)
                .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(sp => sp.SupplierId == supplierId && sp.ProductId == productId, cancellationToken);
    }

    public async Task<(IReadOnlyList<SupplierProduct> Items, int TotalCount)> GetPagedSupplierProductsAsync(
        Guid supplierId,
        string? searchTerm,
        Guid? categoryId,
        SupplierProductStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SupplierProducts
            .Include(sp => sp.Supplier)
            .Include(sp => sp.Product)
                .ThenInclude(p => p.Category)
            .AsNoTracking();

        if (supplierId != Guid.Empty)
        {
            query = query.Where(sp => sp.SupplierId == supplierId);
        }

        if (categoryId.HasValue && categoryId != Guid.Empty)
        {
            query = query.Where(sp => sp.Product.CategoryId == categoryId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(sp => sp.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(sp =>
                sp.Product.Name.ToLower().Contains(term) ||
                sp.Product.Barcode.ToLower().Contains(term) ||
                sp.Product.ProductCode.ToLower().Contains(term) ||
                (sp.SupplierProductCode != null && sp.SupplierProductCode.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(sp => sp.IsPrimarySupplier)
            .ThenByDescending(sp => sp.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddSupplierProductAsync(SupplierProduct supplierProduct, CancellationToken cancellationToken = default)
    {
        await _context.SupplierProducts.AddAsync(supplierProduct, cancellationToken);
    }

    public async Task ClearPrimarySupplierFlagAsync(Guid productId, Guid? excludeSupplierId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.SupplierProducts
            .Where(sp => sp.ProductId == productId && sp.IsPrimarySupplier);

        if (excludeSupplierId.HasValue && excludeSupplierId != Guid.Empty)
        {
            query = query.Where(sp => sp.SupplierId != excludeSupplierId.Value);
        }

        var primaries = await query.ToListAsync(cancellationToken);
        foreach (var sp in primaries)
        {
            sp.IsPrimarySupplier = false;
        }
    }

    public async Task<IReadOnlyList<SupplierProduct>> GetSuppliersForProductAsync(Guid productId, bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.SupplierProducts
            .Include(sp => sp.Supplier)
            .Where(sp => sp.ProductId == productId);

        if (onlyActive)
        {
            query = query.Where(sp => sp.Status == SupplierProductStatus.Active && sp.Supplier.Status == SupplierStatus.Active);
        }

        return await query
            .OrderByDescending(sp => sp.IsPrimarySupplier)
            .ThenBy(sp => sp.AgreedWholesalePrice)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products.AnyAsync(p => p.Id == productId, cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
    }
}

