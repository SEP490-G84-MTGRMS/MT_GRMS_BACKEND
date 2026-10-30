using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Branches;

public interface IBranchProductRepository
{
    Task<(IReadOnlyList<BranchProductDto> Items, int TotalCount)> GetPagedBranchProductsAsync(
        Guid branchId,
        string? searchTerm,
        string? category,
        string? status,
        string? stockAlertFilter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchSummaryDto>> GetBranchSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<BranchStock?> GetBranchStockAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchStock>> GetBranchStocksByProductAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default);

    Task AddBranchStockAsync(
        BranchStock branchStock,
        CancellationToken cancellationToken = default);

    void RemoveBranchStocks(
        IEnumerable<BranchStock> branchStocks);

    Task<Product?> GetProductByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Branch?> GetBranchByIdAsync(
        Guid branchId,
        CancellationToken cancellationToken = default);
}
