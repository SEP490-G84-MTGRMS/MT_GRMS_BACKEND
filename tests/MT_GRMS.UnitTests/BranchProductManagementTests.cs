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
                Status = "Active"
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
                Status = "Active"
            }
        });

        // Seed initial Stock for Product 1
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
        Assert.Equal(12m, updated.MinStockLevel);
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

    // =========================================================================
    // FAKE REPOSITORY & CONTEXT IMPLEMENTATIONS
    // =========================================================================

    private class FakeBranchProductRepository : IBranchProductRepository
    {
        public List<Branch> Branches { get; } = new();
        public List<Product> Products { get; } = new();
        public List<BranchStock> BranchStocks { get; } = new();

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
            var branch = Branches.FirstOrDefault(b => b.Id == branchId);
            var query = BranchStocks.Where(s => s.BranchId == branchId);

            var items = query.Select(s =>
            {
                var prod = Products.FirstOrDefault(p => p.Id == s.ProductId);
                return new BranchProductDto
                {
                    Id = s.Id,
                    BranchId = s.BranchId,
                    BranchName = branch?.Name ?? "Chi nhánh",
                    ProductId = s.ProductId,
                    Name = prod?.Name ?? "Sản phẩm",
                    Sku = prod?.ProductCode ?? "SKU",
                    Barcode = prod?.Barcode ?? "BARCODE",
                    Category = "Lương thực",
                    Unit = prod?.BaseUnit ?? "Gói",
                    RetailPrice = prod?.SellingPrice ?? 0m,
                    CostPrice = prod?.CostPrice ?? 0m,
                    CurrentStock = s.QuantityOnHand,
                    MinStockLevel = prod?.MinStock ?? 0m,
                    PrimarySupplierName = "Nông Sản Tươi Sạch Khải Anh",
                    Status = prod?.Status ?? "Active",
                    ShelfLocation = s.ShelfLocation,
                    AssignedDate = DateTime.UtcNow
                };
            }).ToList();

            return Task.FromResult(((IReadOnlyList<BranchProductDto>)items, items.Count));
        }

        public Task<IReadOnlyList<BranchSummaryDto>> GetBranchSummariesAsync(
            CancellationToken cancellationToken = default)
        {
            var summaries = Branches.Select(b =>
            {
                var bStocks = BranchStocks.Where(s => s.BranchId == b.Id).ToList();
                return new BranchSummaryDto
                {
                    Id = b.Id,
                    Code = b.Code,
                    Name = b.Name,
                    Address = b.Address ?? string.Empty,
                    ManagerName = b.ManagerName,
                    Phone = b.Phone,
                    TotalProducts = bStocks.Select(s => s.ProductId).Distinct().Count(),
                    TotalStock = bStocks.Sum(s => s.QuantityOnHand),
                    LowStockCount = 0
                };
            }).ToList();

            return Task.FromResult((IReadOnlyList<BranchSummaryDto>)summaries);
        }

        public Task<BranchStock?> GetBranchStockAsync(
            Guid branchId,
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BranchStocks.FirstOrDefault(s => s.BranchId == branchId && s.ProductId == productId));
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
