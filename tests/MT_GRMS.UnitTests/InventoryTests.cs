using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using Xunit;

namespace MT_GRMS.UnitTests;

public class InventoryTests
{
    private readonly FakeInventoryRepository _repo;
    private readonly FakeTenantContext _tenantContext;
    private readonly InventoryService _service;

    public InventoryTests()
    {
        _repo = new FakeInventoryRepository();
        _tenantContext = new FakeTenantContext("tenant-test-inv");
        _service = new InventoryService(_repo, _tenantContext);
    }

    [Fact]
    public async Task GetPagedStockSummaryAsync_ShouldReturnCorrectKpisAndItems()
    {
        // Arrange
        var query = new InventoryFilterQuery
        {
            Status = "ALL",
            PageIndex = 1,
            PageSize = 10
        };

        // Act
        var result = await _service.GetPagedStockSummaryAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.KpiSummary.TotalSkus);
        Assert.Equal(1, result.KpiSummary.LowStockCount);
    }

    [Fact]
    public async Task GetProductBatchesAsync_ShouldReturnBatchesForProduct()
    {
        // Act
        var result = await _service.GetProductBatchesAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("B01", result[0].BatchCode);
    }

    private class FakeInventoryRepository : IInventoryRepository
    {
        public Task<InventoryPagedResultDto> GetPagedStockSummaryAsync(string tenantId, InventoryFilterQuery query, CancellationToken cancellationToken = default)
        {
            var items = new List<InventoryStockItemDto>
            {
                new InventoryStockItemDto(
                    Guid.NewGuid(), "SKU-01", "8930001", "Sữa tươi TH 1L", "Dairy",
                    Guid.NewGuid(), "Chi nhánh 1", "Hộp", 50, 40, 5, 5, 0, 15, 30000, 1500000, "OK",
                    new List<InventoryBatchSummaryDto>()
                ),
                new InventoryStockItemDto(
                    Guid.NewGuid(), "SKU-02", "8930002", "Bánh mì sandwich", "Bakery",
                    Guid.NewGuid(), "Chi nhánh 1", "Gói", 8, 8, 0, 0, 0, 10, 15000, 120000, "LOW_STOCK",
                    new List<InventoryBatchSummaryDto>()
                )
            };

            var kpis = new InventoryKpiSummaryDto(2, 1620000, 1, 0, 0);

            return Task.FromResult(new InventoryPagedResultDto(
                Items: items,
                TotalCount: items.Count,
                PageIndex: query.PageIndex,
                PageSize: query.PageSize,
                KpiSummary: kpis
            ));
        }

        public Task<IReadOnlyList<InventoryBatchSummaryDto>> GetProductBatchesAsync(string tenantId, Guid branchId, Guid productId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<InventoryBatchSummaryDto> batches = new List<InventoryBatchSummaryDto>
            {
                new InventoryBatchSummaryDto(Guid.NewGuid(), "B01", DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(20), 20, 40, "Warning")
            };
            return Task.FromResult(batches);
        }

        public Task<StockOnHand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<StockOnHand?>(null);
        public Task<IReadOnlyList<StockOnHand>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockOnHand>>(new List<StockOnHand>());
        public Task<StockOnHand> AddAsync(StockOnHand entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public Task UpdateAsync(StockOnHand entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(StockOnHand entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { CurrentTenantId = tenantId; }
        public void SetRole(string? role) { CurrentRole = role ?? string.Empty; }
        public FakeTenantContext(string tenantId) { CurrentTenantId = tenantId; }
    }
}
