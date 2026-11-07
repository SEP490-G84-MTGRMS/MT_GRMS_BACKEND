using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Inventory;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BatchExpiryTests
{
    private readonly FakeBatchRepository _repo;
    private readonly FakeTenantContext _tenantContext;
    private readonly FakeUnitOfWork _uow;
    private readonly BatchService _batchService;

    public BatchExpiryTests()
    {
        _repo = new FakeBatchRepository();
        _tenantContext = new FakeTenantContext("tenant-test-01");
        _uow = new FakeUnitOfWork();

        _batchService = new BatchService(_repo, _tenantContext, _uow);
    }

    [Fact]
    public async Task GetPagedBatchesAsync_ShouldCallRepository_WithCorrectTenantAndFilter()
    {
        // Arrange
        var query = new BatchFilterQuery
        {
            Keyword = "Sữa",
            Status = "Critical",
            PageIndex = 1,
            PageSize = 10
        };

        // Act
        var result = await _batchService.GetPagedBatchesAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.PageIndex);
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task GetNearExpiryBatchesAsync_ShouldReturnAlerts_WhenBatchesExpireWithinThreshold()
    {
        // Act
        var result = await _batchService.GetNearExpiryBatchesAsync(30);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count);
    }

    private class FakeBatchRepository : IBatchRepository
    {
        public Task<BatchPagedResultDto> GetPagedBatchesAsync(string tenantId, BatchFilterQuery query, CancellationToken cancellationToken = default)
        {
            var kpi = new BatchKpiSummaryDto(10, 1, 2, 3, 4, 150000);
            return Task.FromResult(new BatchPagedResultDto(
                Items: new List<BatchListItemDto>(),
                TotalCount: 0,
                PageIndex: query.PageIndex,
                PageSize: query.PageSize,
                KpiSummary: kpi
            ));
        }

        public Task<BatchListItemDto?> GetBatchByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<BatchListItemDto?>(null);
        }

        public Task<Batch> AddBatchAsync(Batch batch, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(batch);
        }

        public Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(string tenantId, int daysThreshold, Guid? branchId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExpiryAlertDto> list = new List<ExpiryAlertDto>
            {
                new ExpiryAlertDto(Guid.NewGuid(), "BATCH-01", "Sữa tươi TH 1L", DateTime.UtcNow.AddDays(5), 5, 20),
                new ExpiryAlertDto(Guid.NewGuid(), "BATCH-02", "Sữa chua Vinamilk", DateTime.UtcNow.AddDays(2), 2, 50)
            };
            return Task.FromResult(list);
        }
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

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}


