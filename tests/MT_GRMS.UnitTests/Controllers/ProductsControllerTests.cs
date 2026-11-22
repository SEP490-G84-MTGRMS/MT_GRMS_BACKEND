using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Exceptions;
using Npgsql;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class ProductsControllerTests
{
    private readonly Mock<ICatalogService> _mockCatalogService;
    private readonly Mock<ISupplierService> _mockSupplierService;
    private readonly ProductsController _controller;

    public ProductsControllerTests()
    {
        _mockCatalogService = new Mock<ICatalogService>();
        _mockSupplierService = new Mock<ISupplierService>();
        _controller = new ProductsController(_mockCatalogService.Object, _mockSupplierService.Object);
    }

    private static ProductResponse CreateSampleProductResponse(Guid id, string code = "P-01", string name = "Product 1")
    {
        return new ProductResponse(
            Id: id,
            ProductCode: code,
            Barcode: "BAR-" + code,
            Name: name,
            CategoryId: Guid.NewGuid(),
            CategoryName: "Category 1",
            BaseUnit: "Cái",
            SellingPrice: 1000m,
            CostPrice: 800m,
            MinStock: 0,
            MaxStock: 100,
            VatRate: 10,
            ProductType: "STANDARD",
            Status: "Active",
            ImageUrl: null,
            Description: null,
            InternalNote: null,
            HasBatches: false,
            IsFavorite: false,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        );
    }

    private static DbUpdateException CreatePostgresDbUpdateException(string sqlState, string constraintName)
    {
        var pgEx = (PostgresException)RuntimeHelpers.GetUninitializedObject(typeof(PostgresException));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (var field in typeof(PostgresException).GetFields(flags))
        {
            if (field.Name.Contains("sqlState", StringComparison.OrdinalIgnoreCase) || field.Name.Contains("SqlState"))
            {
                field.SetValue(pgEx, sqlState);
            }
            else if (field.Name.Contains("constraint", StringComparison.OrdinalIgnoreCase))
            {
                field.SetValue(pgEx, constraintName);
            }
        }
        return new DbUpdateException("DB Error", pgEx);
    }

    [Fact]
    public async Task GetProducts_ReturnsOk()
    {
        var filter = new ProductQueryFilter();
        var paged = new PagedResult<ProductResponse>
        {
            Items = new List<ProductResponse> { CreateSampleProductResponse(Guid.NewGuid()) },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockCatalogService.Setup(s => s.GetProductsPagedAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetProducts(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<ProductResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
    }

    [Fact]
    public async Task GetById_WhenFound_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var prod = CreateSampleProductResponse(id);
        _mockCatalogService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prod);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<ProductResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _mockCatalogService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductResponse?)null);

        var result = await _controller.GetById(id, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task GetByBarcode_WhenFound_ReturnsOk()
    {
        var prod = CreateSampleProductResponse(Guid.NewGuid());
        _mockCatalogService.Setup(s => s.GetByBarcodeAsync("BAR-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(prod);

        var result = await _controller.GetByBarcode("BAR-01", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<ProductResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetByBarcode_WhenNotFound_ReturnsNotFound()
    {
        _mockCatalogService.Setup(s => s.GetByBarcodeAsync("NOT_EXIST", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductResponse?)null);

        var result = await _controller.GetByBarcode("NOT_EXIST", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task Create_HappyPath_ReturnsCreatedAtAction()
    {
        var request = new ProductCreateRequest("New Prod", Guid.NewGuid(), 1000m);
        var created = CreateSampleProductResponse(Guid.NewGuid(), "NEW", "New Prod");
        _mockCatalogService.Setup(s => s.CreateProductAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ProductsController.GetById), createdResult.ActionName);
        Assert.Equal(created.Id, createdResult.RouteValues!["id"]);
    }

    [Theory]
    [InlineData("IX_Product_ProductCode", "DUPLICATE_CODE")]
    [InlineData("IX_Product_Barcode", "DUPLICATE_BARCODE")]
    [InlineData("IX_Product_Other", "DUPLICATE_KEY")]
    public async Task Create_DbUpdateExceptionUniqueViolation_ReturnsConflict(string constraint, string expectedCode)
    {
        var request = new ProductCreateRequest("Prod", Guid.NewGuid(), 1000m);
        var dbEx = CreatePostgresDbUpdateException("23505", constraint);
        _mockCatalogService.Setup(s => s.CreateProductAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbEx);

        var result = await _controller.Create(request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(conflict.Value);
        Assert.Equal(expectedCode, resp.ErrorCode);
    }

    [Theory]
    [InlineData("DUPLICATE_ITEM", 409)]
    [InlineData("VALIDATION_ERROR", 422)]
    public async Task Create_BusinessException_ReturnsAppropriateStatusCode(string errorCode, int expectedStatus)
    {
        var request = new ProductCreateRequest("Prod", Guid.NewGuid(), 1000m);
        _mockCatalogService.Setup(s => s.CreateProductAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException(errorCode, "Business error"));

        var result = await _controller.Create(request, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objResult.StatusCode);
    }

    [Fact]
    public async Task Create_ArgumentException_ReturnsBadRequest()
    {
        var request = new ProductCreateRequest("Prod", Guid.NewGuid(), 1000m);
        _mockCatalogService.Setup(s => s.CreateProductAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid argument"));

        var result = await _controller.Create(request, CancellationToken.None);

        var badReq = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(badReq.Value);
        Assert.Equal("BAD_REQUEST", resp.ErrorCode);
    }

    [Fact]
    public async Task Update_HappyPath_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new ProductUpdateRequest("Updated Prod", Guid.NewGuid(), 2000m);
        var updated = CreateSampleProductResponse(id, "P-01", "Updated Prod");
        _mockCatalogService.Setup(s => s.UpdateProductAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await _controller.Update(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<ProductResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Updated Prod", resp.Data!.Name);
    }

    [Theory]
    [InlineData("IX_Product_ProductCode", "DUPLICATE_CODE")]
    [InlineData("IX_Product_Barcode", "DUPLICATE_BARCODE")]
    [InlineData("IX_Product_Other", "DUPLICATE_KEY")]
    public async Task Update_DbUpdateExceptionUniqueViolation_ReturnsConflict(string constraint, string expectedCode)
    {
        var id = Guid.NewGuid();
        var request = new ProductUpdateRequest("Prod", Guid.NewGuid(), 1000m);
        var dbEx = CreatePostgresDbUpdateException("23505", constraint);
        _mockCatalogService.Setup(s => s.UpdateProductAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbEx);

        var result = await _controller.Update(id, request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(conflict.Value);
        Assert.Equal(expectedCode, resp.ErrorCode);
    }

    [Fact]
    public async Task Update_NotFoundException_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        var request = new ProductUpdateRequest("Prod", Guid.NewGuid(), 1000m);
        _mockCatalogService.Setup(s => s.UpdateProductAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Product not found"));

        var result = await _controller.Update(id, request, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(notFound.Value);
        Assert.Equal("NOT_FOUND", resp.ErrorCode);
    }

    [Theory]
    [InlineData("DUPLICATE_NAME", 409)]
    [InlineData("VALIDATION_ERROR", 422)]
    public async Task Update_BusinessException_ReturnsAppropriateStatusCode(string errorCode, int expectedStatus)
    {
        var id = Guid.NewGuid();
        var request = new ProductUpdateRequest("Prod", Guid.NewGuid(), 1000m);
        _mockCatalogService.Setup(s => s.UpdateProductAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException(errorCode, "Business error"));

        var result = await _controller.Update(id, request, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objResult.StatusCode);
    }

    [Fact]
    public async Task Update_ArgumentException_ReturnsBadRequest()
    {
        var id = Guid.NewGuid();
        var request = new ProductUpdateRequest("Prod", Guid.NewGuid(), 1000m);
        _mockCatalogService.Setup(s => s.UpdateProductAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Bad argument"));

        var result = await _controller.Update(id, request, CancellationToken.None);

        var badReq = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(badReq.Value);
        Assert.Equal("BAD_REQUEST", resp.ErrorCode);
    }

    [Fact]
    public async Task Delete_HappyPath_ReturnsOk()
    {
        var id = Guid.NewGuid();
        _mockCatalogService.Setup(s => s.DeleteProductAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task Delete_NotFoundException_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _mockCatalogService.Setup(s => s.DeleteProductAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));

        var result = await _controller.Delete(id, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(notFound.Value);
        Assert.Equal("NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task Delete_BusinessException_ReturnsBadRequest()
    {
        var id = Guid.NewGuid();
        _mockCatalogService.Setup(s => s.DeleteProductAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("CANNOT_DELETE", "Cannot delete"));

        var result = await _controller.Delete(id, CancellationToken.None);

        var badReq = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(badReq.Value);
        Assert.Equal("CANNOT_DELETE", resp.ErrorCode);
    }

    [Fact]
    public async Task GetSuppliersForProduct_ReturnsOk()
    {
        var id = Guid.NewGuid();
        IReadOnlyList<ProductSupplierMappingDto> list = new List<ProductSupplierMappingDto>
        {
            new() { SupplierId = Guid.NewGuid(), SupplierName = "Supplier A" }
        };
        _mockSupplierService.Setup(s => s.GetSuppliersForProductAsync(id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetSuppliersForProduct(id, false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<ProductSupplierMappingDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetPriceHistory_ReturnsOk()
    {
        var id = Guid.NewGuid();
        IReadOnlyList<ProductPriceHistoryDto> list = new List<ProductPriceHistoryDto>
        {
            new(Guid.NewGuid(), id, null, null, null, null, "MASTER", "SELLING", 1000m, 1200m, 800m, false, null, null, "USER-1", "Admin", "Reason", DateTime.UtcNow)
        };
        var query = new ProductPriceHistoryQuery();
        _mockCatalogService.Setup(s => s.GetPriceHistoryAsync(id, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProductPriceHistoryDto> { Items = list, Page = 1, PageSize = 15, TotalItems = 1 });

        var result = await _controller.GetPriceHistory(id, query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<ProductPriceHistoryDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
    }
}
