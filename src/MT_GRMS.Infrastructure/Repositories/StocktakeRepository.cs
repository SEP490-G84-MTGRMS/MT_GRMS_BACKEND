using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class StocktakeRepository : GenericRepository<Stocktake>, IStocktakeRepository
{
    private readonly AppDbContext _appContext;

    public StocktakeRepository(AppDbContext context) : base(context)
    {
        _appContext = context;
    }

    public async Task<PagedResult<StocktakeListItemDto>> GetPagedAsync(
        string tenantId,
        StocktakeFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _appContext.Stocktakes.AsNoTracking()
            .Include(s => s.Branch)
            .Include(s => s.Lines)
            .Where(s => s.TenantId == tenantId && !s.IsDeleted);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(s => s.BranchId == query.BranchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            dbQuery = dbQuery.Where(s => s.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            dbQuery = dbQuery.Where(s =>
                s.Code.ToLower().Contains(kw) ||
                (s.CreatedByName != null && s.CreatedByName.ToLower().Contains(kw)) ||
                (s.Branch != null && s.Branch.Name.ToLower().Contains(kw)) ||
                (s.Note != null && s.Note.ToLower().Contains(kw)));
        }

        var totalItems = await dbQuery.CountAsync(cancellationToken);

        var entities = await dbQuery
            .OrderByDescending(s => s.CreatedAt)
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = entities.Select(s => new StocktakeListItemDto(
            Id: s.Id,
            Code: s.Code,
            BranchId: s.BranchId,
            BranchCode: s.Branch?.BranchCode ?? string.Empty,
            BranchName: s.Branch?.Name ?? "Chi nhánh",
            Scope: s.Scope,
            ReasonType: s.ReasonType,
            Status: s.Status,
            CreatedByName: s.CreatedByName,
            StartedAt: s.StartedAt,
            SubmittedAt: s.SubmittedAt,
            ReviewedByName: s.ReviewedByName,
            ReviewedAt: s.ReviewedAt,
            TotalLines: s.Lines.Count,
            AnomalyLines: s.Lines.Count(l => l.DefectiveQty > 0 || l.DamagedQty > 0 || l.NearExpiryQty > 0 || l.ExpiredQty > 0 || l.VarianceQty != 0),
            ReturnEligibleLines: s.Lines.Count(l => (l.DefectiveQty + (l.NearExpiryAction == "NEAR_EXPIRY_RETURN" ? l.NearExpiryQty : 0)) > l.ReturnedQty),
            Note: s.Note
        )).ToList();

        return new PagedResult<StocktakeListItemDto>
        {
            Items = items,
            TotalItems = totalItems,
            Page = query.PageIndex,
            PageSize = query.PageSize
        };
    }

    public async Task<Stocktake?> GetByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _appContext.Stocktakes
            .Include(s => s.Branch)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Product)
                    .ThenInclude(p => p!.Category)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id && !s.IsDeleted, cancellationToken);
    }

    public async Task<StocktakeDetailDto?> GetDetailDtoByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null) return null;

        var stockIds = entity.Lines.Where(l => l.BranchStockId.HasValue).Select(l => l.BranchStockId!.Value).Distinct().ToList();
        var currentStocks = stockIds.Count == 0
            ? new Dictionary<Guid, BranchStock>()
            : await _appContext.BranchStocks.AsNoTracking()
                .Where(bs => stockIds.Contains(bs.Id))
                .ToDictionaryAsync(bs => bs.Id, cancellationToken);

        var supplierIds = entity.Lines.Where(l => l.SupplierId.HasValue).Select(l => l.SupplierId!.Value).Distinct().ToList();
        var suppliers = supplierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _appContext.Suppliers.AsNoTracking()
                .Where(sup => supplierIds.Contains(sup.Id))
                .ToDictionaryAsync(sup => sup.Id, sup => sup.Name, cancellationToken);

        var lineDtos = entity.Lines.Select(l =>
        {
            BranchStock? currentStock = null;
            if (l.BranchStockId.HasValue)
            {
                currentStocks.TryGetValue(l.BranchStockId.Value, out currentStock);
            }

            string? supplierName = null;
            if (l.SupplierId.HasValue && suppliers.TryGetValue(l.SupplierId.Value, out var sName))
            {
                supplierName = sName;
            }

            var eligibleReturnQty = StocktakeService.GetLineEligibleReturnQty(l);

            return new StocktakeLineDto(
                Id: l.Id,
                ProductId: l.ProductId,
                Sku: l.Product?.ProductCode ?? string.Empty,
                ProductName: l.Product?.Name ?? "Sản phẩm",
                CategoryName: l.Product?.Category?.Name ?? "Chung",
                Unit: l.Product?.BaseUnit ?? "Cái",
                HasBatches: l.Product?.HasBatches ?? !string.IsNullOrWhiteSpace(l.BatchCode),
                BranchStockId: l.BranchStockId,
                BatchCode: l.BatchCode,
                ManufactureDate: l.ManufactureDate,
                ExpiryDate: l.ExpiryDate,
                UnitCost: l.UnitCost,
                SystemQty: l.SystemQty,
                CurrentOnHandQty: currentStock?.QuantityOnHand ?? l.SystemQty,
                GoodQty: l.GoodQty,
                DefectiveQty: l.DefectiveQty,
                DamagedQty: l.DamagedQty,
                NearExpiryQty: l.NearExpiryQty,
                ExpiredQty: l.ExpiredQty,
                ActualQty: l.ActualQty,
                VarianceQty: l.VarianceQty,
                DefectiveReasonCode: l.DefectiveReasonCode,
                NearExpiryAction: l.NearExpiryAction,
                ReturnedQty: l.ReturnedQty,
                EligibleReturnQty: eligibleReturnQty,
                OriginalGrnId: l.OriginalGrnId,
                OriginalGrnLineId: l.OriginalGrnLineId,
                OriginalGrnCode: l.OriginalGrnCode,
                SupplierId: l.SupplierId,
                SupplierName: supplierName,
                EvidenceUrl: l.EvidenceUrl,
                Note: l.Note
            );
        }).ToList();

        return new StocktakeDetailDto(
            Id: entity.Id,
            Code: entity.Code,
            BranchId: entity.BranchId,
            BranchCode: entity.Branch?.BranchCode ?? string.Empty,
            BranchName: entity.Branch?.Name ?? "Chi nhánh",
            Scope: entity.Scope,
            ReasonType: entity.ReasonType,
            Status: entity.Status,
            CreatedByName: entity.CreatedByName,
            StartedAt: entity.StartedAt,
            SubmittedAt: entity.SubmittedAt,
            ReviewedByName: entity.ReviewedByName,
            ReviewedAt: entity.ReviewedAt,
            ReviewNote: entity.ReviewNote,
            Note: entity.Note,
            Lines: lineDtos
        );
    }

    public new async Task<Stocktake> AddAsync(
        Stocktake entity,
        CancellationToken cancellationToken = default)
    {
        await _appContext.Stocktakes.AddAsync(entity, cancellationToken);
        return entity;
    }

    public Task UpdateAsync(
        Stocktake entity,
        CancellationToken cancellationToken = default)
    {
        _appContext.Stocktakes.Update(entity);
        return Task.CompletedTask;
    }

    public async Task<string> GenerateCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var count = await _appContext.Stocktakes
            .IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == tenantId && s.Code.StartsWith($"KK-{today}"), cancellationToken);

        return $"KK-{today}-{(count + 1):D3}";
    }

    public async Task<List<BranchStock>> GetBranchStocksForScopeAsync(
        string tenantId,
        Guid branchId,
        string scope,
        List<Guid>? branchStockIds,
        Guid? categoryId,
        CancellationToken cancellationToken = default)
    {
        var query = _appContext.BranchStocks
            .Include(bs => bs.Product)
            .Where(bs => bs.TenantId == tenantId && bs.BranchId == branchId && !bs.IsDeleted && bs.QuantityOnHand > 0);

        if (branchStockIds != null && branchStockIds.Any())
        {
            query = query.Where(bs => branchStockIds.Contains(bs.Id));
        }
        else if (scope == "CATEGORY" && categoryId.HasValue && categoryId.Value != Guid.Empty)
        {
            query = query.Where(bs => bs.Product != null && bs.Product.CategoryId == categoryId.Value);
        }

        return await query
            .OrderBy(bs => bs.ExpiryDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, (Guid GrnId, Guid GrnLineId, string GrnCode, Guid SupplierId)>> GetGrnTraceByStockIdsAsync(
        string tenantId,
        List<Guid> branchStockIds,
        CancellationToken cancellationToken = default)
    {
        if (!branchStockIds.Any())
            return new Dictionary<Guid, (Guid, Guid, string, Guid)>();

        var grnLines = await _appContext.GoodsReceiptLines.AsNoTracking()
            .Include(l => l.GoodsReceipt)
            .Where(l => l.TenantId == tenantId &&
                        l.BranchStockId.HasValue &&
                        branchStockIds.Contains(l.BranchStockId.Value) &&
                        !l.IsDeleted &&
                        l.GoodsReceipt != null &&
                        l.GoodsReceipt.Status == "COMPLETED")
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        return grnLines
            .GroupBy(l => l.BranchStockId!.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return (
                        first.GoodsReceiptId,
                        first.Id,
                        first.GoodsReceipt?.Code ?? string.Empty,
                        first.GoodsReceipt?.SupplierId ?? Guid.Empty
                    );
                });
    }

    public async Task<BranchStock?> GetBranchStockByIdAsync(
        string tenantId,
        Guid branchStockId,
        CancellationToken cancellationToken = default)
    {
        return await _appContext.BranchStocks
            .FirstOrDefaultAsync(bs => bs.TenantId == tenantId && bs.Id == branchStockId && !bs.IsDeleted, cancellationToken);
    }
}

