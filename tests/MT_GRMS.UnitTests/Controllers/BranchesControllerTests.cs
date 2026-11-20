using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class BranchesControllerTests
{
    private readonly Mock<IBranchService> _mockBranchService;
    private readonly Mock<IBranchProductService> _mockBranchProductService;
    private readonly BranchesController _controller;

    public BranchesControllerTests()
    {
        _mockBranchService = new Mock<IBranchService>();
        _mockBranchProductService = new Mock<IBranchProductService>();
        _controller = new BranchesController(_mockBranchService.Object, _mockBranchProductService.Object);
    }

    [Fact]
    public async Task GetQuota_ReturnsOk()
    {
        var quota = new BranchQuotaDto(2, 5, true, "PRO", "Pro Plan");
        _mockBranchService.Setup(s => s.GetBranchQuotaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(quota);

        var result = await _controller.GetQuota(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchQuotaDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(2, resp.Data!.UsedBranches);
    }

    [Fact]
    public async Task GetBranches_ReturnsOk()
    {
        var query = new BranchFilterQuery();
        var paged = new PagedResult<BranchDto>
        {
            Items = new List<BranchDto>
            {
                new() { Id = Guid.NewGuid(), Code = "BR-01", Name = "Branch 1" }
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockBranchService.Setup(s => s.GetPagedBranchesAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetBranches(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<BranchDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
    }

    [Fact]
    public async Task GetBranchSummaries_ReturnsOk()
    {
        IReadOnlyList<BranchSummaryDto> summaries = new List<BranchSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Branch 1", TotalProducts = 100 }
        };
        _mockBranchProductService.Setup(s => s.GetBranchSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries);

        var result = await _controller.GetBranchSummaries(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<BranchSummaryDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetById_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var branch = new BranchDto { Id = id, Code = "BR-01" };
        _mockBranchService.Setup(s => s.GetBranchByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetDetail_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var detail = new BranchDetailDto { Id = id, Code = "BR-01", Name = "Branch 1" };
        _mockBranchService.Setup(s => s.GetBranchDetailAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetDetail(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task Create_ReturnsStatusCode201()
    {
        var request = new CreateBranchRequest { Name = "Branch New" };
        var created = new BranchDto { Id = Guid.NewGuid(), Name = "Branch New" };
        _mockBranchService.Setup(s => s.CreateBranchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _controller.Create(request, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);
        var resp = Assert.IsType<ApiResponse<BranchDto>>(statusResult.Value);
        Assert.True(resp.Success);
        Assert.Equal("Branch New", resp.Data!.Name);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new UpdateBranchRequest { Name = "Branch Updated" };
        var updated = new BranchDto { Id = id, Name = "Branch Updated" };
        _mockBranchService.Setup(s => s.UpdateBranchAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.Update(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Branch Updated", resp.Data!.Name);
    }

    [Theory]
    [InlineData(true, "Đã kích hoạt hoạt động chi nhánh.")]
    [InlineData(false, "Đã tạm dừng hoạt động chi nhánh.")]
    public async Task ToggleStatus_ReturnsOkWithExpectedMessage(bool isActive, string expectedMsg)
    {
        var id = Guid.NewGuid();
        var request = new UpdateBranchStatusRequest { IsActive = isActive };
        var updated = new BranchDto { Id = id, Status = isActive ? "Active" : "Inactive" };
        _mockBranchService.Setup(s => s.ToggleBranchStatusAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.ToggleStatus(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(expectedMsg, resp.Message);
    }

    [Fact]
    public async Task Delete_ReturnsOk()
    {
        var id = Guid.NewGuid();
        _mockBranchService.Setup(s => s.DeleteBranchAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
        Assert.True(resp.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAllBranchProducts_ReturnsOk(bool hasBranchId)
    {
        var branchId = hasBranchId ? Guid.NewGuid() : (Guid?)null;
        var filter = new BranchProductQueryFilter();
        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId ?? Guid.Empty, filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetAllBranchProducts(branchId, filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<BranchProductDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetBranchProducts_ReturnsOk()
    {
        var branchId = Guid.NewGuid();
        var filter = new BranchProductQueryFilter();
        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto>(),
            TotalItems = 0,
            Page = 1,
            PageSize = 20
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetBranchProducts(branchId, filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<BranchProductDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetAssignableProducts_ReturnsOk()
    {
        var id = Guid.NewGuid();
        IReadOnlyList<AssignableBranchProductDto> list = new List<AssignableBranchProductDto>
        {
            new() { ProductId = Guid.NewGuid(), Sku = "SKU-01" }
        };
        _mockBranchProductService.Setup(s => s.GetAssignableProductsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetAssignableProducts(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<AssignableBranchProductDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task AssignProducts_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var command = new AssignBranchProductsCommand();
        IReadOnlyList<BranchProductDto> list = new List<BranchProductDto>();
        _mockBranchProductService.Setup(s => s.AssignProductsAsync(id, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.AssignProducts(id, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<BranchProductDto>>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var command = new UpdateBranchProductCommand();
        var dto = new BranchProductDto { BranchId = id, ProductId = productId };
        _mockBranchProductService.Setup(s => s.UpdateProductAsync(id, productId, command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.UpdateProduct(id, productId, command, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchProductDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task ToggleProductStatus_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var request = new ToggleBranchProductStatusRequest { Status = "ACTIVE" };
        var dto = new BranchProductDto { BranchId = id, ProductId = productId, Status = "ACTIVE" };
        _mockBranchProductService.Setup(s => s.ToggleStatusAsync(id, productId, request.Status, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.ToggleProductStatus(id, productId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchProductDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task RemoveProduct_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var productId = Guid.NewGuid();
        _mockBranchProductService.Setup(s => s.RemoveProductAsync(id, productId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.RemoveProduct(id, productId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task ExportBranchProducts_ReturnsFileResult()
    {
        var id = Guid.NewGuid();
        var mockExcel = new Mock<IBranchProductExcelService>();
        var fileDto = new BranchProductExcelFileDto
        {
            Content = new byte[] { 4, 5, 6 },
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = "branch_products.xlsx"
        };
        mockExcel.Setup(x => x.ExportAsync(id, It.IsAny<BranchProductQueryFilter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileDto);

        var result = await _controller.ExportBranchProducts(
            id,
            new BranchProductQueryFilter(),
            mockExcel.Object,
            CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(fileDto.ContentType, fileResult.ContentType);
        Assert.Equal(fileDto.FileName, fileResult.FileDownloadName);
    }

    [Fact]
    public async Task ImportBranchProducts_NullOrEmptyFile_ReturnsBadRequest()
    {
        var id = Guid.NewGuid();
        var mockExcel = new Mock<IBranchProductExcelService>();

        // Null file
        var result1 = await _controller.ImportBranchProducts(
            id,
            null,
            new BranchProductQueryFilter(),
            mockExcel.Object,
            CancellationToken.None);
        var badReq1 = Assert.IsType<BadRequestObjectResult>(result1);
        var resp1 = Assert.IsType<ApiResponse<object>>(badReq1.Value);
        Assert.Equal("INVALID_FILE", resp1.ErrorCode);

        // Empty file
        var mockFile = new Mock<IFormFile>();
        mockFile.SetupGet(f => f.Length).Returns(0);
        var result2 = await _controller.ImportBranchProducts(
            id,
            mockFile.Object,
            new BranchProductQueryFilter(),
            mockExcel.Object,
            CancellationToken.None);
        var badReq2 = Assert.IsType<BadRequestObjectResult>(result2);
        var resp2 = Assert.IsType<ApiResponse<object>>(badReq2.Value);
        Assert.Equal("INVALID_FILE", resp2.ErrorCode);
    }

    [Theory]
    [InlineData(0, "Import thành công 5/5 dòng.")]
    [InlineData(1, "Import xong: 4 thành công, 1 lỗi / 5 dòng.")]
    public async Task ImportBranchProducts_ValidFile_ReturnsOkWithAppropriateMessage(int failedCount, string expectedMsg)
    {
        var id = Guid.NewGuid();
        var mockExcel = new Mock<IBranchProductExcelService>();
        var mockFile = new Mock<IFormFile>();
        var content = Encoding.UTF8.GetBytes("fake data");
        var ms = new MemoryStream(content);
        mockFile.Setup(f => f.OpenReadStream()).Returns(ms);
        mockFile.SetupGet(f => f.Length).Returns(content.Length);
        mockFile.SetupGet(f => f.FileName).Returns("branches.xlsx");

        var importResult = new BranchProductExcelImportResultDto
        {
            Summary = new BranchProductExcelImportSummaryDto
            {
                TotalRows = 5,
                SuccessCount = 5 - failedCount,
                FailedCount = failedCount
            }
        };
        mockExcel.Setup(x => x.ImportAsync(
                id,
                It.IsAny<Stream>(),
                "branches.xlsx",
                It.IsAny<BranchProductQueryFilter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(importResult);

        var result = await _controller.ImportBranchProducts(
            id,
            mockFile.Object,
            new BranchProductQueryFilter(),
            mockExcel.Object,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchProductExcelImportResultDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(expectedMsg, resp.Message);
    }
}
