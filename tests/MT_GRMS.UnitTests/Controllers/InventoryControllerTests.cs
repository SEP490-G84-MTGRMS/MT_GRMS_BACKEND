using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class InventoryControllerTests
{
    private readonly Mock<IInventoryService> _mockInventoryService;
    private readonly InventoryController _controller;

    public InventoryControllerTests()
    {
        _mockInventoryService = new Mock<IInventoryService>();
        _controller = new InventoryController(_mockInventoryService.Object);
    }

    [Fact]
    public async Task GetInventorySummary_ReturnsOkWithPagedResult()
    {
        var query = new InventoryFilterQuery();
        var paged = new InventoryPagedResultDto(
            Items: new List<InventoryStockItemDto>
            {
                new(
                    ProductId: Guid.NewGuid(),
                    Sku: "SKU-01",
                    Barcode: "BAR-01",
                    ProductName: "Product A",
                    CategoryName: "Category 1",
                    BranchId: Guid.NewGuid(),
                    BranchName: "Branch 1",
                    Unit: "Cái",
                    PhysicalQty: 10m,
                    AvailableQty: 10m,
                    PendingReturnQty: 0m,
                    ExpiredQty: 0m,
                    DamagedQty: 0m,
                    QuarantinedQty: 0m,
                    ReservedQty: 0m,
                    OnOrderQty: 25m,
                    MinStock: 5m,
                    AvgCost: 1000m,
                    TotalValue: 10000m,
                    Status: "OK",
                    BatchCount: 1,
                    Batches: new List<InventoryBatchSummaryDto>()
                )
            },
            TotalCount: 1,
            PageIndex: 1,
            PageSize: 20,
            KpiSummary: new InventoryKpiSummaryDto(1, 10000m, 0, 0, 0, 25m)
        );
        _mockInventoryService.Setup(s => s.GetPagedStockSummaryAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetInventorySummary(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<InventoryPagedResultDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
        Assert.Equal("Product A", resp.Data.Items[0].ProductName);
        Assert.Equal(25m, resp.Data.Items[0].OnOrderQty);
        Assert.Equal(25m, resp.Data.KpiSummary.TotalOnOrder);
    }

    [Fact]
    public async Task GetProductBatches_ReturnsOkWithBatches()
    {
        var productId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        IReadOnlyList<InventoryBatchSummaryDto> list = new List<InventoryBatchSummaryDto>
        {
            new(
                BatchId: Guid.NewGuid(),
                BatchCode: "B-001",
                MfgDate: null,
                ExpiryDate: null,
                DaysLeft: null,
                QuantityOnHand: 10m,
                ReservedQty: 0m,
                AvailableQty: 10m,
                Status: "OK"
            )
        };
        _mockInventoryService.Setup(s => s.GetProductBatchesAsync(branchId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetProductBatches(productId, branchId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<InventoryBatchSummaryDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
        Assert.Equal("B-001", resp.Data[0].BatchCode);
    }
}
