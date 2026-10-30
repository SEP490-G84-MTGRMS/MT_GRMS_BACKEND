using Moq;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ProductUnitServiceTests
{
    private readonly Mock<IProductUnitRepository> _mockUnitRepo;
    private readonly Mock<IProductRepository> _mockProductRepo;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly ProductUnitService _service;

    public ProductUnitServiceTests()
    {
        _mockUnitRepo = new Mock<IProductUnitRepository>();
        _mockProductRepo = new Mock<IProductRepository>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();

        _service = new ProductUnitService(
            _mockUnitRepo.Object,
            _mockProductRepo.Object,
            _mockUnitOfWork.Object);
    }

    [Fact]
    public async Task GetUnitsByProductId_WhenProductNotFound_ThrowsKeyNotFoundException()
    {
        var productId = Guid.NewGuid();
        _mockProductRepo
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.GetUnitsByProductIdAsync(productId));
    }

    [Fact]
    public async Task GetUnitsByProductId_WhenProductExists_ReturnsUnitResponses()
    {
        var productId = Guid.NewGuid();
        var product = new Product { Id = productId, ProductName = "Heineken" };
        var units = new List<ProductUnit>
        {
            new() { Id = Guid.NewGuid(), ProductId = productId, UnitName = "Lon", ConversionRate = 1m, Price = 20000m, IsBaseUnit = true },
            new() { Id = Guid.NewGuid(), ProductId = productId, UnitName = "Thùng", ConversionRate = 24m, Price = 450000m, IsBaseUnit = false }
        };

        _mockProductRepo
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockUnitRepo
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(units);

        var result = await _service.GetUnitsByProductIdAsync(productId);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Lon", result[0].UnitName);
        Assert.True(result[0].IsBaseUnit);
        Assert.Equal("Thùng", result[1].UnitName);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNull()
    {
        var unitId = Guid.NewGuid();
        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductUnit?)null);

        var result = await _service.GetByIdAsync(unitId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetById_WhenExists_ReturnsUnitResponse()
    {
        var unitId = Guid.NewGuid();
        var unit = new ProductUnit
        {
            Id = unitId,
            ProductId = Guid.NewGuid(),
            UnitName = "Hộp",
            ConversionRate = 10m,
            Price = 150000m,
            Barcode = "893123456789"
        };

        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unit);

        var result = await _service.GetByIdAsync(unitId);

        Assert.NotNull(result);
        Assert.Equal("Hộp", result.UnitName);
        Assert.Equal(10m, result.ConversionRate);
        Assert.Equal("893123456789", result.Barcode);
    }

    [Theory]
    [InlineData("", 1, 100)]
    [InlineData("   ", 1, 100)]
    [InlineData("Thùng", 0, 100)]
    [InlineData("Thùng", -5, 100)]
    [InlineData("Thùng", 1, -10)]
    public async Task Create_WithInvalidInput_ThrowsArgumentException(string unitName, decimal rate, decimal price)
    {
        var request = new ProductUnitCreateRequest
        {
            UnitName = unitName,
            ConversionRate = rate,
            Price = price
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task Create_WhenProductNotFound_ThrowsKeyNotFoundException()
    {
        var productId = Guid.NewGuid();
        var request = new ProductUnitCreateRequest { UnitName = "Thùng", ConversionRate = 24m, Price = 100000m };

        _mockProductRepo
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.CreateAsync(productId, request));
    }

    [Fact]
    public async Task Create_WhenNameAlreadyExists_ThrowsInvalidOperationException()
    {
        var productId = Guid.NewGuid();
        var product = new Product { Id = productId };
        var request = new ProductUnitCreateRequest { UnitName = "Lon", ConversionRate = 1m, Price = 20000m };

        _mockProductRepo
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockUnitRepo
            .Setup(r => r.ExistsByNameAsync(productId, "Lon", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(productId, request));

        Assert.Contains("đã tồn tại", ex.Message);
    }

    [Fact]
    public async Task Create_WhenBarcodeAlreadyExists_ThrowsInvalidOperationException()
    {
        var productId = Guid.NewGuid();
        var product = new Product { Id = productId };
        var request = new ProductUnitCreateRequest
        {
            UnitName = "Thùng",
            ConversionRate = 24m,
            Price = 200000m,
            Barcode = "893999999999"
        };

        _mockProductRepo
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockUnitRepo
            .Setup(r => r.ExistsByNameAsync(productId, "Thùng", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockUnitRepo
            .Setup(r => r.ExistsByBarcodeAsync("893999999999", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(productId, request));

        Assert.Contains("Mã vạch", ex.Message);
    }

    [Fact]
    public async Task Create_WhenIsBaseUnitIsTrue_UpdatesOldBaseUnitAndProductBaseUnit()
    {
        var productId = Guid.NewGuid();
        var product = new Product { Id = productId, BaseUnit = "Chai" };
        var oldBaseUnit = new ProductUnit { Id = Guid.NewGuid(), ProductId = productId, UnitName = "Chai", IsBaseUnit = true };

        var request = new ProductUnitCreateRequest
        {
            UnitName = "Lon",
            ConversionRate = 5m, // Phải được tự động gán về 1.00m
            Price = 18000m,
            IsBaseUnit = true
        };

        _mockProductRepo
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _mockUnitRepo
            .Setup(r => r.ExistsByNameAsync(productId, "Lon", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockUnitRepo
            .Setup(r => r.GetBaseUnitByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldBaseUnit);

        var result = await _service.CreateAsync(productId, request);

        Assert.True(result.IsBaseUnit);
        Assert.Equal(1.00m, result.ConversionRate);
        Assert.False(oldBaseUnit.IsBaseUnit);
        Assert.Equal("Lon", product.BaseUnit);

        _mockUnitRepo.Verify(r => r.Update(oldBaseUnit), Times.Once);
        _mockProductRepo.Verify(r => r.Update(product), Times.Once);
        _mockUnitRepo.Verify(r => r.AddAsync(It.IsAny<ProductUnit>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WhenUnitNotFound_ThrowsKeyNotFoundException()
    {
        var unitId = Guid.NewGuid();
        var request = new ProductUnitUpdateRequest { UnitName = "Lốc", ConversionRate = 6m, Price = 60000m };

        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductUnit?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateAsync(unitId, request));
    }

    [Fact]
    public async Task Update_WhenNameConflictOnSameProduct_ThrowsInvalidOperationException()
    {
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unit = new ProductUnit { Id = unitId, ProductId = productId, UnitName = "Lốc 6" };
        var request = new ProductUnitUpdateRequest { UnitName = "Thùng 24", ConversionRate = 24m, Price = 240000m };

        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unit);

        _mockUnitRepo
            .Setup(r => r.ExistsByNameAsync(productId, "Thùng 24", unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(unitId, request));
    }

    [Fact]
    public async Task Delete_WhenUnitNotFound_ThrowsKeyNotFoundException()
    {
        var unitId = Guid.NewGuid();
        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductUnit?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.DeleteAsync(unitId));
    }

    [Fact]
    public async Task Delete_WhenOnlyBaseUnitRemaining_ThrowsInvalidOperationException()
    {
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unit = new ProductUnit { Id = unitId, ProductId = productId, UnitName = "Cái", IsBaseUnit = true };

        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unit);

        _mockUnitRepo
            .Setup(r => r.CountByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1); // Chỉ còn duy nhất 1 đơn vị

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(unitId));

        Assert.Contains("Không thể xóa đơn vị tính cơ bản duy nhất", ex.Message);
    }

    [Fact]
    public async Task Delete_WhenSecondaryUnit_DeletesSuccessfully()
    {
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unit = new ProductUnit { Id = unitId, ProductId = productId, UnitName = "Thùng", IsBaseUnit = false };

        _mockUnitRepo
            .Setup(r => r.GetByIdAsync(unitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unit);

        await _service.DeleteAsync(unitId);

        _mockUnitRepo.Verify(r => r.Delete(unit), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
