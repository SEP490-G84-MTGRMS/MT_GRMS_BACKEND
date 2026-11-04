using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class SupplierReturnRepository : GenericRepository<SupplierReturn>, ISupplierReturnRepository
{
    private readonly AppDbContext _appContext;

    public SupplierReturnRepository(AppDbContext context) : base(context)
    {
        _appContext = context;
    }

    public async Task<PagedResult<SupplierReturnListItemDto>> GetPagedAsync(
        string tenantId,
        SupplierReturnFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _appContext.SupplierReturns.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Branch)
            .Include(r => r.GoodsReceipt)
            .Include(r => r.Lines)
            .Where(r => r.TenantId == tenantId && !r.IsDeleted);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(r => r.BranchId == query.BranchId.Value);
        }

        if (query.SupplierId.HasValue && query.SupplierId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(r => r.SupplierId == query.SupplierId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            dbQuery = dbQuery.Where(r => r.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            dbQuery = dbQuery.Where(r =>
                r.Code.ToLower().Contains(kw) ||
                (r.GoodsReceipt != null && r.GoodsReceipt.Code.ToLower().Contains(kw)) ||
                (r.Supplier != null && r.Supplier.Name.ToLower().Contains(kw)));
        }

        var totalItems = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(r => r.CreatedAt)
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new SupplierReturnListItemDto(
                r.Id,
                r.Code,
                r.GoodsReceiptId,
                r.GoodsReceipt != null ? r.GoodsReceipt.Code : string.Empty,
                r.SupplierId,
                r.Supplier != null ? r.Supplier.Code : string.Empty,
                r.Supplier != null ? r.Supplier.Name : string.Empty,
                r.BranchId,
                r.Branch != null ? r.Branch.BranchCode : string.Empty,
                r.Branch != null ? r.Branch.Name : string.Empty,
                r.Status,
                r.Reason,
                r.Lines.Count,
                r.TotalRequestedQty,
                r.TotalApprovedQty,
                r.TotalHandedOverQty,
                r.TotalEstimatedAmount,
                r.CreatedAt,
                r.HandedOverAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierReturnListItemDto>
        {
            Items = items,
            TotalItems = totalItems,
            Page = query.PageIndex,
            PageSize = query.PageSize
        };
    }

    public async Task<SupplierReturn?> GetByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _appContext.SupplierReturns
            .Include(r => r.Supplier)
            .Include(r => r.Branch)
            .Include(r => r.GoodsReceipt)
            .Include(r => r.Lines)
                .ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id && !r.IsDeleted, cancellationToken);
    }

    public new async Task<SupplierReturn> AddAsync(
        SupplierReturn entity,
        CancellationToken cancellationToken = default)
    {
        await _appContext.SupplierReturns.AddAsync(entity, cancellationToken);
        return entity;
    }

    public Task UpdateAsync(
        SupplierReturn entity,
        CancellationToken cancellationToken = default)
    {
        _appContext.SupplierReturns.Update(entity);
        return Task.CompletedTask;
    }

    public async Task<string> GenerateCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var count = await _appContext.SupplierReturns
            .IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == tenantId && r.Code.StartsWith($"RTV-{today}"), cancellationToken);

        return $"RTV-{today}-{(count + 1):D3}";
    }

    public async Task<List<EligibleGrnLookupDto>> GetEligibleGrnsAsync(
        string tenantId,
        Guid? branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default)
    {
        var query = _appContext.GoodsReceipts.AsNoTracking()
            .Include(g => g.Supplier)
            .Include(g => g.Branch)
            .Include(g => g.Lines)
            .Where(g => g.TenantId == tenantId && !g.IsDeleted && g.Status == "COMPLETED");

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(g => g.BranchId == branchId.Value);
        }

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            query = query.Where(g => g.SupplierId == supplierId.Value);
        }

        return await query
            .OrderByDescending(g => g.ReceivedAtUtc)
            .Select(g => new EligibleGrnLookupDto(
                g.Id,
                g.Code,
                g.SupplierId,
                g.Supplier != null ? g.Supplier.Name : "NCC",
                g.BranchId,
                g.Branch != null ? g.Branch.Name : "Chi nhánh",
                g.ReceivedAtUtc,
                g.Lines.Count
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<GrnReturnableLineDto>> GetGrnReturnableLinesAsync(
        string tenantId,
        Guid grnId,
        CancellationToken cancellationToken = default)
    {
        var grn = await _appContext.GoodsReceipts.AsNoTracking()
            .Include(g => g.Lines)
                .ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == grnId && !g.IsDeleted, cancellationToken);

        if (grn == null) return new List<GrnReturnableLineDto>();

        // Query existing return lines referencing this GRN
        var existingReturns = await _appContext.SupplierReturnLines.AsNoTracking()
            .Include(l => l.SupplierReturn)
            .Where(l => l.TenantId == tenantId && l.SupplierReturn != null &&
                        l.SupplierReturn.GoodsReceiptId == grnId &&
                        l.SupplierReturn.Status != "CANCELLED")
            .ToListAsync(cancellationToken);

        var result = new List<GrnReturnableLineDto>();

        foreach (var line in grn.Lines.Where(l => l.AcceptedQty > 0))
        {
            var relatedReturns = existingReturns
                .Where(r => r.GoodsReceiptLineId == line.Id ||
                            (r.ProductId == line.ProductId && r.BatchCode == line.BatchCode))
                .ToList();

            var returnedQty = relatedReturns
                .Where(r => r.SupplierReturn?.Status == "COMPLETED")
                .Sum(r => r.HandedOverQty > 0 ? r.HandedOverQty : r.ApprovedQty);

            var pendingReturnQty = relatedReturns
                .Where(r => r.SupplierReturn?.Status == "PENDING_SUPPLIER" || r.SupplierReturn?.Status == "APPROVED")
                .Sum(r => r.RequestedQty);

            var availableReturnQty = Math.Max(0, line.AcceptedQty - returnedQty - pendingReturnQty);

            result.Add(new GrnReturnableLineDto(
                GoodsReceiptLineId: line.Id,
                ProductId: line.ProductId,
                Sku: line.Product?.ProductCode ?? string.Empty,
                ProductName: line.Product?.Name ?? "Sản phẩm",
                Unit: line.Product?.BaseUnit ?? "Cái",
                BatchCode: line.BatchCode,
                ExpiryDate: line.ExpiryDate,
                ReceivedQty: line.AcceptedQty,
                ReturnedQty: returnedQty,
                PendingReturnQty: pendingReturnQty,
                AvailableReturnQty: availableReturnQty,
                UnitCost: line.UnitPrice
            ));
        }

        return result;
    }

    public async Task<BranchStock?> GetBranchStockAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        string? batchCode,
        CancellationToken cancellationToken = default)
    {
        return await _appContext.BranchStocks
            .FirstOrDefaultAsync(s => s.TenantId == tenantId &&
                                      s.BranchId == branchId &&
                                      s.ProductId == productId &&
                                      (batchCode == null || s.BatchCode == batchCode),
                                 cancellationToken);
    }
}
