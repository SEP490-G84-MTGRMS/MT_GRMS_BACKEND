using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BranchProductManagementTests
{
    private readonly FakeBranchProductRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly BranchProductService _service;

    private readonly Guid _branchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _prod1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _prod2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid _defaultSupplierId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public BranchProductManagementTests()
    {
        _repo = new FakeBranchProductRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new BranchProductService(_repo, _uow, _tenantContext);

        // Seed Branch
        _repo.Branches.Add(new Branch
        {
            Id = _branchId,
            TenantId = "tenant-test-1",
            Code = "BR-01",
            Name = "Chi nhánh Nguyễn Trãi",
            Address = "123 Nguyễn Trãi, Q1, TP.HCM",
            Phone = "0901234567",
            ManagerName = "Nguyễn Văn Quản Lý",
            IsActive = true
        });

        // Seed Products
        _repo.Products.AddRange(new[]
        {
            new Product
            {
                Id = _prod1Id,
                TenantId = "tenant-test-1",
                ProductCode = "SP0001",
                Barcode = "893500111222",
                Name = "Gạo ST25 Thơm Thượng Hạng 5kg",
                BaseUnit = "Túi",
                CostPrice = 160000m,
                SellingPrice = 195000m,
                MinStock = 10m,
                Status = ProductStatus.Active
            },
            new Product
            {
                Id = _prod2Id,
                TenantId = "tenant-test-1",
                ProductCode = "SP0002",
                Barcode = "893500333444",
                Name = "Sữa Tươi Tiệt Trùng Vinamilk 1L",
                BaseUnit = "Hộp",
                CostPrice = 30000m,
                SellingPrice = 38000m,
                MinStock = 20m,
                Status = ProductStatus.Active
            }
        });

        // Seed catalog + stock for Product 1
        _repo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = _prod1Id,
            SellingPriceOverride = null,
            Status = "Active",
            ShelfLocation = "Kệ A1-02"
        });
        _repo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = _prod1Id,
            QuantityOnHand = 25m,
            ShelfLocation = "Kệ A1-02",
            LandedCostPerUnit = 160000m
        });

        // Default: chi nhánh đã liên kết NCC và cả 2 SP đều thuộc NCC đó
        _repo.ActiveSupplierIdsForBranch.Add(_defaultSupplierId);
        _repo.SupplierProducts.AddRange(new[]
        {
            new SupplierProduct
            {
                Id = Guid.NewGuid(),
                ProductId = _prod1Id,
                SupplierId = _defaultSupplierId,
                AgreedWholesalePrice = 160000m,
                MinimumOrderQuantity = 5
            },
            new SupplierProduct
            {
                Id = Guid.NewGuid(),
                ProductId = _prod2Id,
                SupplierId = _defaultSupplierId,
                AgreedWholesalePrice = 30000m,
                MinimumOrderQuantity = 8
            }
        });
    }

    [Fact]
    public async Task REQ_STOREPROD_01_GetBranchProducts_ReturnsPagedList()
    {
        // Act
        var result = await _service.GetBranchProductsAsync(_branchId, new BranchProductQueryFilter { PageIndex = 1, PageSize = 10 });

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("SP0001", result.Items[0].Sku);
        Assert.Equal(25m, result.Items[0].CurrentStock);
    }

    [Fact]
    public async Task REQ_STOREPROD_02_GetBranchSummaries_ReturnsCorrectMetrics()
    {
        // Act
        var summaries = await _service.GetBranchSummariesAsync();

        // Assert
        Assert.NotEmpty(summaries);
        var summary = summaries.First(s => s.Id == _branchId);
        Assert.Equal(1, summary.TotalProducts);
        Assert.Equal(25m, summary.TotalStock);
        Assert.Equal(0, summary.LowStockCount);
    }

    [Fact]
    public async Task REQ_STOREPROD_03_AssignProducts_CreatesBranchStockRecord()
    {
        // Act: Assign Product 2 to Branch
        var command = new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { _prod2Id },
            DefaultRetailPrice = 40000m,
            DefaultMinStock = 15m,
            ShelfLocation = "Kệ B2-01"
        };

        var result = await _service.AssignProductsAsync(_branchId, command);

        // Assert
        Assert.Single(result);
        Assert.Equal("SP0002", result[0].Sku);
        Assert.True(_uow.HasSaved);

        var stocks = await _repo.GetBranchStocksByProductAsync(_branchId, _prod2Id);
        Assert.Single(stocks);
        Assert.Equal(0m, stocks[0].QuantityOnHand);
        Assert.Equal("Kệ B2-01", stocks[0].ShelfLocation);
    }

    [Fact]
    public async Task REQ_STOREPROD_03c_ManualAssignSecondSupplier_AppendsToAllowedSupplierIds()
    {
        // Arrange: Product 1 đã gán tại _branchId với _defaultSupplierId (NCC B); thêm NCC C cũng cung cấp Product 1
        var supplierCId = Guid.NewGuid();
        var existingBp = _repo.BranchProducts.First(bp => bp.BranchId == _branchId && bp.ProductId == _prod1Id);
        existingBp.SetAllowedSupplierIds(new[] { _defaultSupplierId });

        _repo.ActiveSupplierIdsForBranch.Add(supplierCId);
        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            ProductId = _prod1Id,
            SupplierId = supplierCId,
            Supplier = new Domain.Entities.Supplier.Supplier { Id = supplierCId, Code = "NCC-C", Name = "Nhà cung cấp C" },
            AgreedWholesalePrice = 155000m,
            MinimumOrderQuantity = 6,
            Status = SupplierProductStatus.Active
        });

        // Verify GetAssignableProducts trả cả 2 cặp: NCC B (Đã gán), NCC C (Chưa gán)
        var assignable = await _service.GetAssignableProductsAsync(_branchId);
        var prod1Pairs = assignable.Where(x => x.ProductId == _prod1Id).ToList();
        Assert.Equal(2, prod1Pairs.Count);
        Assert.True(prod1Pairs.Single(x => x.SupplierId == _defaultSupplierId).IsAlreadyAssigned);
        Assert.False(prod1Pairs.Single(x => x.SupplierId == supplierCId).IsAlreadyAssigned);

        // Act: User add tay cặp (Product 1, NCC C)
        var result = await _service.AssignProductsAsync(_branchId, new AssignBranchProductsCommand
        {
            Items = new List<BranchProductAssignItemCommand>
            {
                new() { ProductId = _prod1Id, SupplierId = supplierCId }
            }
        });

        // Assert
        Assert.Single(result);
        Assert.Equal(supplierCId, result[0].SupplierId);
        Assert.Equal(new[] { _defaultSupplierId, supplierCId }, existingBp.GetAllowedSupplierIds());
    }

    [Fact]
    public async Task REQ_STOREPROD_03b_AssignProducts_ReusesSupplierProductTerms_DoesNotWriteMoq()
    {
        var prod3Id = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        _repo.Products.Add(new Product
        {
            Id = prod3Id,
            TenantId = "tenant-test-1",
            ProductCode = "SP0003",
            Name = "Bánh Quy Bơ Danisa 454g",
            BaseUnit = "Hộp",
            CostPrice = 90000m,
            SellingPrice = 120000m,
            MinStock = 5m,
            Status = ProductStatus.Active
        });
        var sp = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            ProductId = prod3Id,
            SupplierId = supplierId,
            AgreedWholesalePrice = 100000m,
            MinimumOrderQuantity = 10
        };
        _repo.SupplierProducts.Add(sp);
        _repo.ActiveSupplierIdsForBranch.Add(supplierId);

        var command = new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { prod3Id },
            DefaultRetailPrice = 125000m,
            DefaultMinStock = 8m,
            DefaultMinOrderQuantity = 15,
            ShelfLocation = "Kệ C1-03"
        };

        var result = await _service.AssignProductsAsync(_branchId, command);

        Assert.Single(result);
        Assert.Equal("SP0003", result[0].Sku);
        Assert.Equal(10, result[0].MinOrderQuantity);
        Assert.Equal(100000m, result[0].CostPrice);
        Assert.Equal(10, sp.MinimumOrderQuantity);
        Assert.Equal(120000m, _repo.Products.First(p => p.Id == prod3Id).SellingPrice);
        Assert.Equal(5m, _repo.Products.First(p => p.Id == prod3Id).MinStock);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_STOREPROD_04_UpdateProduct_ModifiesPriceAndMinStock()
    {
        // Act
        var command = new UpdateBranchProductCommand
        {
            RetailPrice = 210000m,
            MinStockLevel = 12m,
            ShelfLocation = "Kệ A1-05"
        };

        var updated = await _service.UpdateProductAsync(_branchId, _prod1Id, command);

        // Assert
        Assert.Equal(210000m, updated.RetailPrice);
        Assert.Equal(10m, updated.MinStockLevel);
        Assert.Equal("Kệ A1-05", updated.ShelfLocation);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_STOREPROD_05_ToggleStatus_SwitchesActiveStatus()
    {
        // Act
        var toggled = await _service.ToggleStatusAsync(_branchId, _prod1Id, "Inactive");

        // Assert
        Assert.Equal("Inactive", toggled.Status);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_STOREPROD_06_RemoveProduct_ThrowsWhenCurrentStockGreaterThanZero()
    {
        // Product 1 currently has 25 units on hand
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RemoveProductAsync(_branchId, _prod1Id));

        Assert.Equal("CANNOT_REMOVE_PRODUCT_WITH_STOCK", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_STOREPROD_07_RemoveProduct_SucceedsWhenStockIsZero()
    {
        // First assign Product 2 (stock = 0)
        await _service.AssignProductsAsync(_branchId, new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { _prod2Id }
        });

        // Act: Remove Product 2
        await _service.RemoveProductAsync(_branchId, _prod2Id);

        // Assert
        var stocks = await _repo.GetBranchStocksByProductAsync(_branchId, _prod2Id);
        Assert.Empty(stocks);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_STOREPROD_08_UpdateProduct_DoesNotWriteSupplierMoq()
    {
        var supplierId = Guid.NewGuid();
        _repo.SupplierProducts.RemoveAll(sp => sp.ProductId == _prod1Id);
        var sp = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            ProductId = _prod1Id,
            SupplierId = supplierId,
            MinimumOrderQuantity = 5,
            AgreedWholesalePrice = 150000m,
            IsPrimarySupplier = true
        };
        _repo.SupplierProducts.Add(sp);
        var stock = _repo.BranchStocks.First(s => s.BranchId == _branchId && s.ProductId == _prod1Id);
        stock.SupplierId = supplierId;

        var command = new UpdateBranchProductCommand
        {
            RetailPrice = 210000m,
            MinStockLevel = 10m,
            MinOrderQuantity = 25,
            SupplierId = supplierId
        };
        var result = await _service.UpdateProductAsync(_branchId, _prod1Id, command);

        Assert.Equal(5, result.MinOrderQuantity);
        Assert.Equal(5, sp.MinimumOrderQuantity);
        Assert.Equal(150000m, result.CostPrice);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_STOREPROD_09_UpdateProduct_BranchPriceIsolation()
    {
        // Branch 1 has Product 1. Let's create Branch 2 and assign Product 1 to Branch 2
        var branch2Id = Guid.NewGuid();
        _repo.Branches.Add(new Branch { Id = branch2Id, Code = "BR-02", Name = "Chi nhánh 2", IsActive = true });
        _repo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            BranchId = branch2Id,
            ProductId = _prod1Id,
            SellingPriceOverride = 195000m,
            Status = "Active"
        });
        _repo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branch2Id,
            ProductId = _prod1Id,
            QuantityOnHand = 10m,
            RetailPrice = 195000m
        });

        // Act: Update price at Branch 1 only to 220,000
        var command = new UpdateBranchProductCommand
        {
            RetailPrice = 220000m,
            MinStockLevel = 10m
        };
        await _service.UpdateProductAsync(_branchId, _prod1Id, command);

        // Assert: Branch 1 stock has RetailPrice 220,000, Branch 2 stock still has 195,000
        var b1Stocks = await _repo.GetBranchStocksByProductAsync(_branchId, _prod1Id);
        var b2Stocks = await _repo.GetBranchStocksByProductAsync(branch2Id, _prod1Id);

        Assert.Equal(220000m, b1Stocks.First().RetailPrice);
        Assert.Equal(195000m, b2Stocks.First().RetailPrice);
    }

    [Fact]
    public async Task REQ_STOREPROD_10_ToggleStatus_BranchStatusIsolation()
    {
        // Branch 1 has Product 1. Let's create Branch 2 and assign Product 1 to Branch 2
        var branch2Id = Guid.NewGuid();
        _repo.Branches.Add(new Branch { Id = branch2Id, Code = "BR-02", Name = "Chi nhánh 2", IsActive = true });
        _repo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            BranchId = branch2Id,
            ProductId = _prod1Id,
            Status = "Active"
        });
        _repo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branch2Id,
            ProductId = _prod1Id,
            QuantityOnHand = 10m,
            Status = "Active"
        });

        // Act: Inactivate Product 1 at Branch 1
        var toggled = await _service.ToggleStatusAsync(_branchId, _prod1Id, "Inactive");
        Assert.Equal("Inactive", toggled.Status);

        // Assert: Branch 1 stock is Inactive, Branch 2 stock is STILL Active, Master Product is STILL Active
        var b1Stocks = await _repo.GetBranchStocksByProductAsync(_branchId, _prod1Id);
        var b2Stocks = await _repo.GetBranchStocksByProductAsync(branch2Id, _prod1Id);
        var masterProd = await _repo.GetProductByIdAsync(_prod1Id);

        Assert.Equal("Inactive", b1Stocks.First().Status);
        Assert.Equal("Active", b2Stocks.First().Status);
        Assert.Equal(ProductStatus.Active, masterProd!.Status);
    }

    [Fact]
    public async Task REQ_STOREPROD_09_AssignThenSupplierPriceChange_BranchReadsLiveTerms_DoesNotTouchPoSnapshot()
    {
        var supplierAId = Guid.NewGuid();
        var supplierBId = Guid.NewGuid();
        _repo.ActiveSupplierIdsForBranch.Clear();
        _repo.ActiveSupplierIdsForBranch.Add(supplierAId);
        _repo.SupplierProducts.RemoveAll(sp => sp.ProductId == _prod2Id);
        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            ProductId = _prod2Id,
            SupplierId = supplierAId,
            AgreedWholesalePrice = 100000m,
            MinimumOrderQuantity = 10
        });

        var assigned = await _service.AssignProductsAsync(_branchId, new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { _prod2Id }
        });

        Assert.Single(assigned);
        Assert.Equal(100000m, assigned[0].CostPrice);
        Assert.Equal(10, assigned[0].MinOrderQuantity);
        Assert.Equal(0m, (await _repo.GetBranchStocksByProductAsync(_branchId, _prod2Id)).First().QuantityOnHand);

        var spA = _repo.SupplierProducts.Single(x => x.SupplierId == supplierAId);
        spA.AgreedWholesalePrice = 110000m;
        spA.MinimumOrderQuantity = 20;

        var listed = await _service.GetBranchProductsAsync(_branchId, new BranchProductQueryFilter { PageIndex = 1, PageSize = 20 });
        var row = listed.Items.Single(x => x.ProductId == _prod2Id);
        Assert.Equal(110000m, row.CostPrice);
        Assert.Equal(20, row.MinOrderQuantity);

        const decimal historicalPoUnitPrice = 100000m;
        Assert.Equal(100000m, historicalPoUnitPrice);
        Assert.Equal(110000m, spA.AgreedWholesalePrice);

        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            ProductId = _prod2Id,
            SupplierId = supplierBId,
            AgreedWholesalePrice = 88000m,
            MinimumOrderQuantity = 6
        });

        Assert.Equal(2, _repo.SupplierProducts.Count(x => x.ProductId == _prod2Id));
        Assert.Equal(110000m, _repo.SupplierProducts.Single(x => x.SupplierId == supplierAId).AgreedWholesalePrice);
        Assert.Equal(88000m, _repo.SupplierProducts.Single(x => x.SupplierId == supplierBId).AgreedWholesalePrice);
    }

    [Fact]
    public async Task REQ_STOREPROD_10_AssignExistingProduct_DoesNotOverwriteStockQty()
    {
        var existingQty = (await _repo.GetBranchStocksByProductAsync(_branchId, _prod1Id)).First().QuantityOnHand;
        Assert.Equal(25m, existingQty);

        var result = await _service.AssignProductsAsync(_branchId, new AssignBranchProductsCommand
        {
            ProductIds = new List<Guid> { _prod1Id }
        });

        Assert.Empty(result);
        Assert.Equal(25m, (await _repo.GetBranchStocksByProductAsync(_branchId, _prod1Id)).First().QuantityOnHand);
    }

    [Fact]
    public async Task GetBranchProducts_BranchNotFound_Or_EmptyBranchId_ShouldBeHandled()
    {
        // Branch not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetBranchProductsAsync(Guid.NewGuid(), new BranchProductQueryFilter()));

        // Guid.Empty branchId allowed (global query)
        var result = await _service.GetBranchProductsAsync(Guid.Empty, new BranchProductQueryFilter());
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetAssignableProducts_BranchNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetAssignableProductsAsync(Guid.NewGuid()));

        var assignable = await _service.GetAssignableProductsAsync(_branchId);
        Assert.NotNull(assignable);
    }

    [Fact]
    public async Task AssignProducts_Exceptions_ShouldBeHandled()
    {
        // Validation error: empty ProductIds
        var cmdEmpty = new AssignBranchProductsCommand { ProductIds = new List<Guid>() };
        var exVal = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignProductsAsync(_branchId, cmdEmpty));
        Assert.Equal("VALIDATION_ERROR", exVal.ErrorCode);

        // Branch not found
        var cmdValid = new AssignBranchProductsCommand { ProductIds = new List<Guid> { _prod2Id } };
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AssignProductsAsync(Guid.NewGuid(), cmdValid));

        // No linked supplier
        _repo.ActiveSupplierIdsForBranch.Clear();
        var exNoSup = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignProductsAsync(_branchId, cmdValid));
        Assert.Equal("NO_SUPPLIER_LINKED", exNoSup.ErrorCode);

        // Product not supplied to branch
        _repo.ActiveSupplierIdsForBranch.Add(_defaultSupplierId);
        var unsuppliedId = Guid.NewGuid();
        _repo.Products.Add(new Product
        {
            Id = unsuppliedId,
            TenantId = "tenant-test-1",
            ProductCode = "SP-UNSUP",
            Name = "Unsupplied Product",
            Status = ProductStatus.Active
        });
        var cmdUnsup = new AssignBranchProductsCommand { ProductIds = new List<Guid> { unsuppliedId } };
        var exUnsup = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignProductsAsync(_branchId, cmdUnsup));
        Assert.Equal("PRODUCT_NOT_SUPPLIED_TO_BRANCH", exUnsup.ErrorCode);

        // Product inactive
        var inactiveProdId = Guid.NewGuid();
        _repo.Products.Add(new Product
        {
            Id = inactiveProdId,
            TenantId = "tenant-test-1",
            ProductCode = "SP-INACT",
            Name = "Inactive Product",
            Status = ProductStatus.Inactive
        });
        _repo.SupplierProducts.Add(new SupplierProduct
        {
            Id = Guid.NewGuid(),
            ProductId = inactiveProdId,
            SupplierId = _defaultSupplierId,
            AgreedWholesalePrice = 50000m
        });
        var cmdInact = new AssignBranchProductsCommand { ProductIds = new List<Guid> { inactiveProdId } };
        var exInact = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AssignProductsAsync(_branchId, cmdInact));
        Assert.Equal("PRODUCT_INACTIVE", exInact.ErrorCode);
    }

    [Fact]
    public async Task UpdateProduct_Exceptions_ShouldBeHandled()
    {
        var cmd = new UpdateBranchProductCommand { RetailPrice = 100000m };

        // Branch not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateProductAsync(Guid.NewGuid(), _prod1Id, cmd));

        // Product not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateProductAsync(_branchId, Guid.NewGuid(), cmd));

        // Branch product not found
        var unassignedProdId = Guid.NewGuid();
        _repo.Products.Add(new Product { Id = unassignedProdId, Name = "Unassigned", Status = ProductStatus.Active });
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateProductAsync(_branchId, unassignedProdId, cmd));

        // Activate when master product is inactive
        var masterProd = _repo.Products.First(p => p.Id == _prod1Id);
        masterProd.Status = ProductStatus.Inactive;
        var exInact = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateProductAsync(_branchId, _prod1Id, new UpdateBranchProductCommand { Status = "Active" }));
        Assert.Equal("PRODUCT_INACTIVE", exInact.ErrorCode);
        masterProd.Status = ProductStatus.Active;
    }

    [Fact]
    public async Task RemoveProduct_NotFound_Exceptions_ShouldBeHandled()
    {
        // Branch not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.RemoveProductAsync(Guid.NewGuid(), _prod1Id));

        // Product not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.RemoveProductAsync(_branchId, Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveProduct_WhenReservedStockGreaterThanZero_ThrowsBusinessException()
    {
        var prodId = Guid.NewGuid();
        _repo.Products.Add(new Product
        {
            Id = prodId,
            TenantId = "tenant-test-1",
            ProductCode = "SP-RES",
            Name = "SP Có Hàng Tạm Giữ",
            BaseUnit = "Cái",
            SellingPrice = 100000m
        });
        _repo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });
        _repo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = prodId,
            QuantityOnHand = 0m,
            QuantityReserved = 5m
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RemoveProductAsync(_branchId, prodId));
        Assert.Equal("CANNOT_REMOVE_PRODUCT_WITH_RESERVED_STOCK", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateProductAsync_WithUnitPriceOverrides_MatchesMaster_AndNewOverride()
    {
        var unit1Id = Guid.NewGuid();
        var unit2Id = Guid.NewGuid();
        var prod = _repo.Products.First(p => p.Id == _prod1Id);
        prod.Units = new List<ProductUnit>
        {
            new ProductUnit { Id = unit1Id, ProductId = _prod1Id, UnitName = "Thùng", ConversionRate = 10, Price = 1800000m },
            new ProductUnit { Id = unit2Id, ProductId = _prod1Id, UnitName = "Lốc", ConversionRate = 2, Price = 380000m }
        };

        var catalog = _repo.BranchProducts.First(bp => bp.BranchId == _branchId && bp.ProductId == _prod1Id);
        catalog.UnitPriceOverrides = new List<BranchProductUnitPrice>
        {
            new BranchProductUnitPrice
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                BranchProductId = catalog.Id,
                ProductUnitId = unit1Id,
                SellingPriceOverride = 1750000m
            }
        };

        // Act: Override unit 2, and reset unit 1 to master price (1800000)
        var cmd = new UpdateBranchProductCommand
        {
            RetailPrice = 200000m,
            UnitPrices = new List<BranchProductUnitPriceDto>
            {
                new BranchProductUnitPriceDto { UnitId = unit1Id, Price = 1800000m }, // Matches master -> soft deletes
                new BranchProductUnitPriceDto { UnitId = unit2Id, Price = 370000m },  // New override
                new BranchProductUnitPriceDto { UnitId = Guid.Empty, Price = 100m },   // Invalid unit id -> skipped
                new BranchProductUnitPriceDto { UnitId = Guid.NewGuid(), Price = 50m } // Non-existent unit -> skipped
            }
        };

        var result = await _service.UpdateProductAsync(_branchId, _prod1Id, cmd);
        Assert.NotNull(result);

        var unit1Override = catalog.UnitPriceOverrides.FirstOrDefault(u => u.ProductUnitId == unit1Id);
        Assert.True(unit1Override == null || unit1Override.IsDeleted);

        var unit2Override = catalog.UnitPriceOverrides.FirstOrDefault(u => u.ProductUnitId == unit2Id && !u.IsDeleted);
        Assert.NotNull(unit2Override);
        Assert.Equal(370000m, unit2Override.SellingPriceOverride);
    }

    // =========================================================================
    // FAKE REPOSITORY & CONTEXT IMPLEMENTATIONS
    // =========================================================================

    private class FakeBranchProductRepository : IBranchProductRepository
    {
        public List<Branch> Branches { get; } = new();
        public List<Product> Products { get; } = new();
        public List<BranchProduct> BranchProducts { get; } = new();
        public List<BranchStock> BranchStocks { get; } = new();
        public List<SupplierProduct> SupplierProducts { get; } = new();

        public Task<(IReadOnlyList<BranchProductDto> Items, int TotalCount)> GetPagedBranchProductsAsync(
            Guid branchId,
            BranchProductQueryFilter filter,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var branch = Branches.FirstOrDefault(b => b.Id == branchId);
            var items = BranchProducts
                .Where(bp => bp.BranchId == branchId && !bp.IsDeleted)
                .Select(bp =>
            {
                var prod = Products.FirstOrDefault(p => p.Id == bp.ProductId);
                var stocks = BranchStocks.Where(s => s.BranchId == bp.BranchId && s.ProductId == bp.ProductId).ToList();
                var lotSupplierIds = stocks.Where(x => x.SupplierId.HasValue).Select(x => x.SupplierId!.Value).Distinct().ToList();
                Guid? displaySupplierId = lotSupplierIds.Count == 1 ? lotSupplierIds[0] : null;
                var spMatches = SupplierProducts.Where(sp => sp.ProductId == bp.ProductId).ToList();
                SupplierProduct? sp = null;
                if (displaySupplierId.HasValue)
                {
                    sp = spMatches.FirstOrDefault(x => x.SupplierId == displaySupplierId.Value);
                }
                else if (lotSupplierIds.Count == 0 && spMatches.Count == 1)
                {
                    sp = spMatches[0];
                }

                return new BranchProductDto
                {
                    Id = bp.Id,
                    BranchId = bp.BranchId,
                    BranchCode = branch?.BranchCode ?? "BR-01",
                    BranchName = branch?.Name ?? "Chi nhánh",
                    ProductId = bp.ProductId,
                    Name = prod?.Name ?? "Sản phẩm",
                    Sku = prod?.ProductCode ?? "SKU",
                    Barcode = prod?.Barcode ?? "BARCODE",
                    Category = "Lương thực",
                    Unit = prod?.BaseUnit ?? "Gói",
                    RetailPrice = bp.SellingPriceOverride ?? prod?.SellingPrice ?? 0m,
                    CostPrice = sp != null
                        ? MT_GRMS.Application.Suppliers.SupplierProductPricing.ResolveEffectivePurchasePrice(
                            sp.AgreedWholesalePrice,
                            prod?.CostPrice)
                        : (prod?.CostPrice ?? 0m),
                    HasActiveSupplierSource = sp != null,
                    LostSupplierSource = sp == null,
                    CurrentStock = stocks.Sum(x => x.QuantityOnHand),
                    MinStockLevel = prod?.MinStock ?? 0m,
                    SupplierId = sp?.SupplierId ?? displaySupplierId,
                    PrimarySupplierName = sp?.Supplier?.Name ?? string.Empty,
                    MinOrderQuantity = sp != null && sp.MinimumOrderQuantity > 0 ? (int)sp.MinimumOrderQuantity : 0,
                    Status = bp.Status,
                    ShelfLocation = bp.ShelfLocation,
                    AssignedDate = bp.CreatedAt
                };
            }).ToList();

            return Task.FromResult(((IReadOnlyList<BranchProductDto>)items, items.Count));
        }

        public Task<(IReadOnlyList<BranchProductDto> Items, int TotalCount)> GetPagedBranchProductsAsync(
            Guid branchId,
            string? searchTerm,
            string? category,
            string? status,
            string? stockAlertFilter,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            return GetPagedBranchProductsAsync(branchId, new BranchProductQueryFilter(), pageIndex, pageSize, cancellationToken);
        }

        public Task<IReadOnlyList<BranchSummaryDto>> GetBranchSummariesAsync(
            CancellationToken cancellationToken = default)
        {
            var summaries = Branches.Select(b =>
            {
                var bCatalog = BranchProducts.Where(s => s.BranchId == b.Id && !s.IsDeleted).ToList();
                var bStocks = BranchStocks.Where(s => s.BranchId == b.Id).ToList();
                return new BranchSummaryDto
                {
                    Id = b.Id,
                    Code = b.Code,
                    Name = b.Name,
                    Address = b.Address ?? string.Empty,
                    ManagerName = b.ManagerName,
                    Phone = b.Phone,
                    TotalProducts = bCatalog.Select(s => s.ProductId).Distinct().Count(),
                    TotalStock = bStocks.Sum(s => s.QuantityOnHand),
                    LowStockCount = 0
                };
            }).ToList();

            return Task.FromResult((IReadOnlyList<BranchSummaryDto>)summaries);
        }

        public Task<BranchProduct?> GetBranchProductAsync(
            Guid branchId,
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BranchProducts.FirstOrDefault(s =>
                s.BranchId == branchId && s.ProductId == productId && !s.IsDeleted));
        }

        public Task AddBranchProductAsync(
            BranchProduct branchProduct,
            CancellationToken cancellationToken = default)
        {
            BranchProducts.Add(branchProduct);
            return Task.CompletedTask;
        }

        public void RemoveBranchProduct(BranchProduct branchProduct)
        {
            branchProduct.IsDeleted = true;
        }

        public Task<IReadOnlyList<BranchStock>> GetBranchStocksByProductAsync(
            Guid branchId,
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<BranchStock>)BranchStocks.Where(s => s.BranchId == branchId && s.ProductId == productId).ToList());
        }

        public Task SyncBranchStockCatalogAsync(
            Guid branchId,
            Guid productId,
            decimal retailPrice,
            string status,
            string? shelfLocation,
            CancellationToken cancellationToken = default)
        {
            foreach (var stock in BranchStocks.Where(s => s.BranchId == branchId && s.ProductId == productId))
            {
                stock.RetailPrice = retailPrice;
                stock.Status = status;
                stock.ShelfLocation = shelfLocation;
            }

            return Task.CompletedTask;
        }

        public Task AddUnitPriceOverrideAsync(BranchProductUnitPrice unitPrice, CancellationToken cancellationToken = default)
        {
            var catalog = BranchProducts.First(bp => bp.Id == unitPrice.BranchProductId);
            catalog.UnitPriceOverrides.Add(unitPrice);
            return Task.CompletedTask;
        }

        public Task AddBranchStockAsync(
            BranchStock branchStock,
            CancellationToken cancellationToken = default)
        {
            BranchStocks.Add(branchStock);
            return Task.CompletedTask;
        }

        public void RemoveBranchStocks(
            IEnumerable<BranchStock> branchStocks)
        {
            foreach (var bs in branchStocks.ToList())
            {
                BranchStocks.Remove(bs);
            }
        }

        public Task<Product?> GetProductByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == productId));
        }

        public Task<Branch?> GetBranchByIdAsync(
            Guid branchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.FirstOrDefault(b => b.Id == branchId));
        }

        public Task<SupplierProduct?> GetSupplierProductAsync(
            Guid productId,
            Guid? supplierId,
            CancellationToken cancellationToken = default)
        {
            var query = SupplierProducts.Where(sp => sp.ProductId == productId).ToList();
            if (supplierId.HasValue && supplierId.Value != Guid.Empty)
            {
                return Task.FromResult(query.FirstOrDefault(sp => sp.SupplierId == supplierId.Value));
            }

            return Task.FromResult(query.Count == 1 ? query[0] : null);
        }

        public List<Guid> ActiveSupplierIdsForBranch { get; set; } = new();
        public List<AssignableBranchProductDto> AssignableProducts { get; set; } = new();

        public Task<IReadOnlyList<Guid>> GetActiveSupplierIdsForBranchAsync(
            Guid branchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Guid>)ActiveSupplierIdsForBranch);
        }

        public Task<IReadOnlyList<AssignableBranchProductDto>> GetAssignableProductsForBranchAsync(
            Guid branchId,
            CancellationToken cancellationToken = default)
        {
            if (AssignableProducts.Count > 0)
            {
                return Task.FromResult((IReadOnlyList<AssignableBranchProductDto>)AssignableProducts);
            }

            // Default: mọi cặp (Product × Supplier) Active đều khả dụng nếu chi nhánh có NCC liên kết
            var items = SupplierProducts
                .Where(sp => ActiveSupplierIdsForBranch.Contains(sp.SupplierId))
                .GroupBy(sp => (sp.ProductId, sp.SupplierId))
                .Select(g =>
                {
                    var sp = g.First();
                    var prod = Products.FirstOrDefault(p => p.Id == sp.ProductId);
                    var existingBp = BranchProducts.FirstOrDefault(bp =>
                        bp.BranchId == branchId && bp.ProductId == sp.ProductId && !bp.IsDeleted);
                    var allowed = existingBp?.GetAllowedSupplierIds();
                    var isAssigned = existingBp != null && (allowed == null || allowed.Contains(sp.SupplierId));
                    return new AssignableBranchProductDto
                    {
                        ProductId = sp.ProductId,
                        Sku = prod?.ProductCode ?? string.Empty,
                        Name = prod?.Name ?? string.Empty,
                        CategoryName = "Lương thực",
                        BaseUoM = prod?.BaseUnit ?? "Cái",
                        CostPrice = sp.AgreedWholesalePrice > 0 ? sp.AgreedWholesalePrice.Value : (prod?.CostPrice ?? 0m),
                        Moq = sp.MinimumOrderQuantity > 0 ? sp.MinimumOrderQuantity : 1,
                        SupplierId = sp.SupplierId,
                        SupplierCode = sp.Supplier?.Code ?? string.Empty,
                        SupplierName = sp.Supplier?.Name ?? string.Empty,
                        IsAlreadyAssigned = isAssigned
                    };
                })
                .ToList();

            return Task.FromResult((IReadOnlyList<AssignableBranchProductDto>)items);
        }
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
}
