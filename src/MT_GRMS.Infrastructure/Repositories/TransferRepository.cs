using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public sealed class TransferRepository(AppDbContext context) : ITransferRepository
{
    public async Task<(IReadOnlyList<Transfer> Items, int TotalItems)> GetPagedAsync(
        string tenantId,
        Guid? restrictToBranchId,
        TransferFilterParams filter,
        CancellationToken ct = default)
    {
        var query = context.Transfers
            .AsNoTracking()
            .Include(t => t.Details)
            .Where(x => !x.IsDeleted && x.TenantId == tenantId)
            .AsQueryable();

        if (restrictToBranchId.HasValue)
        {
            var branchId = restrictToBranchId.Value;
            query = query.Where(x => x.FromBranchId == branchId || x.ToBranchId == branchId);
        }

        if (filter.FromBranchId.HasValue)
        {
            query = query.Where(x => x.FromBranchId == filter.FromBranchId.Value);
        }

        if (filter.ToBranchId.HasValue)
        {
            query = query.Where(x => x.ToBranchId == filter.ToBranchId.Value);
        }

        var statuses = filter.Statuses is { Count: > 0 }
            ? filter.Statuses
            : filter.Status.HasValue
                ? [filter.Status.Value]
                : null;
        if (statuses is not null)
        {
            query = query.Where(x => statuses.Contains(x.Status));
        }

        if (filter.CompletionResult.HasValue)
        {
            var completionResult = filter.CompletionResult.Value;
            query = query.Where(x => x.CompletionResult == completionResult);
        }

        var receiveDate = string.Equals(filter.DateField, "receive", StringComparison.OrdinalIgnoreCase);
        if (filter.FromDate.HasValue)
        {
            var fromDate = filter.FromDate.Value;
            query = receiveDate
                ? query.Where(x => x.ReceivedAt >= fromDate)
                : query.Where(x => (x.DispatchedAt ?? x.CreatedAt) >= fromDate);
        }

        if (filter.ToDate.HasValue)
        {
            var toExclusive = filter.ToDate.Value.Date.AddDays(1);
            query = receiveDate
                ? query.Where(x => x.ReceivedAt < toExclusive)
                : query.Where(x => (x.DispatchedAt ?? x.CreatedAt) < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var q = filter.Search.Trim();
            if (context.Database.IsNpgsql())
            {
                var pattern = $"%{q}%";
                query = query.Where(x =>
                    EF.Functions.ILike(x.TransferCode, pattern) ||
                    (x.Note != null && EF.Functions.ILike(x.Note, pattern)));
            }
            else
            {
                query = query.Where(x =>
                    x.TransferCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.Note != null && x.Note.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (string.Equals(filter.ReceiveStatus, "match", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x =>
                x.Status == TransferStatus.Completed &&
                x.CompletionResult != TransferCompletionResult.Returned &&
                x.Details.All(d => d.ReceivedQuantity == d.SentQuantity && d.DamagedQuantity == 0));
        }
        else if (string.Equals(filter.ReceiveStatus, "mismatch", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x =>
                x.Status == TransferStatus.Completed &&
                x.CompletionResult != TransferCompletionResult.Returned &&
                x.Details.Any(d => d.ReceivedQuantity != d.SentQuantity || d.DamagedQuantity > 0));
        }

        var totalItems = await query.CountAsync(ct);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems);
    }

    public async Task<Transfer?> GetTransferForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        if (context.Database.IsNpgsql())
        {
            return await context.Transfers
                .FromSqlInterpolated($@"
                    SELECT * FROM ""Transfers""
                    WHERE ""TransferID"" = {id}
                      AND NOT ""IsDeleted""
                    FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }

        return await context.Transfers.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
    }

    public async Task<BranchStock?> GetBranchStockByIdForUpdateAsync(Guid branchStockId, CancellationToken ct = default)
    {
        if (context.Database.IsNpgsql())
        {
            return await context.BranchStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM ""BranchStocks""
                    WHERE ""Id"" = {branchStockId}
                      AND NOT ""IsDeleted""
                    FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }

        return await context.BranchStocks.FirstOrDefaultAsync(s => s.Id == branchStockId && !s.IsDeleted, ct);
    }

    public async Task<IReadOnlyList<BranchStock>> GetBranchStocksForUpdateAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        CancellationToken ct = default)
    {
        if (context.Database.IsNpgsql())
        {
            return await context.BranchStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM ""BranchStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""BranchId"" = {branchId}
                      AND ""ProductId"" = {productId}
                      AND NOT ""IsDeleted""
                    ORDER BY ""Id"" ASC
                    FOR UPDATE")
                .ToListAsync(ct);
        }

        return await context.BranchStocks
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId && s.ProductId == productId && !s.IsDeleted)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BranchStock>> GetBranchStocksByProductForUpdateAsync(
        string tenantId,
        Guid productId,
        Guid excludeBranchId,
        CancellationToken ct = default)
    {
        if (context.Database.IsNpgsql())
        {
            return await context.BranchStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM ""BranchStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""ProductId"" = {productId}
                      AND ""BranchId"" <> {excludeBranchId}
                      AND NOT ""IsDeleted""
                    ORDER BY ""Id"" ASC
                    FOR UPDATE")
                .ToListAsync(ct);
        }

        return await context.BranchStocks
            .Where(s => s.TenantId == tenantId && s.ProductId == productId && s.BranchId != excludeBranchId && !s.IsDeleted)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
    }
}
