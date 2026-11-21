using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class OrderAndBatchRepositoriesIntegrationTests
{
    private const string TenantId = "tenant-repo-test-02";

    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = TenantId;
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrderBatchRepoDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, new TestTenantContext());
    }

    [Fact]
    public async Task OrderRepository_AllMethods_ShouldWork()
    {
        using var context = CreateDbContext();
        var repo = new OrderRepository(context);

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchCode = "BR-ORD",
            Name = "Chi nhánh Đơn hàng",
            IsActive = true
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CustomerCode = "KH-ORD",
            Name = "Nguyễn Văn Đơn"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductCode = "SP-ORD",
            Name = "Bánh Mì Sanwich",
            BaseUnit = "Gói",
            CostPrice = 10000,
            SellingPrice = 15000,
            Units = new List<ProductUnit>
            {
                new() { Id = Guid.NewGuid(), UnitName = "Gói", ConversionRate = 1m, IsBaseUnit = true }
            }
        };
        var branchProduct = new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchId = branch.Id,
            ProductId = product.Id,
            Status = "Active"
        };

        var stock1 = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchId = branch.Id,
            ProductId = product.Id,
            BatchCode = "LOT-01",
            QuantityOnHand = 50,
            ManufactureDate = DateTime.UtcNow.AddDays(-10),
            ExpiryDate = DateTime.UtcNow.AddDays(20)
        };
        var stock2 = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchId = branch.Id,
            ProductId = product.Id,
            BatchCode = "LOT-02",
            QuantityOnHand = 30,
            ManufactureDate = DateTime.UtcNow.AddDays(-5),
            ExpiryDate = DateTime.UtcNow.AddDays(40)
        };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "ORD-001",
            BranchId = branch.Id,
            CustomerId = customer.Id,
            PaymentMethod = "CASH",
            Status = "COMPLETED",
            OrderDate = DateTime.UtcNow,
            TotalAmount = 30000,
            FinalAmount = 30000,
            Details = new List<OrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Qty = 2,
                    UnitPrice = 15000
                }
            }
        };

        await context.Branches.AddAsync(branch);
        await context.Customers.AddAsync(customer);
        await context.Products.AddAsync(product);
        await context.BranchProducts.AddAsync(branchProduct);
        await context.BranchStocks.AddRangeAsync(stock1, stock2);
        await repo.AddAsync(order);
        await context.SaveChangesAsync();

        // 1. GetBranchByIdAsync
        Assert.NotNull(await repo.GetBranchByIdAsync(branch.Id));
        Assert.Null(await repo.GetBranchByIdAsync(Guid.NewGuid()));

        // 2. GetCustomerByIdAsync
        Assert.NotNull(await repo.GetCustomerByIdAsync(customer.Id));
        Assert.Null(await repo.GetCustomerByIdAsync(Guid.NewGuid()));

        // 3. GetProductsByIdsAsync
        var prods = await repo.GetProductsByIdsAsync(new[] { product.Id, Guid.NewGuid() });
        Assert.True(prods.ContainsKey(product.Id));

        // 4. GetBranchProductsByProductIdsAsync
        var bpDict = await repo.GetBranchProductsByProductIdsAsync(branch.Id, new[] { product.Id });
        Assert.True(bpDict.ContainsKey(product.Id));

        // 5. GetBranchStocksForProductFefoAsync
        var fefoStocks = await repo.GetBranchStocksForProductFefoAsync(branch.Id, product.Id);
        Assert.Equal(2, fefoStocks.Count);
        Assert.Equal("LOT-01", fefoStocks[0].BatchCode); // Earliest expiry first

        // 6. GetBranchStockByIdAsync
        Assert.NotNull(await repo.GetBranchStockByIdAsync(branch.Id, product.Id, stock1.Id));
        Assert.Null(await repo.GetBranchStockByIdAsync(branch.Id, product.Id, Guid.NewGuid()));

        // 7. GetOrderWithDetailsAsync
        var loadedOrder = await repo.GetOrderWithDetailsAsync(order.Id);
        Assert.NotNull(loadedOrder);
        Assert.Single(loadedOrder!.Details);

        // 8. GetOrdersWithFilterAsync & CountOrdersWithFilterAsync
        var filterCount = await repo.CountOrdersWithFilterAsync(
            branch.Id, customer.Id, "CASH", "COMPLETED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal(1, filterCount);

        var filterOrders = await repo.GetOrdersWithFilterAsync(
            branch.Id, customer.Id, "CASH", "COMPLETED", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), 1, 10);
        Assert.Single(filterOrders);

        // 9. GetBranchStockAsync (with and without batchCode)
        Assert.NotNull(await repo.GetBranchStockAsync(branch.Id, product.Id, "LOT-01"));
        Assert.NotNull(await repo.GetBranchStockAsync(branch.Id, product.Id, null));

        // 10. GetAllOrdersForSummaryAsync
        var summaryOrders = await repo.GetAllOrdersForSummaryAsync(branch.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Single(summaryOrders);
    }

    [Fact]
    public async Task BatchRepository_AllMethods_ShouldWork()
    {
        using var context = CreateDbContext();
        var repo = new BatchRepository(context);

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchCode = "BR-BAT",
            Name = "Chi nhánh Lô hàng"
        };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CategoryCode = "CAT-BAT",
            Name = "Thực phẩm tươi sống"
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "SUP-BAT",
            Name = "NCC Sữa Ba Vì"
        };
        var batchProd = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CategoryId = category.Id,
            ProductCode = "SP-HAS-BATCH",
            Name = "Sữa Tươi Thanh Trùng Ba Vì",
            BaseUnit = "Chai",
            CostPrice = 25000,
            HasBatches = true
        };
        var nonBatchProd = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CategoryId = category.Id,
            ProductCode = "SP-NO-BATCH",
            Name = "Bút Bi Thiên Long",
            BaseUnit = "Cây",
            CostPrice = 3000,
            HasBatches = false
        };

        var supProd = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SupplierId = supplier.Id,
            ProductId = batchProd.Id,
            DeliveryUoM = "Thùng"
        };

        var stockBatch = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchId = branch.Id,
            ProductId = batchProd.Id,
            SupplierId = supplier.Id,
            BatchCode = "L-202611-01",
            QuantityOnHand = 40,
            QuantityReserved = 5,
            QuantityQuarantined = 0,
            LandedCostPerUnit = 25000,
            ManufactureDate = DateTime.UtcNow.AddDays(-2),
            ExpiryDate = DateTime.UtcNow.AddDays(5) // Warning / Critical range
        };

        var stockNonBatch = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchId = branch.Id,
            ProductId = nonBatchProd.Id,
            SupplierId = supplier.Id,
            QuantityOnHand = 100,
            LandedCostPerUnit = 3000
        };

        await context.Branches.AddAsync(branch);
        await context.Categories.AddAsync(category);
        await context.Suppliers.AddAsync(supplier);
        await context.Products.AddRangeAsync(batchProd, nonBatchProd);
        await context.SupplierProducts.AddAsync(supProd);
        await context.BranchStocks.AddRangeAsync(stockBatch, stockNonBatch);
        await context.SaveChangesAsync();

        // 1. GetBranchStockForUpdateAsync
        var forUpdate = await repo.GetBranchStockForUpdateAsync(TenantId, stockBatch.Id);
        Assert.NotNull(forUpdate);

        // 2. GetPagedBatchesAsync (all batches)
        var pagedAll = await repo.GetPagedBatchesAsync(TenantId, new BatchFilterQuery
        {
            BranchId = branch.Id,
            PageIndex = 1,
            PageSize = 10
        });
        Assert.Equal(2, pagedAll.TotalCount);

        // Filter by keyword
        var pagedKeyword = await repo.GetPagedBatchesAsync(TenantId, new BatchFilterQuery
        {
            BranchId = branch.Id,
            Keyword = "Thanh Trùng"
        });
        Assert.Single(pagedKeyword.Items);

        // Filter by status
        var pagedStatus = await repo.GetPagedBatchesAsync(TenantId, new BatchFilterQuery
        {
            BranchId = branch.Id,
            Status = "Normal"
        });
        Assert.Single(pagedStatus.Items);

        // 3. GetBatchByIdAsync (BranchStock batch managed & non-batch managed)
        var itemBatch = await repo.GetBatchByIdAsync(TenantId, stockBatch.Id);
        Assert.NotNull(itemBatch);
        Assert.Equal("L-202611-01", itemBatch!.BatchCode);

        var itemNonBatch = await repo.GetBatchByIdAsync(TenantId, stockNonBatch.Id);
        Assert.NotNull(itemNonBatch);
        Assert.Equal("Normal", itemNonBatch!.Status);

        // 4. AddBatchAsync legacy đã tắt; GetBatchById chỉ đọc BranchStocks
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.AddBatchAsync(new Batch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = batchProd.Id,
                BatchCode = "LEGACY-01",
                InitialQuantity = 20,
                LandedCostPerUnit = 25000,
                ManufactureDate = DateTime.UtcNow.AddDays(-10),
                ExpiryDate = DateTime.UtcNow.AddDays(20)
            }));

        var missingLegacy = await repo.GetBatchByIdAsync(TenantId, Guid.NewGuid());
        Assert.Null(missingLegacy);

        // 5. GetNearExpiryBatchesAsync
        var nearExpiry = await repo.GetNearExpiryBatchesAsync(TenantId, 10, branch.Id);
        Assert.Single(nearExpiry);
        Assert.Equal("L-202611-01", nearExpiry[0].BatchCode);
    }
}