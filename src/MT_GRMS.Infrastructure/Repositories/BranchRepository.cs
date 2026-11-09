using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BranchRepository : GenericRepository<Branch>, IBranchRepository
{
    public BranchRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Branches.AnyAsync(b => b.BranchCode == code, cancellationToken);
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Branches.CountAsync(b => b.IsActive && !b.IsDeleted, cancellationToken);
    }

    public async Task<int> CountTotalAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Branches.CountAsync(b => !b.IsDeleted, cancellationToken);
    }

    public async Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
        string? keyword,
        string? branchCode,
        string? status,
        IReadOnlyList<string>? branchCodes,
        IReadOnlyList<string>? branchNames,
        IReadOnlyList<string>? addresses,
        IReadOnlyList<string>? phones,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Branches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(b =>
                b.BranchCode.ToLower().Contains(kw) ||
                b.Name.ToLower().Contains(kw) ||
                (b.Address != null && b.Address.ToLower().Contains(kw)) ||
                (b.Phone != null && b.Phone.ToLower().Contains(kw)));
        }

        var codeTerms = CleanTerms(branchCodes);
        if (codeTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.BranchCode, codeTerms, ignoreCase: true);
        }
        else if (!string.IsNullOrWhiteSpace(branchCode) && !string.Equals(branchCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(b => b.BranchCode == branchCode);
        }

        var nameTerms = CleanTerms(branchNames);
        if (nameTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.Name, nameTerms, ignoreCase: true);
        }

        var addressTerms = CleanTerms(addresses);
        if (addressTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.Address ?? string.Empty, addressTerms, ignoreCase: true);
        }

        var phoneTerms = CleanTerms(phones);
        if (phoneTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.Phone ?? string.Empty, phoneTerms, ignoreCase: true);
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedStatus = status.Trim().ToLower();
            if (normalizedStatus == "active")
            {
                query = query.Where(b => b.IsActive);
            }
            else if (normalizedStatus == "inactive")
            {
                query = query.Where(b => !b.IsActive);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderBy(b => b.BranchCode)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<BranchDetailDto?> GetBranchDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var branch = await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken);

        if (branch == null) return null;

        int totalProducts = 0;
        decimal totalStock = 0;
        int lowStockCount = 0;
        var recentProducts = new List<BranchProductSummaryItemDto>();

        try
        {
            var branchStocks = await _context.BranchStocks
                .Include(s => s.Product)
                .AsNoTracking()
                .Where(s => s.BranchId == id && !s.IsDeleted)
                .ToListAsync(cancellationToken);

            totalStock = branchStocks.Sum(s => s.QuantityOnHand);

            List<Guid> catalogProductIds = new();

            try
            {
                totalProducts = await _context.BranchProducts
                    .AsNoTracking()
                    .CountAsync(bp => bp.BranchId == id && !bp.IsDeleted, cancellationToken);

                catalogProductIds = await _context.BranchProducts
                    .AsNoTracking()
                    .Where(bp => bp.BranchId == id && !bp.IsDeleted)
                    .Select(bp => bp.ProductId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                recentProducts = await _context.BranchProducts
                    .AsNoTracking()
                    .Include(bp => bp.Product)
                    .Where(bp => bp.BranchId == id && !bp.IsDeleted && bp.Product != null)
                    .OrderByDescending(bp => bp.CreatedAt)
                    .Take(5)
                    .Select(bp => new BranchProductSummaryItemDto
                    {
                        ProductId = bp.ProductId,
                        Sku = bp.Product!.ProductCode,
                        Barcode = bp.Product.Barcode,
                        ProductName = bp.Product.Name,
                        Unit = bp.Product.BaseUnit,
                        SellingPrice = bp.SellingPriceOverride ?? bp.Product.SellingPrice,
                        Status = bp.Status
                    })
                    .ToListAsync(cancellationToken);
            }
            catch
            {
                // Bỏ qua lỗi nếu bảng BranchProducts chưa tồn tại hoặc rỗng
            }

            // Fallback sang BranchStocks nếu catalog chưa có sản phẩm
            if (totalProducts == 0 && branchStocks.Count > 0)
            {
                var distinctStockProducts = branchStocks.Select(s => s.ProductId).Distinct().ToList();
                totalProducts = distinctStockProducts.Count;
                catalogProductIds = distinctStockProducts;

                if (recentProducts.Count == 0)
                {
                    recentProducts = branchStocks
                        .Where(s => s.Product != null)
                        .GroupBy(s => s.ProductId)
                        .Select(g => g.First())
                        .Take(5)
                        .Select(s => new BranchProductSummaryItemDto
                        {
                            ProductId = s.ProductId,
                            Sku = s.Product!.ProductCode,
                            Barcode = s.Product.Barcode,
                            ProductName = s.Product.Name,
                            Unit = s.Product.BaseUnit,
                            SellingPrice = s.RetailPrice ?? s.Product.SellingPrice,
                            Status = s.Status
                        })
                        .ToList();
                }
            }

            lowStockCount = catalogProductIds.Count(pid =>
            {
                var qty = branchStocks.Where(s => s.ProductId == pid).Sum(s => s.QuantityOnHand);
                var min = branchStocks.FirstOrDefault(s => s.ProductId == pid)?.Product?.MinStock ?? 0m;
                return qty <= min;
            });
        }
        catch
        {
            // Bỏ qua lỗi thống kê tồn kho nếu có
        }

        int totalEmployees = 0;
        int activeEmployees = 0;
        var recentEmployees = new List<BranchEmployeeSummaryItemDto>();

        try
        {
            totalEmployees = await _context.Employees
                .AsNoTracking()
                .CountAsync(e => e.BranchId == id && !e.IsDeleted, cancellationToken);

            activeEmployees = await _context.Employees
                .AsNoTracking()
                .CountAsync(e => e.BranchId == id && !e.IsDeleted && e.Status == EmployeeStatus.Active, cancellationToken);

            recentEmployees = await _context.Employees
                .AsNoTracking()
                .Where(e => e.BranchId == id && !e.IsDeleted)
                .OrderByDescending(e => e.CreatedAt)
                .Take(5)
                .Select(e => new BranchEmployeeSummaryItemDto
                {
                    Id = e.Id,
                    EmployeeCode = e.EmployeeCode,
                    FullName = e.FullName,
                    Phone = e.Phone,
                    Status = e.Status.ToString(),
                    StartDate = e.StartDate
                })
                .ToListAsync(cancellationToken);
        }
        catch
        {
            // Bỏ qua lỗi thống kê nhân sự nếu có
        }

        return new BranchDetailDto
        {
            Id = branch.Id,
            Code = branch.BranchCode,
            Name = branch.Name,
            Address = branch.Address ?? string.Empty,
            Phone = branch.Phone,
            Email = branch.Email,
            ManagerName = branch.ManagerName,
            IsHeadquarter = branch.IsHeadquarter,
            Status = branch.IsActive ? "Active" : "Inactive",
            CreatedAt = branch.CreatedAt,
            TotalProducts = totalProducts,
            TotalStock = totalStock,
            LowStockCount = lowStockCount,
            TotalEmployees = totalEmployees,
            ActiveEmployees = activeEmployees,
            PendingOrdersCount = branch.PendingOrdersCount,
            LastOrderDate = branch.LastOrderDate,
            RecentEmployees = recentEmployees,
            RecentProducts = recentProducts
        };
    }

    private static List<string> CleanTerms(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0) return [];
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLower())
            .Distinct()
            .ToList();
    }

    private static IQueryable<T> WhereAnyContains<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> selector,
        IReadOnlyList<string> terms,
        bool ignoreCase)
    {
        if (terms.Count == 0) return query;

        var parameter = selector.Parameters[0];
        Expression source = selector.Body;
        if (ignoreCase)
        {
            source = Expression.Call(source, nameof(string.ToLower), Type.EmptyTypes);
        }

        Expression? body = null;
        foreach (var term in terms)
        {
            var contains = Expression.Call(
                source,
                nameof(string.Contains),
                Type.EmptyTypes,
                Expression.Constant(term));
            body = body is null ? contains : Expression.OrElse(body, contains);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(body!, parameter);
        return query.Where(predicate);
    }
}
