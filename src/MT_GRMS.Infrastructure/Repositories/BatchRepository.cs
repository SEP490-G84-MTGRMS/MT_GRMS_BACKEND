using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BatchRepository : GenericRepository<Batch>, IBatchRepository
{
    private readonly AppDbContext _appContext;

    public BatchRepository(AppDbContext context) : base(context)
    {
        _appContext = context;
    }

    public async Task<BatchPagedResultDto> GetPagedBatchesAsync(
        string tenantId,
        BatchFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;

        var batchesQuery = _appContext.Batches.AsNoTracking().AsQueryable();

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            batchesQuery = batchesQuery.Where(b => b.BranchId == query.BranchId.Value);
        }

        var batches = await batchesQuery.ToListAsync(cancellationToken);

        var productIds = batches.Select(b => b.ProductId).Distinct().ToList();
        var branchIds = batches.Select(b => b.BranchId).Distinct().ToList();
        var batchIds = batches.Select(b => b.Id).Distinct().ToList();

        var products = await _appContext.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var branches = await _appContext.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var stocks = await _appContext.StockOnHands.AsNoTracking()
            .Where(s => s.BatchId.HasValue && batchIds.Contains(s.BatchId.Value))
            .ToDictionaryAsync(s => s.BatchId!.Value, cancellationToken);

        var allItems = batches.Select(b =>
        {
            products.TryGetValue(b.ProductId, out var product);
            branches.TryGetValue(b.BranchId, out var branch);
            stocks.TryGetValue(b.Id, out var stock);

            var daysLeft = (b.ExpiryDate.Date - now).Days;
            string status = daysLeft switch
            {
                <= 0 => "Expired",
                <= 3 => "Critical",
                <= 30 => "Warning",
                _ => "Normal"
            };

            decimal currentQty = stock?.Quantity ?? b.InitialQuantity;
            decimal totalValuation = currentQty * b.LandedCostPerUnit;

            return new BatchListItemDto(
                Id: b.Id,
                BatchCode: b.BatchCode,
                ProductId: b.ProductId,
                Sku: product?.ProductCode ?? string.Empty,
                ProductName: product?.Name ?? "Sản phẩm",
                CategoryName: product?.Category?.Name ?? "Chung",
                BranchId: b.BranchId,
                BranchName: branch?.Name ?? "Chi nhánh",
                SupplierId: null,
                SupplierName: "NCC Tiêu chuẩn",
                MfgDate: b.ManufactureDate,
                ExpiryDate: b.ExpiryDate,
                DaysLeft: daysLeft,
                QtyInitial: b.InitialQuantity,
                QtyCurrent: currentQty,
                Unit: product?.BaseUnit ?? "Cái",
                CostPrice: b.LandedCostPerUnit,
                TotalValuation: totalValuation,
                Status: status,
                ZoneArea: "Kho mát",
                ShelfLocation: "K-01",
                OriginalGrnNo: null
            );
        }).ToList();

        var kpiSummary = new BatchKpiSummaryDto(
            TotalBatches: allItems.Count,
            ExpiredCount: allItems.Count(x => x.Status == "Expired"),
            CriticalCount: allItems.Count(x => x.Status == "Critical"),
            WarningCount: allItems.Count(x => x.Status == "Warning"),
            NormalCount: allItems.Count(x => x.Status == "Normal"),
            ExpiredValue: allItems.Where(x => x.Status == "Expired").Sum(x => x.TotalValuation)
        );

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            allItems = allItems.Where(x =>
                x.BatchCode.ToLower().Contains(kw) ||
                x.Sku.ToLower().Contains(kw) ||
                x.ProductName.ToLower().Contains(kw)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            allItems = allItems.Where(x => x.Status.Equals(query.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        allItems = allItems.OrderBy(x => x.DaysLeft).ToList();

        var totalCount = allItems.Count;
        var pagedItems = allItems
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new BatchPagedResultDto(
            Items: pagedItems,
            TotalCount: totalCount,
            PageIndex: query.PageIndex,
            PageSize: query.PageSize,
            KpiSummary: kpiSummary
        );
    }

    public async Task<BatchListItemDto?> GetBatchByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var batch = await _appContext.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null) return null;

        var product = await _appContext.Products.AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == batch.ProductId, cancellationToken);

        var branch = await _appContext.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == batch.BranchId, cancellationToken);

        var stock = await _appContext.StockOnHands.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BatchId == batch.Id, cancellationToken);

        var now = DateTime.UtcNow.Date;
        var daysLeft = (batch.ExpiryDate.Date - now).Days;
        string status = daysLeft switch
        {
            <= 0 => "Expired",
            <= 3 => "Critical",
            <= 30 => "Warning",
            _ => "Normal"
        };

        decimal currentQty = stock?.Quantity ?? batch.InitialQuantity;
        decimal totalValuation = currentQty * batch.LandedCostPerUnit;

        return new BatchListItemDto(
            Id: batch.Id,
            BatchCode: batch.BatchCode,
            ProductId: batch.ProductId,
            Sku: product?.ProductCode ?? string.Empty,
            ProductName: product?.Name ?? "Sản phẩm",
            CategoryName: product?.Category?.Name ?? "Chung",
            BranchId: batch.BranchId,
            BranchName: branch?.Name ?? "Chi nhánh",
            SupplierId: null,
            SupplierName: "NCC Tiêu chuẩn",
            MfgDate: batch.ManufactureDate,
            ExpiryDate: batch.ExpiryDate,
            DaysLeft: daysLeft,
            QtyInitial: batch.InitialQuantity,
            QtyCurrent: currentQty,
            Unit: product?.BaseUnit ?? "Cái",
            CostPrice: batch.LandedCostPerUnit,
            TotalValuation: totalValuation,
            Status: status,
            ZoneArea: "Kho mát",
            ShelfLocation: "K-01",
            OriginalGrnNo: null
        );
    }

    public async Task<Batch> AddBatchAsync(
        Batch batch,
        CancellationToken cancellationToken = default)
    {
        await _appContext.Batches.AddAsync(batch, cancellationToken);

        var stock = new StockOnHand
        {
            TenantId = batch.TenantId,
            BranchId = batch.BranchId,
            ProductId = batch.ProductId,
            BatchId = batch.Id,
            Quantity = batch.InitialQuantity,
            ReservedQuantity = 0
        };
        await _appContext.StockOnHands.AddAsync(stock, cancellationToken);

        return batch;
    }

    public async Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(
        string tenantId,
        int daysThreshold,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;
        var thresholdDate = now.AddDays(daysThreshold);

        var query = _appContext.Batches.AsNoTracking().Where(b => b.ExpiryDate.Date <= thresholdDate);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(b => b.BranchId == branchId.Value);
        }

        var batches = await query.ToListAsync(cancellationToken);
        var productIds = batches.Select(b => b.ProductId).Distinct().ToList();

        var products = await _appContext.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return batches.Select(b =>
        {
            products.TryGetValue(b.ProductId, out var product);
            var daysRemaining = (b.ExpiryDate.Date - now).Days;

            return new ExpiryAlertDto(
                BatchId: b.Id,
                BatchCode: b.BatchCode,
                ProductName: product?.Name ?? "Sản phẩm",
                ExpiryDate: b.ExpiryDate,
                DaysRemaining: daysRemaining,
                RemainingQuantity: b.InitialQuantity
            );
        }).OrderBy(x => x.DaysRemaining).ToList();
    }
}

