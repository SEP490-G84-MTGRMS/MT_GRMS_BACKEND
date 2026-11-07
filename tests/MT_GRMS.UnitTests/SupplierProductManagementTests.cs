using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SupplierProductManagementTests
{
    private readonly FakeSupplierRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly SupplierService _service;

    public SupplierProductManagementTests()
    {
        _repo = new FakeSupplierRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new SupplierService(_repo, _uow, _tenantContext);
    }

    [Fact]
    public async Task REQ_SP01_AssignProduct_ValidData_Succeeds()
    {
        // Arrange
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa Tươi Tiệt Trùng 1L", "89312345601", "PRD-MILK-1L");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var command = new AssignSupplierProductCommand
        {
            ProductId = product.Id,
            DeliveryUoM = "Thùng",
            AgreedWholesalePrice = 280000m,
            MinimumOrderQuantity = 5,
            IsPrimarySupplier = true,
            SupplierProductCode = "VNM-TH-1L"
        };

        // Act
        var result = await _service.AssignSupplierProductAsync(supplier.Id, command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(supplier.Id, result.SupplierId);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Equal("Thùng", result.DeliveryUoM);
        Assert.Equal(280000m, result.AgreedWholesalePrice);
        Assert.Equal(5, result.MinimumOrderQuantity);
        Assert.True(result.IsPrimarySupplier);
        Assert.Equal("Active", result.Status);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_SP01_AssignProduct_AutomaticallySyncsToLinkedActiveBranches()
    {
        // Arrange
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa Tươi Tiệt Trùng 1L", "89312345601", "PRD-MILK-1L");
        var branch1Id = Guid.NewGuid();
        var branch2Id = Guid.NewGuid();

        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);
        _repo.BranchAssignments.Add(new SupplierBranchAssignment
        {
            SupplierId = supplier.Id,
            BranchId = branch1Id,
            Status = SupplierStatus.Active
        });
        _repo.BranchAssignments.Add(new SupplierBranchAssignment
        {
            SupplierId = supplier.Id,
            BranchId = branch2Id,
            Status = SupplierStatus.Active
        });

        var command = new AssignSupplierProductCommand
        {
            ProductId = product.Id,
            DeliveryUoM = "Thùng",
            AgreedWholesalePrice = 280000m,
            MinimumOrderQuantity = 5,
            IsPrimarySupplier = false,
            SupplierProductCode = "VNM-TH-1L"
        };

        // Act
        var result = await _service.AssignSupplierProductAsync(supplier.Id, command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.SyncedBranchesCount);
        Assert.Equal(2, _repo.BranchStocks.Count);
        Assert.Contains(_repo.BranchStocks, bs => bs.BranchId == branch1Id && bs.ProductId == product.Id);
        Assert.Contains(_repo.BranchStocks, bs => bs.BranchId == branch2Id && bs.ProductId == product.Id);
    }

    [Fact]
    public async Task REQ_SP01_BatchAssignProducts_AssignsMultipleAndSyncsBranches()
    {
        // Arrange
        var supplier = CreateTestSupplier("Unilever", "NCC-UNI-001");
        var product1 = CreateTestProduct("Dầu gội Clear", "893111111", "PRD-CLEAR");
        var product2 = CreateTestProduct("Bột giặt OMO", "893222222", "PRD-OMO");
        var branchId = Guid.NewGuid();

        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product1);
        _repo.Products.Add(product2);
        _repo.BranchAssignments.Add(new SupplierBranchAssignment
        {
            SupplierId = supplier.Id,
            BranchId = branchId,
            Status = SupplierStatus.Active
        });

        var command = new BatchAssignSupplierProductsCommand
        {
            ProductIds = new List<Guid> { product1.Id, product2.Id },
            AgreedWholesalePrice = 50000m,
            MinimumOrderQuantity = 2
        };

        // Act
        var result = await _service.BatchAssignSupplierProductsAsync(supplier.Id, command);

        // Assert
        Assert.Equal(2, result.AssignedCount);
        Assert.Equal(1, result.SyncedBranchesCount);
        Assert.Equal(2, _repo.SupplierProducts.Count);
        Assert.Equal(2, _repo.BranchStocks.Count);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_SP01_AssignProduct_DuplicateAssignment_ThrowsConflictException()
    {
        // Arrange (BR-SP01)
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa Tươi Tiệt Trùng 1L", "89312345601", "PRD-MILK-1L");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var existing = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            ProductId = product.Id,
            DeliveryUoM = "Thùng",
            AgreedWholesalePrice = 280000m,
            MinimumOrderQuantity = 5,
            IsPrimarySupplier = true,
            Status = SupplierProductStatus.Active
        };
        _repo.SupplierProducts.Add(existing);

        var command = new AssignSupplierProductCommand
        {
            ProductId = product.Id,
            DeliveryUoM = "Thùng",
            AgreedWholesalePrice = 280000m,
            MinimumOrderQuantity = 5
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignSupplierProductAsync(supplier.Id, command));

        Assert.Equal("PRODUCT_ALREADY_ASSIGNED_TO_SUPPLIER", ex.ErrorCode);
    }

    [Theory]
    [InlineData(-500, 5)]  // Giá âm
    [InlineData(10000, 0)] // MOQ < 1
    [InlineData(10000, -1)]// MOQ âm
    public async Task REQ_SP01_AssignProduct_InvalidPriceOrMOQ_ThrowsValidationException(decimal price, int moq)
    {
        // Arrange (BR-SP03)
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa Chua 100g", "89312345602", "PRD-YOGURT");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var command = new AssignSupplierProductCommand
        {
            ProductId = product.Id,
            DeliveryUoM = "Hộp",
            AgreedWholesalePrice = price,
            MinimumOrderQuantity = moq
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignSupplierProductAsync(supplier.Id, command));

        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_SP02_UpdateSupplierProduct_PriceAndMOQ_Succeeds()
    {
        // Arrange
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Cà Chua Đà Lạt", "893999901", "PRD-TOMATO");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var existing = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Supplier = supplier,
            ProductId = product.Id,
            Product = product,
            DeliveryUoM = "Kg",
            AgreedWholesalePrice = 18000m,
            MinimumOrderQuantity = 10,
            IsPrimarySupplier = false,
            Status = SupplierProductStatus.Active
        };
        _repo.SupplierProducts.Add(existing);

        var updateCmd = new UpdateSupplierProductCommand
        {
            DeliveryUoM = "Thùng 10Kg",
            AgreedWholesalePrice = 20000m,
            MinimumOrderQuantity = 15,
            IsPrimarySupplier = false,
            SupplierProductCode = "TOMATO-10K"
        };

        // Act
        var result = await _service.UpdateSupplierProductAsync(supplier.Id, product.Id, updateCmd);

        // Assert
        Assert.Equal(20000m, result.AgreedWholesalePrice);
        Assert.Equal(15, result.MinimumOrderQuantity);
        Assert.Equal("Thùng 10Kg", result.DeliveryUoM);
        Assert.Equal("TOMATO-10K", result.SupplierProductCode);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_SP03_ToggleSupplierProductStatus_SoftDeactivateAndReactivate_Succeeds()
    {
        // Arrange
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Bơ Booth 7", "893999902", "PRD-AVOCADO");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var item = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Supplier = supplier,
            ProductId = product.Id,
            Product = product,
            DeliveryUoM = "Kg",
            AgreedWholesalePrice = 45000m,
            MinimumOrderQuantity = 10,
            Status = SupplierProductStatus.Active
        };
        _repo.SupplierProducts.Add(item);

        // Act 1: Soft Deactivate
        var deactivated = await _service.ToggleSupplierProductStatusAsync(supplier.Id, product.Id,
            new ToggleSupplierProductStatusRequest { Status = SupplierProductStatus.Inactive });

        // Assert 1
        Assert.Equal("Inactive", deactivated.Status);
        Assert.Equal(SupplierProductStatus.Inactive, item.Status);

        // Act 2: Reactivate
        var reactivated = await _service.ToggleSupplierProductStatusAsync(supplier.Id, product.Id,
            new ToggleSupplierProductStatusRequest { Status = SupplierProductStatus.Active });

        // Assert 2
        Assert.Equal("Active", reactivated.Status);
        Assert.Equal(SupplierProductStatus.Active, item.Status);
    }

    [Fact]
    public async Task REQ_SP04_SetPrimarySupplier_ExclusivityRule_ClearsPreviousPrimarySupplier()
    {
        // Arrange (BR-SP02)
        var product = CreateTestProduct("Coca Cola 330ml", "893500123", "PRD-COCA-330");
        var suppA = CreateTestSupplier("Đại lý Nước ngọt A", "NCC-BEV-A");
        var suppB = CreateTestSupplier("Đại lý Nước ngọt B", "NCC-BEV-B");
        _repo.Products.Add(product);
        _repo.Suppliers.Add(suppA);
        _repo.Suppliers.Add(suppB);

        var itemA = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = suppA.Id,
            Supplier = suppA,
            ProductId = product.Id,
            Product = product,
            AgreedWholesalePrice = 8500m,
            MinimumOrderQuantity = 24,
            IsPrimarySupplier = true, // Ban đầu A là Primary
            Status = SupplierProductStatus.Active
        };
        var itemB = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = suppB.Id,
            Supplier = suppB,
            ProductId = product.Id,
            Product = product,
            AgreedWholesalePrice = 8400m,
            MinimumOrderQuantity = 48,
            IsPrimarySupplier = false, // Ban đầu B không là Primary
            Status = SupplierProductStatus.Active
        };
        _repo.SupplierProducts.Add(itemA);
        _repo.SupplierProducts.Add(itemB);

        // Act: Đổi B thành Primary Supplier
        var resultB = await _service.SetPrimarySupplierAsync(suppB.Id, product.Id,
            new SetPrimarySupplierRequest { IsPrimarySupplier = true });

        // Assert: B thành Primary, A tự động bị gỡ cờ Primary!
        Assert.True(resultB.IsPrimarySupplier);
        Assert.True(itemB.IsPrimarySupplier);
        Assert.False(itemA.IsPrimarySupplier);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_SP05_GetSuppliersForProduct_ReverseLookup_ReturnsOrderedListWithPrimaryFirst()
    {
        // Arrange
        var product = CreateTestProduct("Gạo ST25 5Kg", "893888001", "PRD-RICE-ST25");
        var suppX = CreateTestSupplier("Kho gạo X", "NCC-RICE-X");
        var suppY = CreateTestSupplier("Kho gạo Y", "NCC-RICE-Y");
        _repo.Products.Add(product);
        _repo.Suppliers.Add(suppX);
        _repo.Suppliers.Add(suppY);

        var itemX = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = suppX.Id,
            Supplier = suppX,
            ProductId = product.Id,
            Product = product,
            AgreedWholesalePrice = 180000m,
            MinimumOrderQuantity = 10,
            IsPrimarySupplier = true,
            Status = SupplierProductStatus.Active
        };
        var itemY = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = suppY.Id,
            Supplier = suppY,
            ProductId = product.Id,
            Product = product,
            AgreedWholesalePrice = 185000m,
            MinimumOrderQuantity = 5,
            IsPrimarySupplier = false,
            Status = SupplierProductStatus.Active
        };
        _repo.SupplierProducts.Add(itemX);
        _repo.SupplierProducts.Add(itemY);

        // Act
        var result = await _service.GetSuppliersForProductAsync(product.Id, onlyActive: true);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.True(result.First().IsPrimarySupplier);
        Assert.Equal(suppX.Id, result.First().SupplierId);
    }

    [Fact]
    public async Task REQ_SP01_AssignProduct_AgreedPriceZero_UsesProductCostPrice()
    {
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa Chua 100g", "89312345602", "PRD-YOGURT");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var result = await _service.AssignSupplierProductAsync(supplier.Id, new AssignSupplierProductCommand
        {
            ProductId = product.Id,
            AgreedWholesalePrice = 0m,
            MinimumOrderQuantity = 3
        });

        // Giá 0 từ client → backend mặc định lưu giá Master (CostPrice)
        Assert.Equal(8000m, result.AgreedWholesalePrice);
        Assert.Equal(8000m, result.EffectivePurchasePrice);
        Assert.Equal(3, result.MinimumOrderQuantity);
    }

    [Fact]
    public async Task REQ_SP01_AssignProduct_AgreedPriceZeroWithoutCost_Throws()
    {
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa Chua 100g", "89312345602", "PRD-YOGURT");
        product.CostPrice = null;
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignSupplierProductAsync(supplier.Id, new AssignSupplierProductCommand
            {
                ProductId = product.Id,
                AgreedWholesalePrice = 0m,
                MinimumOrderQuantity = 3
            }));

        Assert.Equal("INVALID_PURCHASE_PRICE", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_SP01_BatchAssign_PerProductTerms_KeepsIndependentPrices()
    {
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product1 = CreateTestProduct("Sữa tươi", "893111", "PRD-MILK");
        var product2 = CreateTestProduct("Sữa chua", "893222", "PRD-YOG");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product1);
        _repo.Products.Add(product2);

        var result = await _service.BatchAssignSupplierProductsAsync(supplier.Id, new BatchAssignSupplierProductsCommand
        {
            Items = new List<BatchAssignSupplierProductItemCommand>
            {
                new() { ProductId = product1.Id, AgreedWholesalePrice = 100000m, MinimumOrderQuantity = 10 },
                new() { ProductId = product2.Id, AgreedWholesalePrice = 55000m, MinimumOrderQuantity = 8 }
            }
        });

        Assert.Equal(2, result.AssignedCount);
        Assert.Equal(100000m, result.Items.First(x => x.ProductId == product1.Id).AgreedWholesalePrice);
        Assert.Equal(10, result.Items.First(x => x.ProductId == product1.Id).MinimumOrderQuantity);
        Assert.Equal(55000m, result.Items.First(x => x.ProductId == product2.Id).AgreedWholesalePrice);
        Assert.Equal(8, result.Items.First(x => x.ProductId == product2.Id).MinimumOrderQuantity);
    }

    [Fact]
    public async Task REQ_SP01_BatchAssign_SkipsExistingWithoutOverwritingTerms()
    {
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var existingProduct = CreateTestProduct("Sữa tươi", "893111", "PRD-MILK");
        var newProduct = CreateTestProduct("Sữa chua", "893222", "PRD-YOG");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(existingProduct);
        _repo.Products.Add(newProduct);

        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Supplier = supplier,
            ProductId = existingProduct.Id,
            Product = existingProduct,
            DeliveryUoM = existingProduct.BaseUnit,
            AgreedWholesalePrice = 99000m,
            MinimumOrderQuantity = 7,
            Status = SupplierProductStatus.Active
        });

        var result = await _service.BatchAssignSupplierProductsAsync(supplier.Id, new BatchAssignSupplierProductsCommand
        {
            Items = new List<BatchAssignSupplierProductItemCommand>
            {
                new() { ProductId = existingProduct.Id, AgreedWholesalePrice = 1m, MinimumOrderQuantity = 1 },
                new() { ProductId = newProduct.Id, AgreedWholesalePrice = 55000m, MinimumOrderQuantity = 8 }
            }
        });

        Assert.Equal(1, result.AssignedCount);
        Assert.Equal(2, _repo.SupplierProducts.Count);

        var kept = _repo.SupplierProducts.Single(x => x.ProductId == existingProduct.Id);
        Assert.Equal(99000m, kept.AgreedWholesalePrice);
        Assert.Equal(7, kept.MinimumOrderQuantity);

        var created = _repo.SupplierProducts.Single(x => x.ProductId == newProduct.Id);
        Assert.Equal(55000m, created.AgreedWholesalePrice);
        Assert.Equal(8, created.MinimumOrderQuantity);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1000, 0)]
    [InlineData(1000, -2)]
    public async Task REQ_SP01_BatchAssign_InvalidPriceOrMoq_ThrowsValidation(decimal price, int moq)
    {
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        var product = CreateTestProduct("Sữa tươi", "893111", "PRD-MILK");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.BatchAssignSupplierProductsAsync(supplier.Id, new BatchAssignSupplierProductsCommand
            {
                Items = new List<BatchAssignSupplierProductItemCommand>
                {
                    new() { ProductId = product.Id, AgreedWholesalePrice = price, MinimumOrderQuantity = moq }
                }
            }));

        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_SP05_GetSupplierProducts_AndFilters_NeverLeakOtherSupplier()
    {
        var supplierA = CreateTestSupplier("NCC A", "SUP-A-0001");
        var supplierB = CreateTestSupplier("NCC B", "SUP-B-0002");
        var productA = CreateTestProduct("Cà chua", "893aaa", "PRD-TOM");
        var productB = CreateTestProduct("Cà rốt", "893bbb", "PRD-CAR");
        _repo.Suppliers.AddRange(new[] { supplierA, supplierB });
        _repo.Products.AddRange(new[] { productA, productB });
        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierA.Id,
            Supplier = supplierA,
            ProductId = productA.Id,
            Product = productA,
            AgreedWholesalePrice = 100000m,
            MinimumOrderQuantity = 10,
            Status = SupplierProductStatus.Active
        });
        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierB.Id,
            Supplier = supplierB,
            ProductId = productB.Id,
            Product = productB,
            AgreedWholesalePrice = 20000m,
            MinimumOrderQuantity = 5,
            Status = SupplierProductStatus.Active
        });

        var result = await _service.GetSupplierProductsAsync(Guid.Empty, new SupplierProductQueryFilter
        {
            Names = ["Cà"],
            SupplierCodes = ["SUP-A-0001"],
            PageIndex = 1,
            PageSize = 15
        });

        Assert.Single(result.Items);
        Assert.Equal(supplierA.Id, result.Items[0].SupplierId);
        Assert.DoesNotContain(result.Items, x => x.SupplierId == supplierB.Id);
    }

    [Fact]
    public async Task REQ_SP03_ToggleActive_WhenSupplierInactive_Throws()
    {
        var supplier = CreateTestSupplier("Vinamilk", "NCC-VNM-001");
        supplier.Status = SupplierStatus.Inactive;
        var product = CreateTestProduct("Sữa", "8931", "PRD-1");
        _repo.Suppliers.Add(supplier);
        _repo.Products.Add(product);
        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Supplier = supplier,
            ProductId = product.Id,
            Product = product,
            Status = SupplierProductStatus.Inactive,
            AgreedWholesalePrice = 10000m,
            MinimumOrderQuantity = 1
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ToggleSupplierProductStatusAsync(supplier.Id, product.Id,
                new ToggleSupplierProductStatusRequest { Status = SupplierProductStatus.Active }));

        Assert.Equal("SUPPLIER_INACTIVE", ex.ErrorCode);
    }

    // Helper methods
    private static Supplier CreateTestSupplier(string name, string code)
    {
        return new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Name = name,
            Code = code,
            ContactPerson = "Nguyễn Văn Test",
            Phone = "0901234567",
            Status = SupplierStatus.Active
        };
    }

    private static Product CreateTestProduct(string name, string barcode, string sku)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            ProductName = name,
            Barcode = barcode,
            SKU = sku,
            BasePrice = 10000m,
            CostPrice = 8000m,
            UnitName = "Cái",
            IsActive = true
        };
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool HasSaved { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            HasSaved = true;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-1";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;

        public FakeTenantContext(string tenantId) => CurrentTenantId = tenantId;
    }


    private class FakeSupplierRepository : ISupplierRepository
    {
        public List<Supplier> Suppliers { get; } = new();
        public List<SupplierProduct> SupplierProducts { get; } = new();
        public List<Product> Products { get; } = new();
        public List<SupplierAPLedger> Ledgers { get; } = new();
        public List<SupplierBlackoutDate> BlackoutDates { get; } = new();

        public Task AddAsync(Supplier entity, CancellationToken cancellationToken = default)
        {
            Suppliers.Add(entity);
            return Task.CompletedTask;
        }

        public void Delete(Supplier entity) => entity.IsDeleted = true;
        public void Update(Supplier entity) { }

        public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.FirstOrDefault(s => s.Id == id && !s.IsDeleted));
        }

        public Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Supplier>)Suppliers.Where(s => !s.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<Supplier>> FindAsync(Expression<Func<Supplier, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult((IReadOnlyList<Supplier>)Suppliers.Where(compiled).ToList());
        }

        public Task<Supplier?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.FirstOrDefault(s => s.Id == id && !s.IsDeleted));
        }

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.Any(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !s.IsDeleted));
        }

        public Task<int> CountTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(Suppliers.Count);

        public Task<(IReadOnlyList<Supplier> Items, int TotalCount)> GetPagedListAsync(
            string? searchTerm, SupplierType? supplierType, SupplierStatus? status, Guid? categoryId, int pageIndex, int pageSize,
            string? code = null, string? name = null, string? contactPerson = null, string? phone = null,
            List<Guid>? supplierIds = null, List<Guid>? productIds = null, List<string>? contactPersons = null, List<string>? phones = null,
            List<string>? codes = null, List<string>? names = null,
            string? category = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(((IReadOnlyList<Supplier>)Suppliers, Suppliers.Count));
        }

        public Task<SupplierSearchSuggestionsDto> GetSearchSuggestionsAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SupplierSearchSuggestionsDto());
        }

        public Task<bool> HasLedgerTransactionsAsync(Guid supplierId, APDocType docType, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<SupplierAPLedger?> GetLedgerByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult<SupplierAPLedger?>(null);
        public Task AddLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(IReadOnlyList<SupplierAPLedger> Items, int TotalCount)> GetPagedLedgerAsync(Guid supplierId, DateTime? fromDate, DateTime? toDate, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(((IReadOnlyList<SupplierAPLedger>)Ledgers, Ledgers.Count));
        }

        public Task<IReadOnlyList<SupplierBlackoutDate>> GetBlackoutDatesAsync(Guid supplierId, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<SupplierBlackoutDate>)BlackoutDates);
        public Task ReplaceBlackoutDatesAsync(Guid supplierId, IEnumerable<SupplierBlackoutDate> blackoutDates, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SupplierProduct?> GetSupplierProductAsync(Guid supplierId, Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SupplierProducts.FirstOrDefault(sp => sp.SupplierId == supplierId && sp.ProductId == productId));
        }

        public Task<(IReadOnlyList<SupplierProduct> Items, int TotalCount)> GetPagedSupplierProductsAsync(
            Guid supplierId, string? searchTerm, Guid? categoryId, SupplierProductStatus? status, int pageIndex, int pageSize, string? supplierSearch = null, string? categoryName = null,
            IReadOnlyList<string>? skus = null, IReadOnlyList<string>? names = null, IReadOnlyList<string>? supplierCodes = null, IReadOnlyList<string>? supplierNames = null,
            CancellationToken cancellationToken = default)
        {
            var query = SupplierProducts.AsEnumerable();
            if (supplierId != Guid.Empty) query = query.Where(sp => sp.SupplierId == supplierId);
            if (status.HasValue) query = query.Where(sp => sp.Status == status.Value);

            static List<string> Clean(IReadOnlyList<string>? values) =>
                (values ?? Array.Empty<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();

            var skuTerms = Clean(skus);
            if (skuTerms.Count > 0)
            {
                query = query.Where(sp => skuTerms.Any(term => (sp.Product?.SKU ?? string.Empty).ToLowerInvariant().Contains(term)));
            }

            var nameTerms = Clean(names);
            if (nameTerms.Count > 0)
            {
                query = query.Where(sp => nameTerms.Any(term => (sp.Product?.Name ?? string.Empty).ToLowerInvariant().Contains(term)));
            }
            else if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLowerInvariant();
                query = query.Where(sp =>
                    (sp.Product?.Name ?? string.Empty).ToLowerInvariant().Contains(term) ||
                    (sp.Product?.SKU ?? string.Empty).ToLowerInvariant().Contains(term) ||
                    (sp.Product?.Barcode ?? string.Empty).ToLowerInvariant().Contains(term));
            }

            var supplierCodeTerms = Clean(supplierCodes);
            if (supplierCodeTerms.Count > 0)
            {
                query = query.Where(sp => supplierCodeTerms.Any(term => (sp.Supplier?.Code ?? string.Empty).ToLowerInvariant().Contains(term)));
            }

            var supplierNameTerms = Clean(supplierNames);
            if (supplierNameTerms.Count > 0)
            {
                query = query.Where(sp => supplierNameTerms.Any(term => (sp.Supplier?.Name ?? string.Empty).ToLowerInvariant().Contains(term)));
            }
            else if (!string.IsNullOrWhiteSpace(supplierSearch) && supplierCodeTerms.Count == 0)
            {
                var term = supplierSearch.Trim().ToLowerInvariant();
                query = query.Where(sp =>
                    (sp.Supplier?.Code ?? string.Empty).ToLowerInvariant().Contains(term) ||
                    (sp.Supplier?.Name ?? string.Empty).ToLowerInvariant().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(categoryName) && !string.Equals(categoryName, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                var term = categoryName.Trim().ToLowerInvariant();
                query = query.Where(sp => (sp.Product?.Category?.Name ?? string.Empty).ToLowerInvariant().Contains(term));
            }
            var list = query.ToList();
            var paged = list.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<SupplierProduct>)paged, list.Count));
        }

        public Task AddSupplierProductAsync(SupplierProduct supplierProduct, CancellationToken cancellationToken = default)
        {
            SupplierProducts.Add(supplierProduct);
            return Task.CompletedTask;
        }

        public Task ClearPrimarySupplierFlagAsync(Guid productId, Guid? excludeSupplierId = null, CancellationToken cancellationToken = default)
        {
            var primaries = SupplierProducts.Where(sp => sp.ProductId == productId && sp.IsPrimarySupplier);
            if (excludeSupplierId.HasValue && excludeSupplierId != Guid.Empty)
            {
                primaries = primaries.Where(sp => sp.SupplierId != excludeSupplierId.Value);
            }
            foreach (var sp in primaries)
            {
                sp.IsPrimarySupplier = false;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SupplierProduct>> GetSuppliersForProductAsync(Guid productId, bool onlyActive = false, CancellationToken cancellationToken = default)
        {
            var query = SupplierProducts.Where(sp => sp.ProductId == productId);
            if (onlyActive)
            {
                query = query.Where(sp => sp.Status == SupplierProductStatus.Active);
            }
            return Task.FromResult((IReadOnlyList<SupplierProduct>)query.ToList());
        }

        public Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.Any(p => p.Id == productId));
        }

        public Task<Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == productId));
        }

        public List<SupplierBranchAssignment> BranchAssignments { get; } = new();

        public Task<(IReadOnlyList<SupplierBranchAssignment> Items, int TotalCount)> GetPagedBranchAssignmentsAsync(
            string? supplierSearch,
            string? branchSearch,
            IReadOnlyList<string>? supplierCodes,
            IReadOnlyList<string>? supplierNames,
            IReadOnlyList<string>? branchCodes,
            IReadOnlyList<string>? branchNames,
            Guid? supplierId,
            Guid? branchId,
            SupplierStatus? status,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(((IReadOnlyList<SupplierBranchAssignment>)new List<SupplierBranchAssignment>(), 0));
        }

        public Task<SupplierBranchAssignment?> GetBranchAssignmentAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BranchAssignments.FirstOrDefault(x => x.SupplierId == supplierId && x.BranchId == branchId));
        }

        public Task<bool> BranchAssignmentExistsAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BranchAssignments.Any(x => x.SupplierId == supplierId && x.BranchId == branchId));
        }

        public Task AddBranchAssignmentAsync(SupplierBranchAssignment assignment, CancellationToken cancellationToken = default)
        {
            BranchAssignments.Add(assignment);
            return Task.CompletedTask;
        }

        public Task RemoveBranchAssignmentAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default)
        {
            var item = BranchAssignments.FirstOrDefault(x => x.SupplierId == supplierId && x.BranchId == branchId);
            if (item != null)
            {
                BranchAssignments.Remove(item);
            }
            return Task.CompletedTask;
        }

        public Task<bool> BranchExistsAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<MT_GRMS.Domain.Entities.SaaS.Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MT_GRMS.Domain.Entities.SaaS.Branch?>(new MT_GRMS.Domain.Entities.SaaS.Branch
            {
                Id = branchId,
                BranchCode = "BR-01",
                Name = "Chi nhánh mẫu",
                IsActive = true
            });
        }

        public List<MT_GRMS.Domain.Entities.Inventory.BranchStock> BranchStocks { get; } = new();

        public Task<IReadOnlyList<Guid>> GetActiveBranchIdsForSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
        {
            var branchIds = BranchAssignments
                .Where(sba => sba.SupplierId == supplierId && sba.Status == SupplierStatus.Active && !sba.IsDeleted)
                .Select(sba => sba.BranchId)
                .Distinct()
                .ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(branchIds);
        }

        public Task<IReadOnlyList<SupplierProduct>> GetActiveSupplierProductsForSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
        {
            var products = SupplierProducts
                .Where(sp => sp.SupplierId == supplierId && sp.Status == SupplierProductStatus.Active && !sp.IsDeleted)
                .ToList();
            return Task.FromResult<IReadOnlyList<SupplierProduct>>(products);
        }

        public Task<bool> BranchStockExistsAsync(Guid branchId, Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BranchStocks.Any(bs => bs.BranchId == branchId && bs.ProductId == productId && !bs.IsDeleted));
        }

        public Task AddBranchStockAsync(MT_GRMS.Domain.Entities.Inventory.BranchStock branchStock, CancellationToken cancellationToken = default)
        {
            BranchStocks.Add(branchStock);
            return Task.CompletedTask;
        }
    }
}

