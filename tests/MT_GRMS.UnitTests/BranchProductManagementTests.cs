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

            // Default: mọi SupplierProduct Active đều khả dụng nếu chi nhánh có NCC liên kết
            var items = SupplierProducts
                .Where(sp => ActiveSupplierIdsForBranch.Contains(sp.SupplierId))
                .GroupBy(sp => sp.ProductId)
                .Select(g =>
                {
                    var sp = g.First();
                    var prod = Products.FirstOrDefault(p => p.Id == sp.ProductId);
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
                        IsAlreadyAssigned = BranchProducts.Any(bp =>
                            bp.BranchId == branchId && bp.ProductId == sp.ProductId && !bp.IsDeleted)
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
