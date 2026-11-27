using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ProductPriceHistoryRepository : GenericRepository<ProductPriceHistory>, IProductPriceHistoryRepository
{
    public ProductPriceHistoryRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<(IReadOnlyList<ProductPriceHistory> Items, int TotalCount)> GetPagedByProductIdAsync(
        Guid productId,
        ProductPriceHistoryQuery filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ProductPriceHistories
            .Include(h => h.Branch)
            .Include(h => h.ProductUnit)
            .Where(h => h.ProductId == productId && !h.IsDeleted);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(h => h.BranchId == filter.BranchId.Value || h.BranchId == null);
        }

        if (filter.ProductUnitId.HasValue && filter.ProductUnitId.Value != Guid.Empty)
            query = query.Where(h => h.ProductUnitId == filter.ProductUnitId.Value);

        if (!string.IsNullOrWhiteSpace(filter.PriceScope))
            query = query.Where(h => h.PriceScope == filter.PriceScope);

        if (filter.FromDate.HasValue)
            query = query.Where(h => h.CreatedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(h => h.CreatedAt <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim().ToLower();
            query = query.Where(h =>
                h.ChangedByUserName.ToLower().Contains(term) ||
                h.Reason.ToLower().Contains(term) ||
                (h.Branch != null && h.Branch.Name.ToLower().Contains(term)) ||
                (h.ProductUnit != null && h.ProductUnit.UnitName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        // Enrich phone-only or ID-only ChangedByUserName with User.FullName for readable audit trail (FE-03.2)
        var userIds = items
            .Select(h => h.ChangedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();
        var phoneNames = items
            .Select(h => h.ChangedByUserName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.All(char.IsDigit))
            .Distinct()
            .ToList();
        if (userIds.Count > 0 || phoneNames.Count > 0)
        {
            var matchedUsers = await _context.Users.AsNoTracking()
                .Where(u => (userIds.Contains(u.Id) || (u.Phone != null && phoneNames.Contains(u.Phone))) && !string.IsNullOrWhiteSpace(u.FullName))
                .Select(u => new { u.Id, u.Phone, u.FullName })
                .ToListAsync(cancellationToken);

            var byId = matchedUsers.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().FullName);
            var byPhone = matchedUsers.Where(u => !string.IsNullOrWhiteSpace(u.Phone)).GroupBy(u => u.Phone!).ToDictionary(g => g.Key, g => g.First().FullName);

            foreach (var h in items)
            {
                if (string.IsNullOrWhiteSpace(h.ChangedByUserName) || h.ChangedByUserName.All(char.IsDigit))
                {
                    if (!string.IsNullOrWhiteSpace(h.ChangedByUserId) && byId.TryGetValue(h.ChangedByUserId, out var fullName))
                        h.ChangedByUserName = fullName;
                    else if (!string.IsNullOrWhiteSpace(h.ChangedByUserName) && byPhone.TryGetValue(h.ChangedByUserName, out var byPhoneName))
                        h.ChangedByUserName = byPhoneName;
                }
            }
        }

        return (items, totalCount);
    }
}

