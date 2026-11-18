using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class BatchesControllerTests
{
    private readonly Mock<IBatchService> _mockBatchService;
    private readonly BatchesController _controller;

    public BatchesControllerTests()
    {
        _mockBatchService = new Mock<IBatchService>();
        _controller = new BatchesController(_mockBatchService.Object);
    }

    private static BatchListItemDto CreateSampleBatch(Guid id, string batchCode = "BATCH-001")
    {
        return new BatchListItemDto(
            Id: id,
            BatchCode: batchCode,
            ProductId: Guid.NewGuid(),
            Sku: "SKU-001",
            ProductName: "Product 1",
            CategoryName: "Category 1",
            BranchId: Guid.NewGuid(),
            BranchName: "Branch 1",
            SupplierId: null,
            SupplierName: "Supplier 1",
            MfgDate: DateTime.UtcNow,
            ExpiryDate: DateTime.UtcNow.AddDays(30),
            DaysLeft: 30,
            QtyInitial: 10m,
            QtyCurrent: 10m,
            Unit: "Chai"
        );
    }

    [Fact]
    public async Task GetBatches_ReturnsOkWithPagedResult()
    {
        var query = new BatchFilterQuery();
        var paged = new BatchPagedResultDto(
            Items: new List<BatchListItemDto> { CreateSampleBatch(Guid.NewGuid()) },
            TotalCount: 1,
            PageIndex: 1,
            PageSize: 20,
            KpiSummary: new BatchKpiSummaryDto(1, 0, 0, 0, 1, 0m)
        );
        _mockBatchService.Setup(s => s.GetPagedBatchesAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetBatches(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BatchPagedResultDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
        Assert.Equal("BATCH-001", resp.Data.Items[0].BatchCode);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithBatchDetail()
    {
        var id = Guid.NewGuid();
        var batch = CreateSampleBatch(id, "BATCH-002");
        _mockBatchService.Setup(s => s.GetBatchByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batch);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BatchListItemDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetExpiryAlerts_ReturnsOkWithAlerts()
    {
        var branchId = Guid.NewGuid();
        IReadOnlyList<ExpiryAlertDto> alerts = new List<ExpiryAlertDto>
        {
            new(Guid.NewGuid(), "BATCH-EXP", "Product 1", DateTime.UtcNow.AddDays(10), 10, 5m)
        };
        _mockBatchService.Setup(s => s.GetNearExpiryBatchesAsync(30, branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);

        var result = await _controller.GetExpiryAlerts(30, branchId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<ExpiryAlertDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task CreateBatch_ReturnsCreatedAtAction()
    {
        var request = new BatchCreateRequest(
            ProductId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            BatchCode: "BATCH-NEW",
            ManufactureDate: DateTime.UtcNow,
            ExpiryDate: DateTime.UtcNow.AddDays(30),
            InitialQuantity: 10m,
            LandedCostPerUnit: 1000m
        );
        var created = new BatchResponse(
            Id: Guid.NewGuid(),
            ProductId: request.ProductId,
            BranchId: request.BranchId,
            BatchCode: "BATCH-NEW",
            ManufactureDate: request.ManufactureDate,
            ExpiryDate: request.ExpiryDate,
            InitialQuantity: 10m,
            LandedCostPerUnit: 1000m,
            DaysUntilExpiry: 30,
            IsExpired: false
        );
        _mockBatchService.Setup(s => s.CreateBatchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _controller.CreateBatch(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(BatchesController.GetById), createdResult.ActionName);
        Assert.Equal(created.Id, createdResult.RouteValues!["id"]);
    }

    [Fact]
    public async Task ApplyFefoDiscount_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new ApplyFefoDiscountRequest(15, true);
        var response = new FefoDiscountResponse(id, 15, true);
        _mockBatchService.Setup(s => s.ApplyFefoDiscountAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.ApplyFefoDiscount(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<FefoDiscountResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(15, resp.Data!.DiscountPercent);
    }
}
