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
        // 1. Lấy mọi assignment (kể cả inactive) để trả trạng thái đúng theo OpenSpec §4.3 / §7
        var branchAssignments = await _context.SupplierBranchAssignments
            .Where(sba => sba.BranchId == branchId && !sba.IsDeleted)
            .ToDictionaryAsync(sba => sba.SupplierId, cancellationToken);

        var assignedSupplierIds = branchAssignments.Keys.ToList();
        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            assignedSupplierIds = assignedSupplierIds.Contains(supplierId.Value)
                ? new List<Guid> { supplierId.Value }
                : new List<Guid>();
        }

        if (assignedSupplierIds.Count == 0)
        {
            return Array.Empty<OrderMatrixProductDto>();
        }

        // 2. Sản phẩm được phép tại chi nhánh: BranchProducts (ưu tiên) ∪ BranchStocks
        var branchProductRows = await _context.BranchProducts
            .Where(bp => bp.BranchId == branchId && !bp.IsDeleted)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var branchProductLookup = branchProductRows
            .GroupBy(bp => bp.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        var stockProductIds = await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && !bs.IsDeleted)
            .Select(bs => bs.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allocatedProductIds = branchProductLookup.Keys
            .Union(stockProductIds)
            .Distinct()
            .ToList();

        if (allocatedProductIds.Count == 0)
        {
            return Array.Empty<OrderMatrixProductDto>();
        }

        // 3. SupplierProducts: giữ cả inactive để hiển thị dòng xám + draft cũ
        var spQuery = _context.SupplierProducts
            .Include(sp => sp.Product)
                .ThenInclude(p => p!.Category)
            .Include(sp => sp.Supplier)
            .Where(sp => assignedSupplierIds.Contains(sp.SupplierId)
                         && !sp.IsDeleted
                         && sp.Product != null
                         && !sp.Product.IsDeleted
                         && allocatedProductIds.Contains(sp.ProductId));

        var rawSupplierProducts = await spQuery.AsNoTracking().ToListAsync(cancellationToken);
        if (rawSupplierProducts.Count == 0)
        {
            return Array.Empty<OrderMatrixProductDto>();
        }

        // Khi filter 1 NCC: giữ mọi dòng. Khi ALL: mỗi ProductId giữ 1 NCC (primary / giá tốt)
        // nhưng vẫn giữ thêm các dòng inactive nếu cần hiển thị — ưu tiên primary.
        var supplierProducts = rawSupplierProducts;
        if (!supplierId.HasValue || supplierId.Value == Guid.Empty)
        {
            supplierProducts = rawSupplierProducts
                .GroupBy(sp => sp.ProductId)
                .SelectMany(g =>
                {
                    var primary = g.OrderByDescending(sp => sp.IsPrimarySupplier)
                        .ThenByDescending(sp => sp.Status == SupplierProductStatus.Active)
                        .ThenBy(sp => sp.AgreedWholesalePrice > 0 ? sp.AgreedWholesalePrice : decimal.MaxValue)
                        .First();
                    return new[] { primary };
                })
                .ToList();
        }

        var productIds = supplierProducts.Select(sp => sp.ProductId).Distinct().ToList();

        var stocks = await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && productIds.Contains(bs.ProductId) && !bs.IsDeleted)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var stockLookup = stocks
            .GroupBy(bs => bs.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityOnHand));

        var list = new List<OrderMatrixProductDto>();

        foreach (var sp in supplierProducts)
        {
            var p = sp.Product!;
            var resolvedSupplier = sp.Supplier;
            var resolvedSupplierId = sp.SupplierId;
            var supplierName = resolvedSupplier?.Name ?? "Nhà cung cấp";

            var isSupActive = resolvedSupplier == null || resolvedSupplier.Status == SupplierStatus.Active;
            var price = sp.AgreedWholesalePrice > 0 ? sp.AgreedWholesalePrice : (p.CostPrice ?? p.SellingPrice);
            var moq = sp.MinimumOrderQuantity > 0 ? sp.MinimumOrderQuantity : 1;

            var isProdActive = string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase);
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
            if (!branchAssignments.TryGetValue(resolvedSupplierId, out var asg))
            {
                isSupBranchActive = false;
                supBranchInactiveReason = $"NCC {supplierName} chưa được gán hoặc đã ngừng phục vụ chi nhánh này";
            }
            else if (asg.Status != SupplierStatus.Active)
            {
                isSupBranchActive = false;
                supBranchInactiveReason = asg.InactiveReason
                    ?? $"NCC {supplierName} đang tạm ngưng phân bổ tới chi nhánh này";
            }

            var isBranchProductActive = true;
            string? branchProductInactiveReason = null;
            if (branchProductLookup.TryGetValue(p.Id, out var bp))
            {
                isBranchProductActive = string.Equals(bp.Status, "Active", StringComparison.OrdinalIgnoreCase);
                if (!isBranchProductActive)
                {
                    branchProductInactiveReason = "Chi nhánh không kinh doanh sản phẩm này";
                }
            }

            var rowDisabledReason = prodInactiveReason
                ?? (!isSupActive ? (supInactiveReason ?? "NCC ngừng hoạt động") : null)
                ?? (!isSupBranchActive ? supBranchInactiveReason : null)
                ?? spInactiveReason
                ?? branchProductInactiveReason;

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
                OrderMultiple = 1,
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
                IsBranchProductActive = isBranchProductActive,
                BranchProductInactiveReason = branchProductInactiveReason,
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

        if (assignedSupplierIds.Count == 0)
        {
            return Array.Empty<OrderMatrixSupplierDeliveryConfigDto>();
        }

        var supQuery = _context.Suppliers
            .Include(s => s.DeliveryConfig)
                .ThenInclude(dc => dc!.BlackoutDates)
            .Include(s => s.BlackoutDates)
            .Include(s => s.BranchAssignments)
            .Where(s => !s.IsDeleted && assignedSupplierIds.Contains(s.Id))
            .AsNoTracking();

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            supQuery = supQuery.Where(s => s.Id == supplierId.Value);
        }

        var suppliers = await supQuery.ToListAsync(cancellationToken);
        var result = new List<OrderMatrixSupplierDeliveryConfigDto>();

        foreach (var s in suppliers)
        {
            var dc = s.DeliveryConfig;
            var asg = s.BranchAssignments.FirstOrDefault(b => b.BranchId == branchId && !b.IsDeleted);
            var isAssigned = asg != null && asg.Status == SupplierStatus.Active;
            var isSupActive = s.Status == SupplierStatus.Active && isAssigned;
            var inactiveReason = s.Status != SupplierStatus.Active
                ? $"Nhà cung cấp {s.Name} đang tạm ngừng hoạt động"
                : (asg == null
                    ? $"NCC {s.Name} chưa được gán tới chi nhánh này"
                    : (asg.Status != SupplierStatus.Active
                        ? asg.InactiveReason ?? "NCC tạm ngưng phân bổ tới chi nhánh này"
                        : null));

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

    public async Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == userId, cancellationToken);
    }

    public void AddPurchaseOrderDetail(PurchaseOrderDetail detail)
    {
        _context.PurchaseOrderDetails.Add(detail);
    }

    public async Task<PurchaseOrder?> FindCommittedPoAsync(
        Guid branchId,
        Guid supplierId,
        DateTime deliveryDate,
        CancellationToken cancellationToken = default)
    {
        var day = deliveryDate.Date;
        return await _context.PurchaseOrders
            .AsNoTracking()
            .Where(po => !po.IsDeleted
                         && po.BranchId == branchId
                         && po.SupplierId == supplierId
                         && po.Status != "DRAFT"
                         && po.Status != "CANCELLED"
                         && po.Status != "REJECTED"
                         && po.DeliveryDate.HasValue
                         && po.DeliveryDate.Value.Date == day)
            .OrderByDescending(po => po.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetCommittedPosInRangeAsync(
        Guid branchId,
        Guid? supplierId,
        DateTime viewFrom,
        int durationDays,
        CancellationToken cancellationToken = default)
    {
        var from = viewFrom.Date;
        var to = from.AddDays(Math.Clamp(durationDays, 1, 31));
        var query = _context.PurchaseOrders
            .AsNoTracking()
            .Where(po => !po.IsDeleted
                         && po.BranchId == branchId
                         && po.Status != "DRAFT"
                         && po.Status != "CANCELLED"
                         && po.Status != "REJECTED"
                         && po.DeliveryDate.HasValue
                         && po.DeliveryDate.Value.Date >= from
                         && po.DeliveryDate.Value.Date < to);

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            query = query.Where(po => po.SupplierId == supplierId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<SupplierProduct?> GetSupplierProductAsync(
        Guid supplierId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SupplierProducts
            .Include(sp => sp.Product)
            .FirstOrDefaultAsync(
                sp => sp.SupplierId == supplierId && sp.ProductId == productId && !sp.IsDeleted,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Branch>> GetActiveBranchesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Branches
            .AsNoTracking()
            .Where(b => b.IsActive && !b.IsDeleted)
            .OrderBy(b => b.BranchCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveBranchIdsForSupplierAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default)
    {
        var activeBranchIds = await _context.Branches
            .AsNoTracking()
            .Where(b => b.IsActive && !b.IsDeleted)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        if (activeBranchIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        return await _context.SupplierBranchAssignments
            .AsNoTracking()
            .Where(a =>
                a.SupplierId == supplierId
                && !a.IsDeleted
                && a.Status == SupplierStatus.Active
                && activeBranchIds.Contains(a.BranchId))
            .Select(a => a.BranchId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}


