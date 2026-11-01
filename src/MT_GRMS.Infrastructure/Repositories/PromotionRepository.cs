using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class PromotionRepository : GenericRepository<Promotion>, IPromotionRepository
{
    public PromotionRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Promotions
            .Include(p => p.Conditions)
                .ThenInclude(c => c.Product)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Promotions
            .AnyAsync(p => p.PromotionCode == code && !p.IsDeleted, cancellationToken);
    }

    public async Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Promotions
            .Include(p => p.Conditions)
                .ThenInclude(c => c.Product)
            .Where(p => !p.IsDeleted && p.Type != "VOUCHER");

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(p => p.PromotionCode.ToLower().Contains(term) || p.PromotionName.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Type) && !string.Equals(query.Type, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            dbQuery = dbQuery.Where(p => p.Type == query.Type.ToUpper());
        }

        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(query.Status) && !string.Equals(query.Status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            switch (query.Status.ToUpper())
            {
                case "ACTIVE":
                    dbQuery = dbQuery.Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= now);
                    break;
                case "UPCOMING":
                    dbQuery = dbQuery.Where(p => p.IsActive && p.StartDate > now);
                    break;
                case "EXPIRED":
                    dbQuery = dbQuery.Where(p => p.EndDate < now);
                    break;
                case "PAUSED":
                    dbQuery = dbQuery.Where(p => !p.IsActive);
                    break;
            }
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var items = await dbQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Promotions
            .Include(p => p.Conditions)
                .ThenInclude(c => c.Product)
            .Where(p => !p.IsDeleted && p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc && p.Type != "VOUCHER");

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(p => p.TargetBranches == "ALL" || p.TargetBranches.Contains(branchId));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PromotionConditions
            .Where(c => c.PromotionId == promotionId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _context.PromotionConditions.RemoveRange(existing);
        }

        await _context.PromotionConditions.AddRangeAsync(conditions, cancellationToken);
    }

    public async Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Promotions
            .Where(p => !p.IsDeleted && p.Type == "VOUCHER");

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(p => p.PromotionCode.ToLower().Contains(term) || p.PromotionName.ToLower().Contains(term));
        }

        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            switch (status.ToUpper())
            {
                case "ACTIVE":
                    query = query.Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= now && (!p.UsageLimit.HasValue || p.UsedCount < p.UsageLimit.Value));
                    break;
                case "EXPIRED":
                    query = query.Where(p => p.EndDate < now);
                    break;
                case "PAUSED":
                    query = query.Where(p => !p.IsActive);
                    break;
                case "DEPLETED":
                    query = query.Where(p => p.UsageLimit.HasValue && p.UsedCount >= p.UsageLimit.Value);
                    break;
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddVoucherCodesAsync(IEnumerable<VoucherCode> codes, CancellationToken cancellationToken = default)
    {
        await _context.VoucherCodes.AddRangeAsync(codes, cancellationToken);
    }

    public async Task<bool> VoucherCodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.VoucherCodes
            .AnyAsync(v => v.Code == code && !v.IsDeleted, cancellationToken);
    }

    public async Task<VoucherCode?> GetVoucherCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.VoucherCodes
            .Include(v => v.Promotion)
            .FirstOrDefaultAsync(v => v.Code == code && !v.IsDeleted, cancellationToken);
    }

    public async Task<(IReadOnlyList<VoucherCode> Codes, int TotalCount)> GetPagedVoucherCodesAsync(Guid promotionId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.VoucherCodes
            .Where(v => v.PromotionId == promotionId && !v.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(v => v.Status == status.ToUpper());
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        var items = await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default)
    {
        var voucher = await _context.VoucherCodes
            .FirstOrDefaultAsync(v => v.Code == code && v.Status == "UNUSED" && !v.IsDeleted, cancellationToken);

        if (voucher == null) return false;

        voucher.Status = "USED";
        voucher.OrderId = orderId;
        voucher.OrderCode = orderCode;
        voucher.CustomerId = customerId;
        voucher.CustomerName = customerName;
        voucher.CustomerPhone = customerPhone;
        voucher.UsedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

