using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Repacks;
using MT_GRMS.Application.Repacks.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class RepackServiceTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-1";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool HasSaved { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            HasSaved = true;
            return Task.FromResult(1);
        }
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) => action(ct);
    }

    private readonly Mock<IRepackRepository> _repo;
    private readonly FakeTenantContext _tenant;
    private readonly FakeUnitOfWork _uow;
    private readonly RepackService _service;

    private readonly Guid _branchStockId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    public RepackServiceTests()
    {
        _repo = new Mock<IRepackRepository>();
        _tenant = new FakeTenantContext();
        _uow = new FakeUnitOfWork();
        _service = new RepackService(_repo.Object, _tenant, _uow);
    }

    private Product CreateWeightProduct(decimal sellingPrice = 50000m, decimal? processedPrice = 60000m)
    {
        return new Product
        {
            Id = _productId,
            TenantId = "tenant-test-1",
            ProductCode = "SP-DUAHOU",
            Name = "Dưa hấu Long An",
            BaseUnit = "kg",
            SellingPrice = sellingPrice,
            ProcessedSellingPricePerKg = processedPrice,
            CostPrice = 30000m,
            AllowRepack = true,
            HasBatches = true
        };
    }

    private BranchStock CreateBranchStock(decimal onHand = 50m)
    {
        return new BranchStock
        {
            Id = _branchStockId,
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = _productId,
            BatchCode = "LOT-202611-01",
            QuantityOnHand = onHand,
            QuantityReserved = 0,
            QuantityQuarantined = 0,
            LandedCostPerUnit = 30000m,
            ExpiryDate = DateTime.UtcNow.AddDays(7)
        };
    }

    [Fact]
    public async Task GetBranchStockPackages_StockNotFound_ThrowsBusinessException()
    {
        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BranchStock?)null);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.GetBranchStockPackagesAsync(_branchStockId));
        Assert.Equal("BRANCH_STOCK_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task GetBranchStockPackages_ProductNotFound_ThrowsBusinessException()
    {
        var stock = CreateBranchStock();
        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _repo.Setup(r => r.GetProductWithUnitsAsync("tenant-test-1", _productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.GetBranchStockPackagesAsync(_branchStockId));
        Assert.Equal("PRODUCT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task GetBranchStockPackages_Valid_ReturnsSummary()
    {
        var stock = CreateBranchStock(50m);
        var product = CreateWeightProduct();
        var packages = new List<Package>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                BranchStockId = _branchStockId,
                ProductId = _productId,
                InternalCode = "PKG-001",
                NetWeight = 2.5m,
                SellingPrice = 150000m,
                Status = "AVAILABLE"
            }
        };

        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _repo.Setup(r => r.GetProductWithUnitsAsync("tenant-test-1", _productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _repo.Setup(r => r.GetPackagesByBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(packages);

        var result = await _service.GetBranchStockPackagesAsync(_branchStockId);

        Assert.NotNull(result);
        Assert.Equal(_branchStockId, result.BranchStockId);
        Assert.Equal("Dưa hấu Long An", result.ProductName);
        Assert.True(result.IsWeightProduct);
        Assert.Equal(2.5m, result.PackagedQty);
        Assert.Equal(1, result.PackageCount);
        Assert.Single(result.Packages);
    }

    [Fact]
    public async Task CreateRepackOrder_NoPackages_ThrowsBusinessException()
    {
        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 10m,
            Packages: new List<PackageLineRequest>()
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("REPACK_NO_PACKAGES", ex.ErrorCode);
    }

    [Theory]
    [InlineData("INVALID_TYPE")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateRepackOrder_InvalidRepackType_ThrowsBusinessException(string? type)
    {
        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: type!,
            InputWeight: 10m,
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 10m, SellingPrice: 100000m)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("REPACK_TYPE_INVALID", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRepackOrder_NegativeOrZeroInputWeight_ThrowsBusinessException()
    {
        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 0,
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 1m, SellingPrice: 10000m)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("REPACK_INPUT_INVALID", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRepackOrder_WasteWithoutReason_ThrowsBusinessException()
    {
        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 10m,
            WasteWeight: 2m,
            WasteReason: null, // Missing!
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 8m, SellingPrice: 80000m)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("REPACK_WASTE_REASON_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRepackOrder_BalanceMismatch_ThrowsBusinessException()
    {
        // 8kg pkg + 1kg waste = 9kg != 10kg input
        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 10m,
            WasteWeight: 1m,
            WasteReason: "PEEL_SEED",
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 8m, SellingPrice: 80000m)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("REPACK_BALANCE_MISMATCH", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRepackOrder_NotWeightProduct_ThrowsBusinessException()
    {
        var stock = CreateBranchStock(50m);
        var product = new Product
        {
            Id = _productId,
            TenantId = "tenant-test-1",
            ProductCode = "SP-BIA",
            Name = "Bia lon",
            BaseUnit = "Lon", // Not kg!
            SellingPrice = 15000m
        };

        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _repo.Setup(r => r.GetProductWithUnitsAsync("tenant-test-1", _productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 10m,
            WasteWeight: 1m,
            WasteReason: "PEEL_SEED",
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 9m, SellingPrice: 90000m)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("NOT_WEIGHT_PRODUCT", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRepackOrder_InsufficientRawWeight_ThrowsBusinessException()
    {
        var stock = CreateBranchStock(5m); // Only 5kg on hand
        var product = CreateWeightProduct();

        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _repo.Setup(r => r.GetProductWithUnitsAsync("tenant-test-1", _productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _repo.Setup(r => r.GetActivePackageWeightAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 10m, // Takes 10kg > 5kg!
            WasteWeight: 1m,
            WasteReason: "PEEL_SEED",
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 9m, SellingPrice: 90000m)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateRepackOrderAsync(req));
        Assert.Equal("REPACK_INSUFFICIENT_RAW", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRepackOrder_Valid_SuccessfullyCreatesOrderAndPackages()
    {
        var stock = CreateBranchStock(50m);
        var product = CreateWeightProduct(sellingPrice: 50000m, processedPrice: 65000m);

        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _repo.Setup(r => r.GetProductWithUnitsAsync("tenant-test-1", _productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _repo.Setup(r => r.GetActivePackageWeightAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _repo.Setup(r => r.CountRepackOrdersTodayAsync("tenant-test-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repo.Setup(r => r.CountPackagesCreatedTodayAsync("tenant-test-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repo.Setup(r => r.AddRepackOrderAsync(It.IsAny<RepackOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddPackagesAsync(It.IsAny<IEnumerable<Package>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var req = new CreateRepackOrderRequest(
            BranchStockId: _branchStockId,
            RepackType: "PORTIONING",
            InputWeight: 10m,
            WasteWeight: 1m,
            WasteReason: "PEEL_SEED",
            Packages: new List<PackageLineRequest>
            {
                new(NetWeight: 5m, SellingPrice: 325000m, ExpiryDate: DateTime.UtcNow.AddDays(3), Barcode: null, ManufactureDate: DateTime.UtcNow),
                new(NetWeight: 4m, SellingPrice: 260000m, ExpiryDate: DateTime.UtcNow.AddDays(3), Barcode: null, ManufactureDate: DateTime.UtcNow)
            }
        );

        var result = await _service.CreateRepackOrderAsync(req);

        Assert.NotNull(result);
        Assert.Equal("PORTIONING", result.RepackType);
        Assert.Equal(10m, result.InputWeight);
        Assert.Equal(1m, result.WasteWeight);
        Assert.Equal("PEEL_SEED", result.WasteReason);
        Assert.Equal(2, result.Packages.Count);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task UpdatePackagePrice_NotFoundOrInvalidStatus_ThrowsBusinessException()
    {
        // Not found
        _repo.Setup(r => r.GetPackageByIdAsync("tenant-test-1", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Package?)null);

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdatePackagePriceAsync(Guid.NewGuid(), new UpdatePackagePriceRequest(100000m)));
        Assert.Equal("PACKAGE_NOT_FOUND", ex1.ErrorCode);

        // Invalid status (VOID)
        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Status = "VOID",
            NetWeight = 2m,
            SellingPrice = 50000m
        };
        _repo.Setup(r => r.GetPackageByIdAsync("tenant-test-1", pkg.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pkg);

        var ex2 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdatePackagePriceAsync(pkg.Id, new UpdatePackagePriceRequest(100000m)));
        Assert.Equal("PACKAGE_NOT_EDITABLE", ex2.ErrorCode);
    }

    [Fact]
    public async Task UpdatePackagePrice_Valid_UpdatesSalePrice()
    {
        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Status = "AVAILABLE",
            NetWeight = 2m,
            SellingPrice = 50000m
        };
        _repo.Setup(r => r.GetPackageByIdAsync("tenant-test-1", pkg.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pkg);

        var result = await _service.UpdatePackagePriceAsync(pkg.Id, new UpdatePackagePriceRequest(70000m));

        Assert.Equal(70000m, result.SellingPrice);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task QuarantineAndReleasePackage_Lifecycle()
    {
        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Status = "AVAILABLE",
            NetWeight = 2m
        };
        _repo.Setup(r => r.GetPackageByIdAsync("tenant-test-1", pkg.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pkg);

        // Quarantine
        var q = await _service.QuarantinePackageAsync(pkg.Id);
        Assert.Equal("QUARANTINED", q.Status);

        // Release
        var r = await _service.ReleasePackageAsync(pkg.Id);
        Assert.Equal("AVAILABLE", r.Status);
    }

    [Fact]
    public async Task VoidPackage_ValidationsAndSuccess()
    {
        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchStockId = _branchStockId,
            Status = "AVAILABLE",
            NetWeight = 2m
        };
        var stock = CreateBranchStock(50m);

        _repo.Setup(r => r.GetPackageByIdAsync("tenant-test-1", pkg.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pkg);
        _repo.Setup(r => r.GetBranchStockAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _repo.Setup(r => r.GetActivePackageWeightAsync("tenant-test-1", _branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2m);

        // Invalid mode
        var exMode = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.VoidPackageAsync(pkg.Id, new VoidPackageRequest("INVALID_MODE", "Lý do")));
        Assert.Equal("VOID_MODE_INVALID", exMode.ErrorCode);

        // Processing type cannot unpack to raw
        pkg.RepackType = "PROCESSING";
        var exProc = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.VoidPackageAsync(pkg.Id, new VoidPackageRequest("UNPACK_TO_RAW", "Mở khay")));
        Assert.Equal("PROCESSING_NO_UNPACK", exProc.ErrorCode);

        // Success WRITE_OFF for PORTIONING
        pkg.RepackType = "PORTIONING";
        var resultWriteOff = await _service.VoidPackageAsync(pkg.Id, new VoidPackageRequest("WRITE_OFF", "Hỏng"));
        Assert.Equal("VOID", resultWriteOff.Status);
        Assert.Equal("Hỏng", pkg.VoidReason);
        Assert.True(_uow.HasSaved);

        // Success UNPACK_TO_RAW
        pkg.Status = "AVAILABLE";
        var resultUnpack = await _service.VoidPackageAsync(pkg.Id, new VoidPackageRequest("UNPACK_TO_RAW", "Mở khay"));
        Assert.Equal("VOID", resultUnpack.Status);
    }
}
