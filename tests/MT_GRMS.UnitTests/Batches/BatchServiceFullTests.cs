using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Batches;

public class BatchServiceFullTests
{
    private readonly Mock<IBatchRepository> _mockRepo;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly BatchService _service;
    private const string TenantId = "tenant-batch-01";

    public BatchServiceFullTests()
    {
        _mockRepo = new Mock<IBatchRepository>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);
        _mockUow = new Mock<IUnitOfWork>();

        _service = new BatchService(_mockRepo.Object, _mockTenant.Object, _mockUow.Object);
    }

    [Fact]
    public async Task GetPagedBatchesAsync_ShouldCallRepository()
    {
        var query = new BatchFilterQuery();
        var kpi = new BatchKpiSummaryDto(0, 0, 0, 0, 0, 0m);
        var paged = new BatchPagedResultDto(new List<BatchListItemDto>(), 0, 1, 10, kpi);
        _mockRepo.Setup(r => r.GetPagedBatchesAsync(TenantId, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _service.GetPagedBatchesAsync(query);
        Assert.NotNull(result);
        _mockRepo.Verify(r => r.GetPagedBatchesAsync(TenantId, query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBatchByIdAsync_WhenFound_ShouldReturnBatch()
    {
        var id = Guid.NewGuid();
        var dto = new BatchListItemDto(
            Id: id,
            BatchCode: "BATCH-01",
            ProductId: Guid.NewGuid(),
            Sku: "SKU-01",
            ProductName: "Product A",
            CategoryName: "Category 1",
            BranchId: Guid.NewGuid(),
            BranchName: "Branch 1",
            SupplierId: Guid.NewGuid(),
            SupplierName: "Supplier 1",
            MfgDate: DateTime.UtcNow,
            ExpiryDate: DateTime.UtcNow.AddMonths(6),
            DaysLeft: 180,
            QtyInitial: 100m,
            QtyCurrent: 100m,
            Unit: "Chai"
        );
        _mockRepo.Setup(r => r.GetBatchByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _service.GetBatchByIdAsync(id);
        Assert.NotNull(result);
        Assert.Equal("BATCH-01", result.BatchCode);
    }

    [Fact]
    public async Task GetBatchByIdAsync_WhenNotFound_ShouldThrowKeyNotFoundException()
    {
        var id = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetBatchByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BatchListItemDto?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetBatchByIdAsync(id));
    }

    [Fact]
    public async Task CreateBatchAsync_ShouldThrowBusinessException_Deprecated()
    {
        var request = new BatchCreateRequest(
            ProductId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            BatchCode: "BATCH-NEW",
            ManufactureDate: DateTime.UtcNow.AddDays(-10),
            ExpiryDate: DateTime.UtcNow.AddDays(100),
            InitialQuantity: 50,
            LandedCostPerUnit: 25000m
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateBatchAsync(request));

        Assert.Equal("BATCH_CREATE_DEPRECATED", ex.ErrorCode);
        _mockRepo.Verify(r => r.AddBatchAsync(It.IsAny<Batch>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetNearExpiryBatchesAsync_ShouldCallRepository()
    {
        _mockRepo.Setup(r => r.GetNearExpiryBatchesAsync(TenantId, 30, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExpiryAlertDto>());

        var result = await _service.GetNearExpiryBatchesAsync(30, null);
        Assert.NotNull(result);
        _mockRepo.Verify(r => r.GetNearExpiryBatchesAsync(TenantId, 30, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyFefoDiscountAsync_InvalidPercentage_ShouldThrowArgumentOutOfRangeException()
    {
        var id = Guid.NewGuid();
        var badRequest1 = new ApplyFefoDiscountRequest(-5m, true);
        var badRequest2 = new ApplyFefoDiscountRequest(95m, true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.ApplyFefoDiscountAsync(id, badRequest1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.ApplyFefoDiscountAsync(id, badRequest2));
    }

    [Fact]
    public async Task ApplyFefoDiscountAsync_StockNotFound_ShouldThrowKeyNotFoundException()
    {
        var id = Guid.NewGuid();
        var request = new ApplyFefoDiscountRequest(10m, true);
        _mockRepo.Setup(r => r.GetBranchStockForUpdateAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BranchStock?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.ApplyFefoDiscountAsync(id, request));
    }

    [Fact]
    public async Task ApplyFefoDiscountAsync_StockExpired_ShouldThrowInvalidOperationException()
    {
        var id = Guid.NewGuid();
        var request = new ApplyFefoDiscountRequest(10m, true);
        var stock = new BranchStock
        {
            Id = id,
            TenantId = TenantId,
            ExpiryDate = DateTime.UtcNow.AddDays(-5) // Expired
        };
        _mockRepo.Setup(r => r.GetBranchStockForUpdateAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApplyFefoDiscountAsync(id, request));
    }

    [Fact]
    public async Task ApplyFefoDiscountAsync_ValidStock_ShouldUpdateDiscount()
    {
        var id = Guid.NewGuid();
        var request = new ApplyFefoDiscountRequest(20m, true);
        var stock = new BranchStock
        {
            Id = id,
            TenantId = TenantId,
            ExpiryDate = DateTime.UtcNow.AddDays(10) // Not expired
        };
        _mockRepo.Setup(r => r.GetBranchStockForUpdateAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _service.ApplyFefoDiscountAsync(id, request);

        Assert.Equal(id, result.BatchId);
        Assert.Equal(20m, result.DiscountPercent);
        Assert.True(result.IsActive);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
