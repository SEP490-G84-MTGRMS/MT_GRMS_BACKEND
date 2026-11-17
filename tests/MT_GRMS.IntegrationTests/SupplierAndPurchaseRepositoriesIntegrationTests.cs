using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class SupplierAndPurchaseRepositoriesIntegrationTests
{
    private const string TenantId = "tenant-sup-repo-01";

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

    private (AppDbContext Context, TestTenantContext Tenant) CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"SupRepoDb_{Guid.NewGuid()}")
            .Options;

        return (new AppDbContext(options, tenant), tenant);
    }

    [Fact]
    public async Task SupplierRepository_AllMethods_ShouldWork()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new SupplierRepository(context, tenant);

            var supplier1 = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "NCC01",
                Name = "Công ty CP Thực phẩm ABC",
                ContactPerson = "Nguyen Van A",
                Phone = "0912345678",
                SupplierType = SupplierType.Goods,
                Status = SupplierStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var supplier2 = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "NCC02",
                Name = "Đại lý phân phối XYZ",
                ContactPerson = "Tran Van B",
                Phone = "0987654321",
                SupplierType = SupplierType.Service,
                Status = SupplierStatus.Inactive,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(supplier1);
            await repo.AddAsync(supplier2);
            await context.SaveChangesAsync();

            // 1. ExistsByCodeAsync
            Assert.True(await repo.ExistsByCodeAsync("NCC01"));
            Assert.False(await repo.ExistsByCodeAsync("NCC99"));

            // 2. CountTodayAsync
            Assert.Equal(2, await repo.CountTodayAsync());

            // 3. GetByIdWithDetailsAsync
            var found = await repo.GetByIdWithDetailsAsync(supplier1.Id);
            Assert.NotNull(found);
            Assert.Equal("NCC01", found!.Code);

            // 4. GetPagedListAsync
            var (items, total) = await repo.GetPagedListAsync("ABC", null, null, null, 1, 10);
            Assert.Equal(1, total);
            Assert.Single(items);

            // 5. GetSearchSuggestionsAsync
            var emptySuggestions = await repo.GetSearchSuggestionsAsync("");
            Assert.Empty(emptySuggestions.Suppliers);

            var supSuggestions = await repo.GetSearchSuggestionsAsync("ABC");
            Assert.NotEmpty(supSuggestions.Suppliers);

            var phoneSuggestions = await repo.GetSearchSuggestionsAsync("0912");
            Assert.NotEmpty(phoneSuggestions.Phones);

            var contactSuggestions = await repo.GetSearchSuggestionsAsync("Nguyen");
            Assert.NotEmpty(contactSuggestions.ContactPersons);

            // 6. Ledger methods
            var ledger = new SupplierAPLedger
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier1.Id,
                DocNo = "DOC-001",
                DocType = APDocType.PurchaseInvoice,
                IdempotencyKey = "key-001",
                Amount = 100000,
                Direction = 1,
                BalanceAfter = 100000,
                TransactionDate = DateTime.UtcNow
            };
            await repo.AddLedgerAsync(ledger);
            await context.SaveChangesAsync();

            Assert.True(await repo.HasLedgerTransactionsAsync(supplier1.Id, APDocType.PurchaseInvoice));
            Assert.False(await repo.HasLedgerTransactionsAsync(supplier1.Id, APDocType.PaymentVoucher));
            Assert.NotNull(await repo.GetLedgerByIdempotencyKeyAsync("key-001"));
            Assert.Null(await repo.GetLedgerByIdempotencyKeyAsync("key-unknown"));

            var (ledgers, ledgerCount) = await repo.GetPagedLedgerAsync(supplier1.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), 1, 10, default, APDocType.PurchaseInvoice);
            Assert.Equal(1, ledgerCount);
            Assert.Single(ledgers);

            var docCount = await repo.CountLedgersByDocNoPrefixAsync("DOC");
            Assert.Equal(1, docCount);

            // 7. Blackout dates
            var boList = new List<SupplierBlackoutDate>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    SupplierId = supplier1.Id,
                    StartDate = DateTime.UtcNow.Date,
                    EndDate = DateTime.UtcNow.Date.AddDays(2),
                    Reason = "Nghi le"
                }
            };
            await repo.ReplaceBlackoutDatesAsync(supplier1.Id, boList);
            await context.SaveChangesAsync();

            var fetchedBo = await repo.GetBlackoutDatesAsync(supplier1.Id);
            Assert.Single(fetchedBo);

            // 8. Return policy
            var returnPolicy = new SupplierReturnPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier1.Id,
                Notes = "Return Policy 1",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddReturnPolicyAsync(returnPolicy);
            await context.SaveChangesAsync();

            var activePolicy = await repo.GetActiveReturnPolicyAsync(supplier1.Id);
            Assert.NotNull(activePolicy);

            // 9. Products & SupplierProducts
            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-SUP",
                Name = "Gia vị",
                CreatedAt = DateTime.UtcNow
            };
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-SUP-01",
                Name = "Nước mắm",
                CategoryId = cat.Id,
                BaseUnit = "Chai",
                Status = ProductStatus.Active,
                SellingPrice = 30000,
                CostPrice = 20000,
                CreatedAt = DateTime.UtcNow
            };
            await context.Categories.AddAsync(cat);
            await context.Products.AddAsync(prod);
            await context.SaveChangesAsync();

            Assert.True(await repo.ProductExistsAsync(prod.Id));
            Assert.False(await repo.ProductExistsAsync(Guid.NewGuid()));
            var foundProd = await repo.GetProductByIdAsync(prod.Id);
            Assert.NotNull(foundProd);

            var sp = new SupplierProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier1.Id,
                ProductId = prod.Id,
                AgreedWholesalePrice = 18000,
                MinimumOrderQuantity = 5,
                IsPrimarySupplier = true,
                Status = SupplierProductStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddSupplierProductAsync(sp);
            await context.SaveChangesAsync();

            var foundSp = await repo.GetSupplierProductAsync(supplier1.Id, prod.Id);
            Assert.NotNull(foundSp);

            var (spItems, spTotal) = await repo.GetPagedSupplierProductsAsync(supplier1.Id, null, cat.Id, SupplierProductStatus.Active, 1, 10);
            Assert.Equal(1, spTotal);
            Assert.Single(spItems);

            var supForProd = await repo.GetSuppliersForProductAsync(prod.Id, onlyActive: true);
            Assert.Single(supForProd);

            var activeSpList = await repo.GetActiveSupplierProductsForSupplierAsync(supplier1.Id);
            Assert.Single(activeSpList);

            await repo.ClearPrimarySupplierFlagAsync(prod.Id);
            await context.SaveChangesAsync();
            var reloadedSp = await repo.GetSupplierProductAsync(supplier1.Id, prod.Id);
            Assert.False(reloadedSp!.IsPrimarySupplier);

            // 10. Branch & Branch Assignment
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-SUP",
                Name = "Chi nhánh Quận 1",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(branch);
            await context.SaveChangesAsync();

            Assert.True(await repo.BranchExistsAsync(branch.Id));
            Assert.False(await repo.BranchExistsAsync(Guid.NewGuid()));
            Assert.NotNull(await repo.GetBranchByIdAsync(branch.Id));

            var assignment = new SupplierBranchAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier1.Id,
                BranchId = branch.Id,
                Status = SupplierStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBranchAssignmentAsync(assignment);
            await context.SaveChangesAsync();

            Assert.True(await repo.BranchAssignmentExistsAsync(supplier1.Id, branch.Id));
            var foundAsg = await repo.GetBranchAssignmentAsync(supplier1.Id, branch.Id);
            Assert.NotNull(foundAsg);

            var activeBranchIds = await repo.GetActiveBranchIdsForSupplierAsync(supplier1.Id);
            Assert.Contains(branch.Id, activeBranchIds);

            var (asgItems, asgTotal) = await repo.GetPagedBranchAssignmentsAsync(
                null, null, new[] { "NCC01" }, null, new[] { "BR-SUP" }, null,
                supplier1.Id, branch.Id, SupplierStatus.Active, 1, 10);
            Assert.Equal(1, asgTotal);

            // 11. BranchProduct & BranchStock
            Assert.False(await repo.BranchProductExistsAsync(branch.Id, prod.Id));
            var bp = new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBranchProductAsync(bp);
            await context.SaveChangesAsync();
            Assert.True(await repo.BranchProductExistsAsync(branch.Id, prod.Id));

            Assert.False(await repo.BranchStockExistsAsync(branch.Id, prod.Id));
            var bs = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                QuantityOnHand = 50,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBranchStockAsync(bs);
            await context.SaveChangesAsync();
            Assert.True(await repo.BranchStockExistsAsync(branch.Id, prod.Id));

            // 12. RemoveBranchAssignmentAsync
            await repo.RemoveBranchAssignmentAsync(supplier1.Id, branch.Id);
            await context.SaveChangesAsync();
            Assert.False(await repo.BranchAssignmentExistsAsync(supplier1.Id, branch.Id));
        }
    }

    [Fact]
    public async Task SupplierRepository_QueryFilters_ShouldFilterCorrectly()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new SupplierRepository(context, tenant);

            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-FD",
                Name = "Thực phẩm",
                CreatedAt = DateTime.UtcNow
            };
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SKU-PROD-01",
                Barcode = "BAR-01",
                Name = "Sản phẩm test",
                Category = cat,
                CategoryId = cat.Id,
                CreatedAt = DateTime.UtcNow
            };
            var sup = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "NCC-FLT",
                Name = "Công ty CP Lọc Test",
                ContactPerson = "Le Van C",
                Phone = "0933445566",
                SupplierType = SupplierType.Goods,
                Status = SupplierStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var supProd = new SupplierProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = sup.Id,
                ProductId = prod.Id,
                Supplier = sup,
                Product = prod,
                CreatedAt = DateTime.UtcNow
            };
            await context.Categories.AddAsync(cat);
            await context.Products.AddAsync(prod);
            await context.Suppliers.AddAsync(sup);
            await context.SupplierProducts.AddAsync(supProd);
            await context.SaveChangesAsync();

            // 1. Supplier Query Filters
            var (s1, c1) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, supplierIds: new() { sup.Id });
            Assert.Equal(1, c1);

            var (s2, c2) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, productIds: new() { prod.Id });
            Assert.Equal(1, c2);

            var (s3, c3) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, codes: new() { "NCC-FLT" });
            Assert.Equal(1, c3);

            var (s4, c4) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, names: new() { "Lọc Test" });
            Assert.Equal(1, c4);

            var (s5, c5) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, contactPersons: new() { "Le Van C" });
            Assert.Equal(1, c5);

            var (s6, c6) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, phones: new() { "0933445566" });
            Assert.Equal(1, c6);

            var (s7, c7) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, code: "FLT");
            Assert.Equal(1, c7);

            var (s8, c8) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, name: "Lọc");
            Assert.Equal(1, c8);

            var (s9, c9) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, contactPerson: "Le");
            Assert.Equal(1, c9);

            var (s10, c10) = await repo.GetPagedListAsync(null, null, null, null, 1, 10, phone: "0933");
            Assert.Equal(1, c10);

            var (s11, c11) = await repo.GetPagedListAsync(null, SupplierType.Goods, null, null, 1, 10);
            Assert.Equal(1, c11);

            var (s12, c12) = await repo.GetPagedListAsync(null, null, SupplierStatus.Active, null, 1, 10);
            Assert.Equal(1, c12);

            // 2. Supplier Product Query Filters
            var (sp1, pc1) = await repo.GetPagedSupplierProductsAsync(sup.Id, null, null, null, 1, 10, skus: new[] { "SKU-PROD-01" });
            Assert.Equal(1, pc1);

            var (sp2, pc2) = await repo.GetPagedSupplierProductsAsync(sup.Id, null, null, null, 1, 10, names: new[] { "Sản phẩm test" });
            Assert.Equal(1, pc2);

            var (sp3, pc3) = await repo.GetPagedSupplierProductsAsync(sup.Id, null, null, null, 1, 10, supplierCodes: new[] { "NCC-FLT" });
            Assert.Equal(1, pc3);

            var (sp4, pc4) = await repo.GetPagedSupplierProductsAsync(sup.Id, null, null, null, 1, 10, supplierNames: new[] { "Lọc Test" });
            Assert.Equal(1, pc4);

            var (sp5, pc5) = await repo.GetPagedSupplierProductsAsync(sup.Id, null, null, null, 1, 10, categoryName: "Thực phẩm");
            Assert.Equal(1, pc5);

            var (sp6, pc6) = await repo.GetPagedSupplierProductsAsync(sup.Id, null, null, null, 1, 10, supplierSearch: "NCC-FLT");
            Assert.Equal(1, pc6);

            var (sp7, pc7) = await repo.GetPagedSupplierProductsAsync(sup.Id, "test", null, null, 1, 10);
            Assert.Equal(1, pc7);
        }
    }

    [Fact]
    public async Task PurchaseOrderRepository_AllMethods_ShouldWork()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new PurchaseOrderRepository(context, tenant);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR01",
                Name = "Kho Tổng",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "NCC-PO",
                Name = "NCC Test",
                Status = SupplierStatus.Active,
                CutoffTime = "15:00",
                CutoffDayOffset = 1,
                MinOrderQuantity = 2,
                CreatedAt = DateTime.UtcNow
            };
            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-PO",
                Name = "Hàng khô",
                CreatedAt = DateTime.UtcNow
            };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-PO",
                Name = "Gạo tấm",
                CategoryId = cat.Id,
                BaseUnit = "Kg",
                Status = ProductStatus.Active,
                CostPrice = 40000,
                SellingPrice = 50000,
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(branch);
            await context.Suppliers.AddAsync(supplier);
            await context.Categories.AddAsync(cat);
            await context.Products.AddAsync(product);
            await context.SaveChangesAsync();

            // 1. GetBranchByIdAsync, GetSupplierByIdAsync, GetProductByIdAsync
            Assert.NotNull(await repo.GetBranchByIdAsync(branch.Id));
            Assert.NotNull(await repo.GetSupplierByIdAsync(supplier.Id));
            var p = await repo.GetProductByIdAsync(product.Id);
            Assert.NotNull(p);
            Assert.Equal("Hàng khô", p!.Category.Name);

            // 2. Matrix Products and Matrix Supplier Configs
            var sba = new SupplierBranchAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier.Id,
                BranchId = branch.Id,
                Status = SupplierStatus.Active,
                DeliveryDaysJson = "[\"mon\",\"wed\",\"fri\"]",
                CreatedAt = DateTime.UtcNow
            };
            var sp = new SupplierProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier.Id,
                ProductId = product.Id,
                AgreedWholesalePrice = 45000,
                MinimumOrderQuantity = 2,
                Status = SupplierProductStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var bp = new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = product.Id,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            var bs = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = product.Id,
                QuantityOnHand = 25,
                CreatedAt = DateTime.UtcNow
            };
            var pUnit = new ProductUnit
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductId = product.Id,
                UnitName = "Bao",
                ConversionRate = 50,
                IsPurchaseUnit = true,
                CreatedAt = DateTime.UtcNow
            };
            await context.SupplierBranchAssignments.AddAsync(sba);
            await context.SupplierProducts.AddAsync(sp);
            await context.BranchProducts.AddAsync(bp);
            await context.BranchStocks.AddAsync(bs);
            await context.ProductUnits.AddAsync(pUnit);
            await context.SaveChangesAsync();

            var matrix = await repo.GetMatrixProductsAsync(branch.Id, supplier.Id);
            Assert.Single(matrix);
            Assert.Equal("Bao", matrix[0].Unit);
            Assert.Equal(45000, matrix[0].Price);
            Assert.Equal(2, matrix[0].Moq);

            var supConfigs = await repo.GetMatrixSupplierConfigsAsync(branch.Id, supplier.Id);
            Assert.Single(supConfigs);
            Assert.Contains(1, supConfigs[0].ActiveDeliveryDays);

            // 3. GeneratePoCodeAsync & UserExistsAsync
            var poCode = await repo.GeneratePoCodeAsync(DateTime.UtcNow);
            Assert.StartsWith("PO-", poCode);
            Assert.False(await repo.UserExistsAsync("fake-user-id"));

            // 4. PO creation, details, revision
            var po = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = poCode,
                BranchId = branch.Id,
                SupplierId = supplier.Id,
                DeliveryDate = DateTime.UtcNow.Date.AddDays(1),
                Status = "DRAFT",
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddPurchaseOrderAsync(po);
            repo.AddPurchaseOrderDetail(new PurchaseOrderDetail
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PurchaseOrderId = po.Id,
                ProductId = product.Id,
                OrderedQty = 10,
                UnitPrice = 45000,
                CreatedAt = DateTime.UtcNow
            });
            repo.AddPurchaseOrderRevision(new PurchaseOrderRevision
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PurchaseOrderId = po.Id,
                RevisionNumber = 1,
                ChangedAtUtc = DateTime.UtcNow,
                ChangeReason = "Initial Draft"
            });
            await context.SaveChangesAsync();

            // 5. Draft queries
            var draftPo = await repo.GetDraftMatrixPoAsync(branch.Id, supplier.Id, DateTime.UtcNow);
            Assert.NotNull(draftPo);
            var drafts = await repo.GetDraftMatrixPosAsync(branch.Id);
            Assert.Single(drafts);

            // 6. Committed queries
            po.Status = "COMMITTED";
            po.OrderedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();

            var committed = await repo.FindCommittedPoAsync(branch.Id, supplier.Id, po.DeliveryDate.Value);
            Assert.NotNull(committed);

            var committedInRange = await repo.GetCommittedPosInRangeAsync(branch.Id, supplier.Id, DateTime.UtcNow.Date, 7);
            Assert.Single(committedInRange);

            // 7. Paged POs & GetById
            var (poList, poCount) = await repo.GetPagedPurchaseOrdersAsync(branch.Id, supplier.Id, "COMMITTED", DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(5), 1, 10, po.Code);
            Assert.Equal(1, poCount);
            Assert.Single(poList);

            var poById = await repo.GetPurchaseOrderByIdAsync(po.Id);
            Assert.NotNull(poById);
            Assert.Single(poById!.Details);

            // 8. Supplier product & active branches
            var spFound = await repo.GetSupplierProductAsync(supplier.Id, product.Id);
            Assert.NotNull(spFound);

            var activeBranches = await repo.GetActiveBranchesAsync();
            Assert.NotEmpty(activeBranches);

            var activeBranchIdsForSup = await repo.GetActiveBranchIdsForSupplierAsync(supplier.Id);
            Assert.Contains(branch.Id, activeBranchIdsForSup);

            var receiptsMap = await repo.GetActiveGoodsReceiptsByPoIdsAsync(new[] { po.Id });
            Assert.NotNull(receiptsMap);
        }
    }

    [Fact]
    public async Task GoodsReceiptRepository_AllMethods_ShouldWork()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new GoodsReceiptRepository(context, tenant);

            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "NCC-GRN",
                Name = "NCC GRN",
                CreatedAt = DateTime.UtcNow
            };
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-GRN",
                Name = "Chi nhánh GRN",
                CreatedAt = DateTime.UtcNow
            };
            var po = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "PO-GRN-01",
                BranchId = branch.Id,
                SupplierId = supplier.Id,
                Status = "SENT",
                DeliveryDate = DateTime.UtcNow.Date,
                CreatedAt = DateTime.UtcNow
            };
            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-GRN",
                Name = "Thực phẩm",
                CreatedAt = DateTime.UtcNow
            };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-GRN",
                Name = "Dầu ăn",
                CategoryId = cat.Id,
                BaseUnit = "Chai",
                CreatedAt = DateTime.UtcNow
            };
            var grn = new GoodsReceipt
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "GRN001",
                BranchId = branch.Id,
                SupplierId = supplier.Id,
                PurchaseOrderId = po.Id,
                PurchaseOrder = po,
                Status = "COMPLETED",
                Supplier = supplier,
                Branch = branch,
                ReceivedAtUtc = DateTime.UtcNow,
                Lines = new List<GoodsReceiptLine>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TenantId = TenantId,
                        ProductId = product.Id,
                        Product = product,
                        OrderedQty = 10,
                        DeliveredQty = 9,
                        AcceptedQty = 8,
                        RejectedQty = 1,
                        RejectionReason = "DAMAGED",
                        UnitPrice = 25000,
                        CreatedAt = DateTime.UtcNow
                    }
                },
                CreatedAt = DateTime.UtcNow
            };
            await context.Suppliers.AddAsync(supplier);
            await context.Branches.AddAsync(branch);
            await context.Categories.AddAsync(cat);
            await context.Products.AddAsync(product);
            await context.PurchaseOrders.AddAsync(po);
            await repo.AddAsync(grn);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            var found = await repo.GetByIdAsync(TenantId, grn.Id);
            Assert.NotNull(found);
            Assert.Equal("GRN001", found!.Code);

            // 2. GenerateCodeAsync
            var code = await repo.GenerateCodeAsync(TenantId);
            Assert.NotNull(code);

            // 3. GetPagedAsync with various filters
            var paged = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery
            {
                Keyword = "GRN001",
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(1, paged.TotalItems);
            Assert.NotEmpty(paged.Items[0].Lines);

            var pagedBranch = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery { BranchId = branch.Id });
            Assert.Equal(1, pagedBranch.TotalItems);

            var pagedSupplier = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery { SupplierId = supplier.Id });
            Assert.Equal(1, pagedSupplier.TotalItems);

            var pagedStatus = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery { Status = "COMPLETED" });
            Assert.Equal(1, pagedStatus.TotalItems);

            var pagedDates = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery
            {
                FromDate = DateTime.UtcNow.AddDays(-1),
                ToDate = DateTime.UtcNow.AddDays(1)
            });
            Assert.Equal(1, pagedDates.TotalItems);

            var pagedToday = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery { IsTodayDelivery = true });
            Assert.Equal(1, pagedToday.TotalItems);

            var pagedMismatch = await repo.GetPagedAsync(TenantId, new GoodsReceiptFilterQuery { HasMismatch = true });
            Assert.Equal(1, pagedMismatch.TotalItems);

            // 4. GetEligiblePurchaseOrdersAsync
            var po2 = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "PO-ELIGIBLE",
                BranchId = branch.Id,
                SupplierId = supplier.Id,
                Status = "SENT",
                DeliveryDate = DateTime.UtcNow.Date,
                CreatedAt = DateTime.UtcNow
            };
            await context.PurchaseOrders.AddAsync(po2);
            await context.SaveChangesAsync();

            var eligible = await repo.GetEligiblePurchaseOrdersAsync(TenantId, branch.Id);
            Assert.Contains(eligible, p => p.Id == po2.Id);

            // 5. PO with details queries
            var poWithDetails = await repo.GetPurchaseOrderWithDetailsAsync(TenantId, po2.Id);
            Assert.NotNull(poWithDetails);

            var poByCode = await repo.GetPurchaseOrderByCodeWithDetailsAsync(TenantId, "PO-ELIGIBLE");
            Assert.NotNull(poByCode);

            // 6. Active receipt by PO
            var activeReceipt = await repo.GetActiveReceiptByPoIdAsync(TenantId, po.Id);
            Assert.NotNull(activeReceipt);

            // 7. Branch stock & batch
            var bStock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = product.Id,
                BatchCode = "LOT-GRN-1",
                QuantityOnHand = 100,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBranchStockAsync(bStock);

            var batch = new Batch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = product.Id,
                BatchCode = "LOT-GRN-1",
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBatchAsync(batch);
            await context.SaveChangesAsync();

            var foundStock = await repo.GetBranchStockAsync(TenantId, branch.Id, product.Id, "LOT-GRN-1");
            Assert.NotNull(foundStock);

            var productNoBatch = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-NOBATCH",
                Name = "Gạo ST25",
                CategoryId = cat.Id,
                BaseUnit = "Kg",
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(productNoBatch);

            var nonBatchStock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = productNoBatch.Id,
                BatchCode = null,
                QuantityOnHand = 50,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBranchStockAsync(nonBatchStock);
            await context.SaveChangesAsync();

            var foundNonBatch = await repo.GetBranchStockAsync(TenantId, branch.Id, productNoBatch.Id, null);
            Assert.NotNull(foundNonBatch);
            Assert.Equal(50, foundNonBatch.QuantityOnHand);

            var totalStock = await repo.GetTotalProductStockAsync(TenantId, product.Id);
            Assert.Equal(100, totalStock);

            // 8. Product by Id & by multiple IDs
            var prodMap = await repo.GetProductsByIdsAsync(new[] { product.Id });
            Assert.Single(prodMap);

            var emptyProdMap = await repo.GetProductsByIdsAsync(Array.Empty<Guid>());
            Assert.Empty(emptyProdMap);

            var fetchedProd = await repo.GetProductByIdAsync(TenantId, product.Id);
            Assert.NotNull(fetchedProd);

            // 9. Supplier & Ledger
            var fetchedSup = await repo.GetSupplierByIdAsync(TenantId, supplier.Id);
            Assert.NotNull(fetchedSup);

            var supLedger = new SupplierAPLedger
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = supplier.Id,
                DocNo = "DOC-GRN-01",
                DocType = APDocType.PurchaseInvoice,
                Amount = 500000,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddSupplierLedgerAsync(supLedger);
            await context.SaveChangesAsync();

            // 10. UserExists & UpdateAsync
            Assert.False(await repo.UserExistsAsync(""));
            Assert.False(await repo.UserExistsAsync("unknown-user"));

            grn.Status = "Completed";
            await repo.UpdateAsync(grn);
        }
    }

    [Fact]
    public async Task SupplierReturnRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new SupplierReturnRepository(context);

            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "NCC-RET",
                Name = "NCC Trả hàng",
                CreatedAt = DateTime.UtcNow
            };
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-RET",
                Name = "Chi nhánh Trả",
                CreatedAt = DateTime.UtcNow
            };
            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-RET",
                Name = "Thực phẩm",
                CreatedAt = DateTime.UtcNow
            };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-RET",
                Name = "Bánh kẹo",
                CategoryId = cat.Id,
                BaseUnit = "Hộp",
                CreatedAt = DateTime.UtcNow
            };
            var grn = new GoodsReceipt
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "GRN-RET-01",
                SupplierId = supplier.Id,
                BranchId = branch.Id,
                Status = "COMPLETED",
                ReceivedAtUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            var grnLine = new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                GoodsReceiptId = grn.Id,
                ProductId = product.Id,
                Product = product,
                AcceptedQty = 20,
                UnitPrice = 15000,
                BatchCode = "LOT-RET-1",
                CreatedAt = DateTime.UtcNow
            };
            grn.Lines = new List<GoodsReceiptLine> { grnLine };

            var bStock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = product.Id,
                BatchCode = "LOT-RET-1",
                QuantityOnHand = 20,
                CreatedAt = DateTime.UtcNow
            };

            var ret = new SupplierReturn
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "RET001",
                SupplierId = supplier.Id,
                BranchId = branch.Id,
                GoodsReceiptId = grn.Id,
                GoodsReceipt = grn,
                Reason = "Hàng cận date",
                Status = "Draft",
                Supplier = supplier,
                Branch = branch,
                Lines = new List<SupplierReturnLine>(),
                CreatedAt = DateTime.UtcNow
            };
            await context.Suppliers.AddAsync(supplier);
            await context.Branches.AddAsync(branch);
            await context.Categories.AddAsync(cat);
            await context.Products.AddAsync(product);
            await context.BranchStocks.AddAsync(bStock);
            await context.GoodsReceipts.AddAsync(grn);
            await repo.AddAsync(ret);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            var byId = await repo.GetByIdAsync(TenantId, ret.Id);
            Assert.NotNull(byId);
            Assert.Equal("RET001", byId!.Code);

            // 2. GenerateCodeAsync
            var code = await repo.GenerateCodeAsync(TenantId);
            Assert.NotNull(code);

            // 3. GetPagedAsync
            var paged = await repo.GetPagedAsync(TenantId, new SupplierReturnFilterQuery
            {
                Keyword = "RET001",
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(1, paged.TotalItems);

            // 4. GetEligibleGrnsAsync
            var eligibleGrns = await repo.GetEligibleGrnsAsync(TenantId, branch.Id, supplier.Id);
            Assert.Single(eligibleGrns);

            // 5. GetGrnReturnableLinesAsync
            var returnableLines = await repo.GetGrnReturnableLinesAsync(TenantId, grn.Id);
            Assert.Single(returnableLines);
            Assert.Equal(20, returnableLines[0].ReceivedQty);

            // 6. GetBranchStockAsync
            var stock = await repo.GetBranchStockAsync(TenantId, branch.Id, product.Id, "LOT-RET-1");
            Assert.NotNull(stock);

            // 7. UpdateAsync
            ret.Status = "Completed";
            await repo.UpdateAsync(ret);
            await context.SaveChangesAsync();
            var reloaded = await repo.GetByIdAsync(TenantId, ret.Id);
            Assert.Equal("Completed", reloaded!.Status);
        }
    }
}
