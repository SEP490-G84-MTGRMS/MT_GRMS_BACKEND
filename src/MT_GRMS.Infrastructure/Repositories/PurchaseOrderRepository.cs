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
        // 1. Xác định các Nhà cung cấp được gán cho Cửa hàng (Chi nhánh) này qua SupplierBranchAssignments
        var assignedSupplierIds = await _context.SupplierBranchAssignments
            .Where(sba => sba.BranchId == branchId && !sba.IsDeleted)
            .Select(sba => sba.SupplierId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Fallback: Nếu có tồn kho lô cũ
        var branchStockSupplierIds = await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && !bs.IsDeleted && bs.SupplierId.HasValue)
            .Select(bs => bs.SupplierId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var validSupplierIds = assignedSupplierIds.Union(branchStockSupplierIds).Distinct().ToList();

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            if (!validSupplierIds.Contains(supplierId.Value))
            {
                return Array.Empty<OrderMatrixProductDto>();
            }
            validSupplierIds = new List<Guid> { supplierId.Value };
        }

        if (validSupplierIds.Count == 0)
        {
            return Array.Empty<OrderMatrixProductDto>();
        }

        // 2. Lấy các sản phẩm đã được add cho các Nhà cung cấp hợp lệ của cửa hàng (từ SupplierProducts)
        var spQuery = _context.SupplierProducts
            .Include(sp => sp.Product)
                .ThenInclude(p => p!.Category)
            .Include(sp => sp.Supplier)
            .Where(sp => validSupplierIds.Contains(sp.SupplierId)
                         && !sp.IsDeleted
                         && sp.Product != null
                         && !sp.Product.IsDeleted);

        var supplierProducts = await spQuery.AsNoTracking().ToListAsync(cancellationToken);
        if (supplierProducts.Count == 0)
        {
            return Array.Empty<OrderMatrixProductDto>();
        }

        var productIds = supplierProducts.Select(sp => sp.ProductId).Distinct().ToList();

        // 3. Lấy thông tin tồn kho BranchStocks theo sản phẩm tại chi nhánh (nếu có)
        var stocks = await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && productIds.Contains(bs.ProductId) && !bs.IsDeleted)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var stockLookup = stocks
            .GroupBy(bs => bs.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityOnHand));

        var branchAssignments = await _context.SupplierBranchAssignments
            .Where(sba => sba.BranchId == branchId && !sba.IsDeleted)
            .ToDictionaryAsync(sba => sba.SupplierId, cancellationToken);

        var list = new List<OrderMatrixProductDto>();

        foreach (var sp in supplierProducts)
        {
            var p = sp.Product!;
            var resolvedSupplier = sp.Supplier;
            var resolvedSupplierId = sp.SupplierId;
            var supplierName = resolvedSupplier?.Name ?? "Nhà cung cấp";

            var isSupActive = resolvedSupplier == null || resolvedSupplier.Status == SupplierStatus.Active;
            var price = sp.AgreedWholesalePrice > 0 ? sp.AgreedWholesalePrice : (p.CostPrice ?? p.SellingPrice);
            var moq = sp.MinimumOrderQuantity > 0 ? (int)sp.MinimumOrderQuantity : 10;

            var isProdActive = p.Status == "Active";
            var prodInactiveReason = !isProdActive ? "Sản phẩm đang tạm ngưng kinh doanh" : null;
            var supInactiveReason = !isSupActive && resolvedSupplier != null
                ? $"Nhà cung cấp {resolvedSupplier.Name} đang tạm ngừng hoạt động"
                : null;

            var isSpActive = sp.Status == SupplierProductStatus.Active;
            var spInactiveReason = !isSpActive
                ? $"Sản phẩm tạm ngừng nhập từ NCC {supplierName}"
                : null;

            var isSupBranchActive = true;
            string? supBranchInactiveReason = null;
            if (branchAssignments.TryGetValue(resolvedSupplierId, out var asg) && asg.Status != SupplierStatus.Active)
            {
                isSupBranchActive = false;
                supBranchInactiveReason = asg.InactiveReason
                    ?? $"NCC {supplierName} đang tạm ngưng phân bổ tới chi nhánh này";
            }

            var rowDisabledReason = prodInactiveReason ?? supInactiveReason ?? supBranchInactiveReason ?? spInactiveReason;
            var safetyStock = p.MinStock > 0 ? p.MinStock : 10m;
            var salesPerDay = Math.Max(2m, Math.Round(safetyStock / 2m));

            stockLookup.TryGetValue(p.Id, out var soh);

            list.Add(new OrderMatrixProductDto
            {
                Id = p.Id,
                SupplierId = resolvedSupplierId,
                SupplierName = supplierName,
                Category = p.Category?.Name ?? "Khác",
                Name = p.Name,
                Sku = p.ProductCode,
                Barcode = p.Barcode,
                Unit = !string.IsNullOrWhiteSpace(sp.DeliveryUoM) ? sp.DeliveryUoM : (p.BaseUnit ?? "Gói"),
                Price = price,
                Moq = moq,
                Soh = soh,
                SalesPerDay = salesPerDay,
                SafetyStock = safetyStock,
                IsProductActive = isProdActive,
                ProductInactiveReason = prodInactiveReason,
                IsSupplierActive = isSupActive,
                SupplierInactiveReason = supInactiveReason,
                IsSupplierProductActive = isSpActive,
                SupplierProductInactiveReason = spInactiveReason,
                IsSupplierBranchActive = isSupBranchActive,
                SupplierBranchInactiveReason = supBranchInactiveReason,
                IsBranchProductActive = true,
                BranchProductInactiveReason = null,
                RowDisabledReason = rowDisabledReason
            });
        }

        return list.OrderBy(x => x.SupplierName).ThenBy(x => x.Sku).ToList();
    }

    public async Task<IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto>> GetMatrixSupplierConfigsAsync(
        Guid branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default)
    {
        var assignedSupplierIds = await _context.SupplierBranchAssignments
            .Where(sba => sba.BranchId == branchId && !sba.IsDeleted)
            .Select(sba => sba.SupplierId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var branchStockSupplierIds = await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && !bs.IsDeleted && bs.SupplierId != null)
            .Select(bs => bs.SupplierId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var branchSupplierIds = assignedSupplierIds.Union(branchStockSupplierIds).Distinct().ToList();

        var supQuery = _context.Suppliers
            .Include(s => s.DeliveryConfig)
                .ThenInclude(dc => dc!.BlackoutDates)
            .Include(s => s.BlackoutDates)
            .Include(s => s.BranchAssignments)
            .Where(s => !s.IsDeleted)
            .AsNoTracking();

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            supQuery = supQuery.Where(s => s.Id == supplierId.Value);
        }
        else if (branchSupplierIds.Count > 0)
        {
            supQuery = supQuery.Where(s => branchSupplierIds.Contains(s.Id));
        }
        else
        {
            return Array.Empty<OrderMatrixSupplierDeliveryConfigDto>();
        }

        var suppliers = await supQuery.ToListAsync(cancellationToken);
        var result = new List<OrderMatrixSupplierDeliveryConfigDto>();

        foreach (var s in suppliers)
        {
            var dc = s.DeliveryConfig;
            var asg = s.BranchAssignments.FirstOrDefault(b => b.BranchId == branchId && !b.IsDeleted);
            var hasStockAtBranch = branchSupplierIds.Contains(s.Id);
            var isAssigned = asg != null || hasStockAtBranch;
            var isSupActive = s.Status == SupplierStatus.Active && (asg == null || asg.Status == SupplierStatus.Active);
            var inactiveReason = s.Status != SupplierStatus.Active
                ? $"Nhà cung cấp {s.Name} đang tạm ngừng hoạt động"
                : (asg != null && asg.Status != SupplierStatus.Active ? asg.InactiveReason ?? "NCC tạm ngưng phân bổ tới chi nhánh này" : null);

            var activeDays = ParseDeliveryDays(dc?.DeliveryDaysJson, dc?.DeliveryWeekdaysMask ?? 127);

            var blackoutDates = new HashSet<string>();
            if (s.BlackoutDates != null)
            {
                foreach (var bo in s.BlackoutDates)
                {
                    for (var dt = bo.StartDate.Date; dt <= bo.EndDate.Date; dt = dt.AddDays(1))
                    {
                        blackoutDates.Add(dt.ToString("yyyy-MM-dd"));
                    }
                }
            }
            if (dc?.BlackoutDates != null)
            {
                foreach (var bo in dc.BlackoutDates)
                {
                    for (var dt = bo.StartDate.Date; dt <= bo.EndDate.Date; dt = dt.AddDays(1))
                    {
                        blackoutDates.Add(dt.ToString("yyyy-MM-dd"));
                    }
                }
            }

            result.Add(new OrderMatrixSupplierDeliveryConfigDto
            {
                SupplierId = s.Id,
                SupplierCode = s.Code,
                SupplierName = s.Name,
                IsActive = isSupActive,
                IsAssignedToBranch = isAssigned,
                InactiveReason = inactiveReason,
                LeadTimeDays = dc?.LeadTimeDays > 0 ? dc.LeadTimeDays : 1,
                CutoffTime = !string.IsNullOrWhiteSpace(dc?.CutoffTime) ? dc.CutoffTime : "15:00",
                ActiveDeliveryDays = activeDays,
                BlackoutDates = blackoutDates.OrderBy(x => x).ToList()
            });
        }

        return result;
    }

    private static List<int> ParseDeliveryDays(string? json, int weekdaysMask)
    {
        var days = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var raw = json.Trim();
                if (raw.StartsWith("[") && raw.EndsWith("]"))
                {
                    var elements = raw.Trim('[', ']').Split(new[] { ',', '"', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var el in elements)
                    {
                        var lower = el.ToLowerInvariant();
                        if (lower is "mon" or "t2" or "thu2" or "thứ 2" or "1") days.Add(1);
                        else if (lower is "tue" or "t3" or "thu3" or "thứ 3" or "2") days.Add(2);
                        else if (lower is "wed" or "t4" or "thu4" or "thứ 4" or "3") days.Add(3);
                        else if (lower is "thu" or "t5" or "thu5" or "thứ 5" or "4") days.Add(4);
                        else if (lower is "fri" or "t6" or "thu6" or "thứ 6" or "5") days.Add(5);
                        else if (lower is "sat" or "t7" or "thu7" or "thứ 7" or "6") days.Add(6);
                        else if (lower is "sun" or "cn" or "chủ nhật" or "0") days.Add(0);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        if (!days.Any() && weekdaysMask > 0)
        {
            for (int i = 0; i < 7; i++)
            {
                if ((weekdaysMask & (1 << i)) != 0) days.Add(i);
            }
        }

        if (!days.Any())
        {
            return new List<int> { 1, 2, 3, 4, 5, 6 };
        }

        return days.OrderBy(d => d).ToList();
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
