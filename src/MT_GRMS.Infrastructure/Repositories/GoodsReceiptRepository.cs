using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class GoodsReceiptRepository : IGoodsReceiptRepository
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GoodsReceiptRepository(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<GoodsReceiptDto>> GetPagedAsync(
        string tenantId,
        GoodsReceiptFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.GoodsReceipts
            .Include(gr => gr.Supplier)
            .Include(gr => gr.Branch)
            .Include(gr => gr.PurchaseOrder)
            .Include(gr => gr.Lines)
                .ThenInclude(l => l.Product!)
                    .ThenInclude(p => p.Units)
            .Include(gr => gr.Lines)
                .ThenInclude(l => l.PurchaseOrderDetail)
            .Where(gr => gr.TenantId == tenantId && !gr.IsDeleted)
            .AsNoTracking();

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(gr => gr.BranchId == query.BranchId.Value);
        }

        if (query.SupplierId.HasValue && query.SupplierId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(gr => gr.SupplierId == query.SupplierId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status != "ALL")
        {
            dbQuery = dbQuery.Where(gr => gr.Status.ToUpper() == query.Status.ToUpper());
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            dbQuery = dbQuery.Where(gr =>
                gr.Code.ToLower().Contains(kw) ||
                (gr.InvoiceNo != null && gr.InvoiceNo.ToLower().Contains(kw)) ||
                (gr.PurchaseOrder != null && gr.PurchaseOrder.Code.ToLower().Contains(kw)) ||
                (gr.Supplier != null && gr.Supplier.Name.ToLower().Contains(kw)));
        }

        if (query.FromDate.HasValue)
        {
            dbQuery = dbQuery.Where(gr => gr.ReceivedAtUtc >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            dbQuery = dbQuery.Where(gr => gr.ReceivedAtUtc <= query.ToDate.Value);
        }

        if (query.IsTodayDelivery == true)
        {
            var today = DateTime.UtcNow.Date;
            dbQuery = dbQuery.Where(gr => gr.ReceivedAtUtc.Date == today);
        }

        if (query.HasMismatch == true)
        {
            dbQuery = dbQuery.Where(gr =>
                gr.Lines.Any(l => l.AcceptedQty != l.OrderedQty || l.RejectedQty > 0));
        }

        var totalItems = await dbQuery.CountAsync(cancellationToken);

        var pagedReceipts = await dbQuery
            .OrderByDescending(gr => gr.ReceivedAtUtc)
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = pagedReceipts.Select(gr => new GoodsReceiptDto
        {
            Id = gr.Id,
            Code = gr.Code,
            PurchaseOrderId = gr.PurchaseOrderId,
            PurchaseOrderCode = gr.PurchaseOrder?.Code,
            SupplierId = gr.SupplierId,
            SupplierCode = gr.Supplier?.Code ?? string.Empty,
            SupplierName = gr.Supplier?.Name ?? string.Empty,
            BranchId = gr.BranchId,
            BranchCode = gr.Branch?.BranchCode ?? string.Empty,
            BranchName = gr.Branch?.Name ?? string.Empty,
            Status = gr.Status,
            ReceivedAtUtc = gr.ReceivedAtUtc,
            InvoiceNo = gr.InvoiceNo,
            ReceiverUserId = gr.ReceiverUserId,
            ConfirmedByUserId = gr.ConfirmedByUserId,
            ConfirmedAtUtc = gr.ConfirmedAtUtc,
            CancelledByUserId = gr.RejectedByUserId,
            CancelledAtUtc = gr.RejectedAtUtc,
            CancellationReason = gr.RejectionReason,
            SubTotal = gr.SubTotal,
            FreightFee = gr.FreightFee,
            DiscountAmount = gr.DiscountAmount,
            VatPercent = gr.VatPercent,
            VatAmount = gr.VatAmount,
            GrandTotal = gr.GrandTotal,
            PaymentMethod = gr.PaymentMethod,
            PaymentDueDate = gr.PaymentDueDate,
            TotalItems = gr.Lines.Count,
            TotalOrderedQty = gr.Lines.Sum(l => l.OrderedQty),
            TotalReceivedQty = gr.Lines.Sum(l => l.DeliveredQty),
            TotalAcceptedQty = gr.Lines.Sum(l => l.AcceptedQty),
            TotalRejectedQty = gr.Lines.Sum(l => l.RejectedQty),
            HasMismatch = gr.Lines.Any(l => l.AcceptedQty != l.OrderedQty || l.RejectedQty > 0),
            Note = gr.Note,
            Lines = gr.Lines.Select(l => new GoodsReceiptLineDto
            {
                Id = l.Id,
                ProductId = l.ProductId,
                ProductCode = l.Product?.ProductCode ?? string.Empty,
                ProductName = l.Product?.Name ?? string.Empty,
                Unit = ResolveInboundUnit(
                    l.PurchaseOrderDetail?.DeliveryUoM,
                    l.PurchaseOrderDetail?.PurchaseUnitNameSnapshot,
                    l.Product),
                HasBatches = l.Product?.HasBatches ?? false,
                PurchaseOrderDetailId = l.PurchaseOrderDetailId,
                OrderedQty = l.OrderedQty,
                DeliveredQty = l.DeliveredQty,
                AcceptedQty = l.AcceptedQty,
                RejectedQty = l.RejectedQty,
                UnitPrice = l.UnitPrice,
                LineAmount = l.LineAmount,
                BatchCode = l.BatchCode,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                FreightAllocated = l.FreightAllocated,
                DiscountAllocated = l.DiscountAllocated,
                VatAllocated = l.VatAllocated,
                LandedUnitCost = l.LandedUnitCost,
                QualityStatus = l.QualityStatus,
                RejectionReason = l.RejectionReason,
                Note = l.Note
            }).ToList()
        }).ToList();

        return new PagedResult<GoodsReceiptDto>
        {
            Items = dtos,
            TotalItems = totalItems,
            Page = query.PageIndex,
            PageSize = query.PageSize
        };
    }

    public async Task<GoodsReceipt?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.GoodsReceipts
            .Include(gr => gr.Supplier)
            .Include(gr => gr.Branch)
            .Include(gr => gr.PurchaseOrder)
            .Include(gr => gr.Lines)
                .ThenInclude(l => l.Product!)
                    .ThenInclude(p => p.Units)
            .Include(gr => gr.Lines)
                .ThenInclude(l => l.PurchaseOrderDetail)
            .FirstOrDefaultAsync(gr => gr.Id == id && gr.TenantId == tenantId && !gr.IsDeleted, cancellationToken);
    }

    public async Task<GoodsReceipt> AddAsync(GoodsReceipt goodsReceipt, CancellationToken cancellationToken = default)
    {
        await _context.GoodsReceipts.AddAsync(goodsReceipt, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return goodsReceipt;
    }

    public async Task UpdateAsync(GoodsReceipt goodsReceipt, CancellationToken cancellationToken = default)
    {
        _context.GoodsReceipts.Update(goodsReceipt);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GenerateCodeAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"GRN-{today}-";
        var countToday = await _context.GoodsReceipts
            .Where(gr => gr.TenantId == tenantId && gr.Code.StartsWith(prefix))
            .CountAsync(cancellationToken);

        return $"{prefix}{(countToday + 1):D3}";
    }

    public async Task<List<PurchaseOrder>> GetEligiblePurchaseOrdersAsync(
        string tenantId,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        // Điều kiện Mục 4: PO có trạng thái SENT hoặc CONFIRMED và chưa có GoodsReceipt khác ở trạng thái DRAFT hoặc COMPLETED
        var activePoIdsWithReceipt = await _context.GoodsReceipts
            .Where(gr => gr.TenantId == tenantId && !gr.IsDeleted &&
                         (gr.Status == "DRAFT" || gr.Status == "COMPLETED") &&
                         gr.PurchaseOrderId != null)
            .Select(gr => gr.PurchaseOrderId!.Value)
            .ToListAsync(cancellationToken);

        // PO đủ điều kiện nhận hàng: đã duyệt / đã gửi NCC / đã chốt sau cutoff.
        // PENDING_APPROVAL chưa đủ để nhập kho (chưa chốt với NCC).
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Branch)
            .Where(po => po.TenantId == tenantId && !po.IsDeleted &&
                         (po.Status == "SENT" || po.Status == "CONFIRMED" || po.Status == "DISPATCHED" || po.Status == "APPROVED" || po.Status == "FINALIZED" || po.Status == "PARTIALLY_RECEIVED") &&
                         !activePoIdsWithReceipt.Contains(po.Id))
            .AsNoTracking();

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(po => po.BranchId == branchId.Value);
        }

        return await query.OrderByDescending(po => po.OrderedAtUtc ?? po.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderWithDetailsAsync(
        string tenantId,
        Guid poId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Branch)
            .Include(po => po.Details)
                .ThenInclude(d => d.Product!)
                    .ThenInclude(p => p.Units)
            .FirstOrDefaultAsync(po => po.Id == poId && po.TenantId == tenantId && !po.IsDeleted, cancellationToken);
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderByCodeWithDetailsAsync(
        string tenantId,
        string poCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = poCode.Trim().ToLower();
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Branch)
            .Include(po => po.Details)
                .ThenInclude(d => d.Product!)
                    .ThenInclude(p => p.Units)
            .FirstOrDefaultAsync(po => po.Code.ToLower() == normalized && po.TenantId == tenantId && !po.IsDeleted, cancellationToken);
    }

    public async Task<GoodsReceipt?> GetActiveReceiptByPoIdAsync(
        string tenantId,
        Guid poId,
        CancellationToken cancellationToken = default)
    {
        // Ưu tiên DRAFT (chặn tạo mới); COMPLETED dùng cho PO mua thường (1 GRN).
        return await _context.GoodsReceipts
            .Where(gr =>
                gr.TenantId == tenantId &&
                gr.PurchaseOrderId == poId &&
                !gr.IsDeleted &&
                (gr.Status == "DRAFT" || gr.Status == "COMPLETED"))
            .OrderBy(gr => gr.Status == "DRAFT" ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<BranchStock?> GetBranchStockAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        string? batchCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchCode))
        {
            var localNonBatch = _context.BranchStocks.Local.FirstOrDefault(bs =>
                bs.TenantId == tenantId &&
                bs.BranchId == branchId &&
                bs.ProductId == productId &&
                !bs.IsDeleted);
            if (localNonBatch != null) return localNonBatch;

            return await _context.BranchStocks
                .Where(bs =>
                    bs.TenantId == tenantId &&
                    bs.BranchId == branchId &&
                    bs.ProductId == productId &&
                    !bs.IsDeleted)
                .OrderBy(bs => bs.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var normalizedBatch = batchCode.Trim();
        var localBatch = _context.BranchStocks.Local.FirstOrDefault(bs =>
            bs.TenantId == tenantId &&
            bs.BranchId == branchId &&
            bs.ProductId == productId &&
            string.Equals(bs.BatchCode?.Trim(), normalizedBatch, StringComparison.OrdinalIgnoreCase) &&
            !bs.IsDeleted);
        if (localBatch != null) return localBatch;

        return await _context.BranchStocks
            .FirstOrDefaultAsync(bs =>
                bs.TenantId == tenantId &&
                bs.BranchId == branchId &&
                bs.ProductId == productId &&
                bs.BatchCode == normalizedBatch &&
                !bs.IsDeleted, cancellationToken);
    }

    public async Task AddBranchStockAsync(BranchStock stock, CancellationToken cancellationToken = default)
    {
        await _context.BranchStocks.AddAsync(stock, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Product>> GetProductsByIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        if (productIds == null || productIds.Count == 0)
        {
            return new Dictionary<Guid, Product>();
        }

        return await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public async Task AddBatchAsync(Batch batch, CancellationToken cancellationToken = default)
    {
        await _context.Batches.AddAsync(batch, cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(string tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId && !p.IsDeleted, cancellationToken);
    }

    public async Task<decimal> GetTotalProductStockAsync(string tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.BranchStocks
            .Where(bs => bs.TenantId == tenantId && bs.ProductId == productId && !bs.IsDeleted)
            .SumAsync(bs => bs.QuantityOnHand, cancellationToken);
    }

    public async Task<Supplier?> GetSupplierByIdAsync(string tenantId, Guid supplierId, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == supplierId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);
    }

    public async Task AddSupplierLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default)
    {
        await _context.SupplierAPLedgers.AddAsync(ledger, cancellationToken);
    }

    public async Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == userId, cancellationToken);
    }

    private static string ResolveInboundUnit(string? deliveryUoM, string? purchaseUnitSnapshot, Product? product)
    {
        if (!string.IsNullOrWhiteSpace(deliveryUoM))
        {
            return deliveryUoM.Trim();
        }

        if (!string.IsNullOrWhiteSpace(purchaseUnitSnapshot))
        {
            return purchaseUnitSnapshot.Trim();
        }

        if (product?.Units != null)
        {
            var active = product.Units.Where(u => !u.IsDeleted).ToList();
            var purchase = active.FirstOrDefault(u => u.IsPurchaseUnit)
                           ?? active.FirstOrDefault(u => u.IsBaseUnit);
            if (purchase != null && !string.IsNullOrWhiteSpace(purchase.UnitName))
            {
                return purchase.UnitName.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(product?.BaseUnit) ? "Cái" : product!.BaseUnit.Trim();
    }
}
