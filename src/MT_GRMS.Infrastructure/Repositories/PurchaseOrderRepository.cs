using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PurchaseOrderRepository(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Branches
            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
    }

    public async Task<Domain.Entities.Supplier.Supplier?> GetSupplierByIdAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .Include(s => s.DeliveryConfig)
            .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderMatrixProductDto>> GetMatrixProductsAsync(
        Guid branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default)
    {
        var spQuery = _context.SupplierProducts
            .Include(sp => sp.Product)
                .ThenInclude(p => p.Category)
            .Include(sp => sp.Supplier)
            .Where(sp => sp.Status == SupplierProductStatus.Active && !sp.Product.IsDeleted)
            .AsNoTracking();

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            spQuery = spQuery.Where(sp => sp.SupplierId == supplierId.Value);
        }

        var supplierProducts = await spQuery.ToListAsync(cancellationToken);

        // Lấy tồn kho thực tế SOH tại chi nhánh
        var branchStocks = await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && !bs.IsDeleted)
            .GroupBy(bs => bs.ProductId)
            .Select(g => new { ProductId = g.Key, Soh = g.Sum(x => x.QuantityOnHand) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Soh, cancellationToken);

        var list = new List<OrderMatrixProductDto>();

        foreach (var sp in supplierProducts)
        {
            var p = sp.Product;
            var soh = branchStocks.TryGetValue(p.Id, out var qty) ? qty : 0m;
            var price = sp.AgreedWholesalePrice > 0 ? sp.AgreedWholesalePrice : (p.CostPrice ?? p.SellingPrice);
            var moq = sp.MinimumOrderQuantity > 0 ? sp.MinimumOrderQuantity : 10;

            // Tính toán mức bán hàng giả định & mức tồn an toàn dựa trên định mức sản phẩm
            var safetyStock = p.MinStock > 0 ? p.MinStock : 10m;
            var salesPerDay = Math.Max(2m, Math.Round(safetyStock / 2m));

            list.Add(new OrderMatrixProductDto
            {
                Id = p.Id,
                SupplierId = sp.SupplierId,
                SupplierName = sp.Supplier?.Name ?? "Nhà cung cấp",
                Category = p.Category?.Name ?? "Khác",
                Name = p.Name,
                Sku = p.ProductCode,
                Barcode = p.Barcode,
                Unit = sp.DeliveryUoM ?? p.BaseUnit ?? "Gói",
                Price = price,
                Moq = moq,
                Soh = soh,
                SalesPerDay = salesPerDay,
                SafetyStock = safetyStock
            });
        }

        return list;
    }

    public async Task<PurchaseOrder?> GetDraftMatrixPoAsync(
        Guid branchId,
        Guid? supplierId,
        DateTime viewFrom,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Details)
                .ThenInclude(d => d.Product)
            .Where(po => po.BranchId == branchId && po.Status == "DRAFT");

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            query = query.Where(po => po.SupplierId == supplierId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetPagedPurchaseOrdersAsync(
        Guid? branchId,
        Guid? supplierId,
        string? status,
        DateTime? dateFrom,
        DateTime? dateTo,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Branch)
            .Include(po => po.Supplier)
            .Include(po => po.Details)
                .ThenInclude(d => d.Product)
            .Where(po => !po.IsDeleted && po.Status != "DRAFT")
            .AsNoTracking();

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(po => po.BranchId == branchId.Value);
        }

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            query = query.Where(po => po.SupplierId == supplierId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var s = status.Trim().ToUpper();
            query = query.Where(po => po.Status == s);
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(po => po.DeliveryDate >= dateFrom.Value.Date);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(po => po.DeliveryDate <= dateTo.Value.Date.AddDays(1).AddTicks(-1));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(po => po.CreatedAt)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.PurchaseOrders
            .Include(po => po.Branch)
            .Include(po => po.Supplier)
            .Include(po => po.Details)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(po => po.Id == id, cancellationToken);
    }

    public async Task AddPurchaseOrderAsync(
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken = default)
    {
        await _context.PurchaseOrders.AddAsync(purchaseOrder, cancellationToken);
    }

    public async Task<string> GeneratePoCodeAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default)
    {
        var datePrefix = deliveryDate.ToString("yyyyMMdd");
        var basePattern = $"PO-{datePrefix}-";

        var count = await _context.PurchaseOrders
            .IgnoreQueryFilters()
            .CountAsync(po => po.TenantId == _tenantContext.CurrentTenantId && po.Code.StartsWith(basePattern), cancellationToken);

        var nextSeq = count + 1;
        return $"PO-{datePrefix}-{nextSeq:D4}";
    }
}
