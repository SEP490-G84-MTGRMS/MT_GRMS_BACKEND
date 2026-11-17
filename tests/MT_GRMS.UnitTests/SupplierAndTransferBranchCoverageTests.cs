using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;

namespace MT_GRMS.UnitTests;

public class SupplierAndTransferBranchCoverageTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-sup-cov-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class LocalUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }
        public bool BeginTransactionCalled { get; private set; }
        public bool CommitTransactionCalled { get; private set; }
        public bool RollbackTransactionCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            BeginTransactionCalled = true;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitTransactionCalled = true;
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackTransactionCalled = true;
            return Task.CompletedTask;
        }
    }

    private class MemoryRepository<T> : IRepository<T> where T : BaseTenantEntity
    {
        public List<T> Items { get; } = new();

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id && !x.IsDeleted));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.Where(x => !x.IsDeleted).ToList());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).Where(x => !x.IsDeleted).ToList());

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            if (!Items.Any(i => i.Id == entity.Id)) Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity)
        {
            var idx = Items.FindIndex(x => x.Id == entity.Id);
            if (idx >= 0) Items[idx] = entity;
            else Items.Add(entity);
        }

        public void Delete(T entity) => entity.IsDeleted = true;
    }

    // =========================================================================
    // SECTION 1: SupplierService Branches
    // =========================================================================

    [Fact]
    public async Task SupplierService_NarrowingSupplierType_ThrowsBusinessException_WhenLedgerTransactionsExist()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<ISupplierRepository>();

        var supplierId = Guid.NewGuid();
        var supplier = new Supplier
        {
            Id = supplierId,
            TenantId = tenant.CurrentTenantId,
            Code = "SUP-01",
            Name = "Supplier Both",
            Phone = "0987654321",
            SupplierType = SupplierType.Both,
            Status = SupplierStatus.Active
        };

        mockRepo.Setup(r => r.GetByIdWithDetailsAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        // Case 1: Both -> Goods with ServiceInvoice exists
        mockRepo.Setup(r => r.HasLedgerTransactionsAsync(supplierId, APDocType.ServiceInvoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new SupplierService(mockRepo.Object, uow, tenant);

        var cmdToGoods = new UpdateSupplierCommand
        {
            Name = "Supplier Goods Only",
            SupplierType = SupplierType.Goods,
            Phone = "0987654321"
        };

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateSupplierAsync(supplierId, cmdToGoods, CancellationToken.None));
        Assert.Equal("INVALID_SUPPLIER_TYPE_NARROWING", ex1.ErrorCode);

        // Case 2: Both -> Service with PurchaseInvoice exists
        mockRepo.Setup(r => r.HasLedgerTransactionsAsync(supplierId, APDocType.PurchaseInvoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cmdToService = new UpdateSupplierCommand
        {
            Name = "Supplier Service Only",
            SupplierType = SupplierType.Service,
            Phone = "0987654321"
        };

        var ex2 = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateSupplierAsync(supplierId, cmdToService, CancellationToken.None));
        Assert.Equal("INVALID_SUPPLIER_TYPE_NARROWING", ex2.ErrorCode);

        // Case 3: Narrowing with NO historical transactions succeeds
        mockRepo.Setup(r => r.HasLedgerTransactionsAsync(supplierId, APDocType.ServiceInvoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var res = await service.UpdateSupplierAsync(supplierId, cmdToGoods, CancellationToken.None);
        Assert.NotNull(res);
        Assert.Equal(SupplierType.Goods, res.SupplierType);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task SupplierService_UpdateBasicSupplier_Branches_HandlesNotFoundAndNullableFields()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<ISupplierRepository>();
        var service = new SupplierService(mockRepo.Object, uow, tenant);

        var nonExistentId = Guid.NewGuid();
        mockRepo.Setup(r => r.GetByIdWithDetailsAsync(nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateBasicSupplierAsync(nonExistentId, new UpdateBasicSupplierCommand { Name = "X", Phone = "0123" }, CancellationToken.None));

        var existingId = Guid.NewGuid();
        var existingSupplier = new Supplier
        {
            Id = existingId,
            TenantId = tenant.CurrentTenantId,
            Code = "SUP-BASIC",
            Name = "Old Name",
            Phone = "0111111111",
            Email = "old@example.com",
            TaxCode = "TAX-OLD",
            Address = "Address Old",
            Status = SupplierStatus.Active
        };

        mockRepo.Setup(r => r.GetByIdWithDetailsAsync(existingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSupplier);

        var cmd = new UpdateBasicSupplierCommand
        {
            Name = " New Name ",
            ContactPerson = "  ",
            Phone = "0999999999",
            Status = SupplierStatus.Inactive,
            Email = "   ", // whitespace should become null
            TaxCode = " 0312345678 ",
            Address = " 123 New Road "
        };

        var result = await service.UpdateBasicSupplierAsync(existingId, cmd, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("New Name", result.Name);
        Assert.Null(existingSupplier.Email);
        Assert.Equal("0312345678", existingSupplier.TaxCode);
        Assert.Equal("123 New Road", existingSupplier.Address);
        Assert.Equal(SupplierStatus.Inactive, existingSupplier.Status);
    }

    [Fact]
    public async Task SupplierService_UpsertReturnPolicy_Validation_AllBranches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<ISupplierRepository>();
        var service = new SupplierService(mockRepo.Object, uow, tenant);
        var supplierId = Guid.NewGuid();

        var validSupplier = new Supplier
        {
            Id = supplierId,
            TenantId = tenant.CurrentTenantId,
            Code = "SUP-POL",
            Name = "Supplier Policy",
            Phone = "0900000000",
            Status = SupplierStatus.Active
        };
        mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validSupplier);

        // 1. Empty AllowedReturnTypes
        var cmd1 = new UpdateSupplierReturnPolicyCommand { AllowedReturnTypes = [] };
        var ex1 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd1, CancellationToken.None));
        Assert.Equal("INVALID_RETURN_TYPES", ex1.ErrorCode);

        // 2. Empty AllowedResolutionTypes
        var cmd2 = new UpdateSupplierReturnPolicyCommand { AllowedReturnTypes = ["QUALITY_DEFECT"], AllowedResolutionTypes = [] };
        var ex2 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd2, CancellationToken.None));
        Assert.Equal("INVALID_RESOLUTION_TYPES", ex2.ErrorCode);

        // 3. Default resolution not in allowed list
        var cmd3 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REFUND"
        };
        var ex3 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd3, CancellationToken.None));
        Assert.Equal("INVALID_DEFAULT_RESOLUTION", ex3.ErrorCode);

        // 4. CostShareEnabled without StoreCostSharePercent
        var cmd4 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REDUCE_PAYABLE",
            CostShareEnabled = true,
            StoreCostSharePercent = null
        };
        var ex4 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd4, CancellationToken.None));
        Assert.Equal("INVALID_COST_SHARE", ex4.ErrorCode);

        // 5. StoreCostSharePercent out of bounds (< 0 or > 100)
        var cmd5 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REDUCE_PAYABLE",
            CostShareEnabled = true,
            StoreCostSharePercent = 120m
        };
        var ex5 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd5, CancellationToken.None));
        Assert.Equal("INVALID_COST_SHARE", ex5.ErrorCode);

        // 6. Store + Supplier percent != 100
        var cmd6 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REDUCE_PAYABLE",
            CostShareEnabled = true,
            StoreCostSharePercent = 30m,
            SupplierCostSharePercent = 60m
        };
        var ex6 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd6, CancellationToken.None));
        Assert.Equal("INVALID_COST_SHARE", ex6.ErrorCode);

        // 7. CostShareByCategory: Duplicate CategoryId
        var catId = Guid.NewGuid();
        var cmd7 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REDUCE_PAYABLE",
            CostShareEnabled = true,
            StoreCostSharePercent = 30m,
            SupplierCostSharePercent = 70m,
            CostShareByCategoryEnabled = true,
            CategoryCostShares =
            [
                new SupplierReturnPolicyCategoryCostShareDto { CategoryId = catId, StoreCostSharePercent = 30m, SupplierCostSharePercent = 70m },
                new SupplierReturnPolicyCategoryCostShareDto { CategoryId = catId, StoreCostSharePercent = 40m, SupplierCostSharePercent = 60m }
            ]
        };
        var ex7 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd7, CancellationToken.None));
        Assert.Equal("INVALID_COST_SHARE_CATEGORY", ex7.ErrorCode);

        // 8. CostShareByCategory: Empty CategoryId
        var cmd8 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REDUCE_PAYABLE",
            CostShareEnabled = true,
            StoreCostSharePercent = 30m,
            SupplierCostSharePercent = 70m,
            CostShareByCategoryEnabled = true,
            CategoryCostShares =
            [
                new SupplierReturnPolicyCategoryCostShareDto { CategoryId = Guid.Empty, StoreCostSharePercent = 30m, SupplierCostSharePercent = 70m }
            ]
        };
        var ex8 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd8, CancellationToken.None));
        Assert.Equal("INVALID_COST_SHARE_CATEGORY", ex8.ErrorCode);

        // 9. CostShareByCategory: category row percent out of bounds or sum != 100
        var cmd9 = new UpdateSupplierReturnPolicyCommand
        {
            AllowedReturnTypes = ["QUALITY_DEFECT"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE"],
            DefaultResolutionType = "REDUCE_PAYABLE",
            CostShareEnabled = true,
            StoreCostSharePercent = 30m,
            SupplierCostSharePercent = 70m,
            CostShareByCategoryEnabled = true,
            CategoryCostShares =
            [
                new SupplierReturnPolicyCategoryCostShareDto { CategoryId = Guid.NewGuid(), StoreCostSharePercent = 30m, SupplierCostSharePercent = 50m }
            ]
        };
        var ex9 = await Assert.ThrowsAsync<BusinessException>(() => service.UpsertReturnPolicyAsync(supplierId, cmd9, CancellationToken.None));
        Assert.Equal("INVALID_COST_SHARE_CATEGORY", ex9.ErrorCode);

        // 10. Valid Upsert - creates new policy when none exists
        var validCatId = Guid.NewGuid();
        var validCmd = new UpdateSupplierReturnPolicyCommand
        {
            AllowReturn = true,
            AllowedReturnTypes = ["QUALITY_DEFECT", "WRONG_ITEM"],
            AllowedResolutionTypes = ["REDUCE_PAYABLE", "EXCHANGE"],
            DefaultResolutionType = "EXCHANGE",
            CostShareEnabled = true,
            StoreCostSharePercent = 40m,
            SupplierCostSharePercent = 60m,
            CostShareByCategoryEnabled = true,
            CategoryCostShares =
            [
                new SupplierReturnPolicyCategoryCostShareDto { CategoryId = validCatId, StoreCostSharePercent = 50m, SupplierCostSharePercent = 50m }
            ]
        };

        mockRepo.Setup(r => r.GetActiveReturnPolicyAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierReturnPolicy?)null);

        var createdPolicy = await service.UpsertReturnPolicyAsync(supplierId, validCmd, CancellationToken.None);
        Assert.NotNull(createdPolicy);
        Assert.Equal("EXCHANGE", createdPolicy.DefaultResolutionType);
        Assert.True(createdPolicy.CostShareEnabled);
        Assert.Single(createdPolicy.CategoryCostShares);
        mockRepo.Verify(r => r.AddReturnPolicyAsync(It.IsAny<SupplierReturnPolicy>(), It.IsAny<CancellationToken>()), Times.Once);

        // 11. Valid Upsert - updates existing policy
        var existingPolicy = new SupplierReturnPolicy
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            AllowReturn = false,
            CategoryCostShares = new List<SupplierReturnPolicyCategoryCostShare>
            {
                new() { Id = Guid.NewGuid(), CategoryId = validCatId, StoreCostSharePercent = 10m, SupplierCostSharePercent = 90m }
            }
        };

        mockRepo.Setup(r => r.GetActiveReturnPolicyAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPolicy);

        var updatedPolicy = await service.UpsertReturnPolicyAsync(supplierId, validCmd, CancellationToken.None);
        Assert.NotNull(updatedPolicy);
        Assert.True(updatedPolicy.AllowReturn);
        Assert.Equal(50m, updatedPolicy.CategoryCostShares.First().StoreCostSharePercent);
    }

    [Fact]
    public async Task SupplierService_DeliverySchedule_And_DeliveryDaysMask_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<ISupplierRepository>();
        var service = new SupplierService(mockRepo.Object, uow, tenant);

        var supplierId = Guid.NewGuid();
        mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetDeliveryConfigAsync(supplierId, CancellationToken.None));

        // Supplier with corrupted DeliveryDaysJson falls back to MaskToDayNames
        var supplier = new Supplier
        {
            Id = supplierId,
            TenantId = tenant.CurrentTenantId,
            Code = "SUP-DELIV",
            Name = "Supplier Delivery",
            Phone = "0912345678",
            DeliveryDaysJson = "{ invalid json }",
            DeliveryWeekdaysMask = (1 << 1) | (1 << 3) | (1 << 5) // Mon (1), Wed (3), Fri (5)
        };

        mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);
        mockRepo.Setup(r => r.GetBlackoutDatesAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierBlackoutDate>());

        var schedule = await service.GetDeliveryConfigAsync(supplierId, CancellationToken.None);
        Assert.NotNull(schedule);
        Assert.Contains("Tue", schedule.DeliveryDays);
        Assert.Contains("Thu", schedule.DeliveryDays);
        Assert.Contains("Sat", schedule.DeliveryDays);

        // Supplier with valid DeliveryDaysJson
        supplier.DeliveryDaysJson = "[\"Mon\",\"Tue\",\"Wed\"]";
        var schedule2 = await service.GetDeliveryConfigAsync(supplierId, CancellationToken.None);
        Assert.NotNull(schedule2);
        Assert.Equal(3, schedule2.DeliveryDays.Count);
    }

    [Fact]
    public async Task SupplierService_UpdateSupplierProduct_PrimaryExclusive_And_DeliveryUoMFallback()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<ISupplierRepository>();
        var service = new SupplierService(mockRepo.Object, uow, tenant);

        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // 1. Not found link throws
        mockRepo.Setup(r => r.GetSupplierProductAsync(supplierId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierProduct?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateSupplierProductAsync(supplierId, productId, new UpdateSupplierProductCommand(), CancellationToken.None));

        // 2. Clear primary supplier flag if promoted to primary
        var product = new Product
        {
            Id = productId,
            TenantId = tenant.CurrentTenantId,
            ProductCode = "P001",
            Name = "Product 1",
            BaseUnit = "Hộp",
            Units = new List<ProductUnit>
            {
                new() { Id = Guid.NewGuid(), UnitName = "Thùng", IsPurchaseUnit = true, IsDeleted = false }
            }
        };

        var spEntity = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            ProductId = productId,
            IsPrimarySupplier = false,
            DeliveryUoM = "Cái",
            Product = product,
            Supplier = new Supplier { Id = supplierId, Name = "Sup Test", Code = "SUP-01" }
        };

        mockRepo.Setup(r => r.GetSupplierProductAsync(supplierId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(spEntity);

        var cmd = new UpdateSupplierProductCommand
        {
            IsPrimarySupplier = true,
            DeliveryUoM = "", // empty triggers fallback
            MinimumOrderQuantity = 5,
            AgreedWholesalePrice = 200000m
        };

        var updated = await service.UpdateSupplierProductAsync(supplierId, productId, cmd, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.True(updated.IsPrimarySupplier);
        // Resolved from product purchase unit: "Thùng"
        Assert.Equal("Thùng", updated.DeliveryUoM);
        mockRepo.Verify(r => r.ClearPrimarySupplierFlagAsync(productId, supplierId, It.IsAny<CancellationToken>()), Times.Once);

        // 3. Fallback when product has no purchase units -> uses BaseUnit
        product.Units.Clear();
        cmd.DeliveryUoM = "   ";
        cmd.IsPrimarySupplier = false;
        spEntity.IsPrimarySupplier = true;

        var updated2 = await service.UpdateSupplierProductAsync(supplierId, productId, cmd, CancellationToken.None);
        Assert.Equal("Hộp", updated2.DeliveryUoM);

        // 4. Fallback when BaseUnit is empty -> uses "Cái"
        product.BaseUnit = "";
        var updated3 = await service.UpdateSupplierProductAsync(supplierId, productId, cmd, CancellationToken.None);
        Assert.Equal("Cái", updated3.DeliveryUoM);
    }

    // =========================================================================
    // SECTION 2: PromotionService Branches
    // =========================================================================

    [Fact]
    public async Task PromotionService_EvaluatePromotions_AllDiscountAndGiftBranches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<IPromotionRepository>();
        var service = new PromotionService(mockRepo.Object, uow, tenant);

        var prodAId = Guid.NewGuid();
        var prodBId = Guid.NewGuid();
        var giftProdId = Guid.NewGuid();

        // 1. ORDER_DISCOUNT capped by remaining budget
        var promoOrder = new Promotion
        {
            Id = Guid.NewGuid(),
            PromotionCode = "ORDER10",
            PromotionName = "Giảm 10%",
            Type = "ORDER_DISCOUNT",
            DiscountValue = 10m,
            MinOrderValue = 100000m,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(5),
            IsActive = true,
            BudgetLimit = 50000m,
            TotalDiscountGiven = 40000m // remaining budget = 10,000m
        };

        // 2. PRODUCT_DISCOUNT with MaxDiscountAmount cap & conversion rate
        var promoProduct = new Promotion
        {
            Id = Guid.NewGuid(),
            PromotionCode = "PROD_CAP",
            PromotionName = "Giảm SP",
            Type = "PRODUCT_DISCOUNT",
            DiscountValue = 20m, // 20%
            MaxDiscountAmount = 15000m, // capped at 15k
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(5),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new() { ProductId = prodAId, ProductRole = "DISCOUNT", MinQty = 1, DiscountValue = 20m }
            }
        };

        // 3. BUY_X_GET_Y with conditions fallback without BUY conditions
        var promoGift = new Promotion
        {
            Id = Guid.NewGuid(),
            PromotionCode = "GIFT_ALL",
            PromotionName = "Tặng quà chung",
            Type = "BUY_X_GET_Y",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(5),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new()
                {
                    ProductId = giftProdId,
                    ProductRole = "GIFT",
                    MinQty = 2,
                    Product = new Product { Id = giftProdId, Name = "Ly Thủy Tinh", SellingPrice = 20000m }
                }
            }
        };

        // 4. Promo with UsageLimit reached
        var promoLimit = new Promotion
        {
            Id = Guid.NewGuid(),
            PromotionCode = "EXHAUSTED",
            PromotionName = "Hết lượt",
            Type = "ORDER_DISCOUNT",
            DiscountValue = 50m,
            MinOrderValue = 50000m,
            UsageLimit = 10,
            UsedCount = 10,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(5),
            IsActive = true
        };

        mockRepo.Setup(r => r.GetActivePromotionsAsync(It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Promotion> { promoOrder, promoProduct, promoGift, promoLimit });

        var cart = new EvaluateCartRequest(
            Items:
            [
                new CartItemDto(prodAId, "Sản phẩm A", unitPrice: 100000m, quantity: 2m), // Line subtotal 200,000 -> 20% is 40,000 capped to 15,000
                new CartItemDto(prodBId, "Sản phẩm B", unitPrice: 50000m, quantity: 1m)
            ],
            SubTotal: 250000m
        );

        var evalResult = await service.EvaluateCartAsync(cart, CancellationToken.None);
        Assert.NotNull(evalResult);
        // Best offer should have picked one of the eligible discounts
        Assert.True(evalResult.TotalDiscountAmount > 0 || evalResult.GiftItems.Count > 0);
    }

    [Fact]
    public async Task PromotionService_ValidateVoucher_AllBranches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<IPromotionRepository>();
        var service = new PromotionService(mockRepo.Object, uow, tenant);

        // 1. Empty Code
        var res1 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("", 100000m), CancellationToken.None);
        Assert.False(res1.IsValid);
        Assert.Equal("Mã voucher không được để trống", res1.Message);

        // 2. Negative CartTotal
        var res2 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("VOUCH01", -500m), CancellationToken.None);
        Assert.False(res2.IsValid);
        Assert.Equal("Tổng giá trị đơn hàng không được âm", res2.Message);

        // 3. Voucher code not found
        mockRepo.Setup(r => r.GetVoucherCodeAsync("NOTFOUND", It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoucherCode?)null);
        var res3 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("NOTFOUND", 100000m), CancellationToken.None);
        Assert.False(res3.IsValid);
        Assert.Equal("Mã voucher không tồn tại", res3.Message);

        // 4. Voucher status != UNUSED
        var usedCode = new VoucherCode { Code = "USED01", Status = "USED" };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("USED01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedCode);
        var res4 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("USED01", 100000m), CancellationToken.None);
        Assert.False(res4.IsValid);
        Assert.Contains("không khả dụng", res4.Message);

        // 5. Inactive / deleted promo
        var inactivePromoCode = new VoucherCode
        {
            Code = "INACTIVE",
            Status = "UNUSED",
            Promotion = new Promotion { IsActive = false, IsDeleted = false }
        };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("INACTIVE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactivePromoCode);
        var res5 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("INACTIVE", 100000m), CancellationToken.None);
        Assert.False(res5.IsValid);
        Assert.Equal("Chương trình ưu đãi của voucher này đã bị ngưng", res5.Message);

        // 6. Voucher start date in future
        var futurePromoCode = new VoucherCode
        {
            Code = "FUTURE",
            Status = "UNUSED",
            Promotion = new Promotion
            {
                IsActive = true,
                StartDate = DateTime.UtcNow.AddDays(5),
                EndDate = DateTime.UtcNow.AddDays(10)
            }
        };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("FUTURE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(futurePromoCode);
        var res6 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("FUTURE", 100000m), CancellationToken.None);
        Assert.False(res6.IsValid);
        Assert.Contains("chỉ có hiệu lực từ ngày", res6.Message);

        // 7. Expired voucher
        var expiredPromoCode = new VoucherCode
        {
            Code = "EXPIRED",
            Status = "UNUSED",
            Promotion = new Promotion
            {
                IsActive = true,
                StartDate = DateTime.UtcNow.AddDays(-10),
                EndDate = DateTime.UtcNow.AddDays(-2)
            }
        };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("EXPIRED", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredPromoCode);
        var res7 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("EXPIRED", 100000m), CancellationToken.None);
        Assert.False(res7.IsValid);
        Assert.Contains("đã hết hạn", res7.Message);

        // 8. CustomerId restricted mismatch vs match
        var restrictedCustId = Guid.NewGuid();
        var restrictedCode = new VoucherCode
        {
            Code = "VIP_ONLY",
            Status = "UNUSED",
            CustomerId = restrictedCustId,
            Promotion = new Promotion
            {
                IsActive = true,
                StartDate = DateTime.UtcNow.AddDays(-2),
                EndDate = DateTime.UtcNow.AddDays(5),
                MinOrderValue = 50000m,
                DiscountValue = 10000m
            }
        };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("VIP_ONLY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(restrictedCode);

        // Mismatch customer
        var res8a = await service.ValidateVoucherAsync(new ValidateVoucherRequest("VIP_ONLY", 100000m, CustomerId: Guid.NewGuid().ToString()), CancellationToken.None);
        Assert.False(res8a.IsValid);
        Assert.Equal("Voucher này được cấp riêng cho khách hàng khác", res8a.Message);

        // Match customer
        var res8b = await service.ValidateVoucherAsync(new ValidateVoucherRequest("VIP_ONLY", 100000m, CustomerId: restrictedCustId.ToString()), CancellationToken.None);
        Assert.True(res8b.IsValid);
        Assert.Equal(10000m, res8b.DiscountAmount);

        // 9. Cart total < MinOrderValue
        var highMinCode = new VoucherCode
        {
            Code = "MIN500K",
            Status = "UNUSED",
            Promotion = new Promotion
            {
                IsActive = true,
                StartDate = DateTime.UtcNow.AddDays(-2),
                EndDate = DateTime.UtcNow.AddDays(5),
                MinOrderValue = 500000m,
                DiscountValue = 50000m
            }
        };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("MIN500K", It.IsAny<CancellationToken>()))
            .ReturnsAsync(highMinCode);
        var res9 = await service.ValidateVoucherAsync(new ValidateVoucherRequest("MIN500K", 200000m), CancellationToken.None);
        Assert.False(res9.IsValid);
        Assert.Contains("chưa đạt giá trị tối thiểu", res9.Message);

        // 10. Disallow simultaneous promotion
        var noStackCode = new VoucherCode
        {
            Code = "NOSTACK",
            Status = "UNUSED",
            Promotion = new Promotion
            {
                IsActive = true,
                StartDate = DateTime.UtcNow.AddDays(-2),
                EndDate = DateTime.UtcNow.AddDays(5),
                MinOrderValue = 50000m,
                DiscountValue = 15m, // 15%
                MaxDiscountAmount = 30000m,
                Description = "[CONFIG_JSON]{\"allowWithPromotions\":false}[/CONFIG_JSON]"
            }
        };
        mockRepo.Setup(r => r.GetVoucherCodeAsync("NOSTACK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(noStackCode);

        var res10a = await service.ValidateVoucherAsync(new ValidateVoucherRequest("NOSTACK", 200000m, HasOtherPromotions: true), CancellationToken.None);
        Assert.False(res10a.IsValid);
        Assert.Contains("không áp dụng đồng thời", res10a.Message);

        // PERCENT capped at MaxDiscountAmount
        var res10b = await service.ValidateVoucherAsync(new ValidateVoucherRequest("NOSTACK", 400000m, HasOtherPromotions: false), CancellationToken.None);
        Assert.True(res10b.IsValid);
        Assert.Equal(30000m, res10b.DiscountAmount);
    }

    [Fact]
    public async Task PromotionService_VoucherBatchById_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();
        var mockRepo = new Mock<IPromotionRepository>();
        var service = new PromotionService(mockRepo.Object, uow, tenant);

        var id = Guid.NewGuid();

        // 1. Not found
        mockRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Promotion?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetVoucherBatchByIdAsync(id, CancellationToken.None));

        // 2. Not VOUCHER type
        var orderPromo = new Promotion { Id = id, Type = "ORDER_DISCOUNT", PromotionCode = "P01" };
        mockRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderPromo);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetVoucherBatchByIdAsync(id, CancellationToken.None));

        // 3. Valid voucher batch
        var voucherPromo = new Promotion
        {
            Id = id,
            Type = "VOUCHER",
            PromotionCode = "VB_2026",
            PromotionName = "Voucher Batch Tet",
            VoucherPrefix = "TET",
            TotalVoucherQuantity = 100,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        };
        mockRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(voucherPromo);

        var res = await service.GetVoucherBatchByIdAsync(id, CancellationToken.None);
        Assert.NotNull(res);
        Assert.Equal("VB_2026", res.BatchCode);
    }

    // =========================================================================
    // SECTION 3: TransferService Branches
    // =========================================================================

    [Fact]
    public async Task TransferService_ResolveStockSource_And_DispatchBackfill_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();

        var transfers = new MemoryRepository<Transfer>();
        var details = new MemoryRepository<TransferDetail>();
        var stocks = new MemoryRepository<BranchStock>();
        var products = new MemoryRepository<Product>();
        var branches = new MemoryRepository<Branch>();

        var branchFromId = Guid.NewGuid();
        var branchToId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        branches.Items.Add(new Branch { Id = branchFromId, TenantId = tenant.CurrentTenantId, Name = "Branch From", IsActive = true });
        branches.Items.Add(new Branch { Id = branchToId, TenantId = tenant.CurrentTenantId, Name = "Branch To", IsActive = true });
        products.Items.Add(new Product { Id = prodId, TenantId = tenant.CurrentTenantId, Name = "Product Transfer", ProductCode = "PTRF01" });

        var service = new TransferService(transfers, details, stocks, products, branches, tenant, uow);

        // 1. Dispatch with ZERO stock throws
        var trf1 = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            FromBranchId = branchFromId,
            ToBranchId = branchToId,
            Status = TransferStatus.Draft,
            TransferCode = "TRF-001"
        };
        transfers.Items.Add(trf1);
        details.Items.Add(new TransferDetail
        {
            Id = Guid.NewGuid(),
            TransferId = trf1.Id,
            ProductId = prodId,
            SentQuantity = 10m
        });

        var exNoStock = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DispatchTransferAsync(trf1.Id, CancellationToken.None));
        Assert.Contains("Không tìm thấy tồn kho cho sản phẩm này", exNoStock.Message);

        // 2. Dispatch with insufficient stock throws
        var singleStock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchFromId,
            ProductId = prodId,
            BatchCode = "B-AUTO",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 5m,
            QuantityReserved = 0m,
            QuantityQuarantined = 0m
        };
        stocks.Items.Add(singleStock);

        var exLowStock = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DispatchTransferAsync(trf1.Id, CancellationToken.None));
        Assert.Contains("Không đủ tồn khả dụng", exLowStock.Message);

        // 3. Dispatch succeeds and backfills line.BatchNumber and line.ExpiryDate
        singleStock.QuantityOnHand = 20m;
        var dispatchResult = await service.DispatchTransferAsync(trf1.Id, CancellationToken.None);
        Assert.NotNull(dispatchResult);
        Assert.Equal(TransferStatus.InTransit.ToString(), dispatchResult.Status);
        Assert.Equal(10m, singleStock.QuantityOnHand); // 20 - 10 = 10

        var line = details.Items.First(d => d.TransferId == trf1.Id);
        Assert.Equal("B-AUTO", line.BatchNumber);
        Assert.NotNull(line.ExpiryDate);

        // 4. Dispatch non-draft throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DispatchTransferAsync(trf1.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TransferService_ReceiveTransfer_QuarantinesDamaged_And_CreatesMissingDestStock()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();

        var transfers = new MemoryRepository<Transfer>();
        var details = new MemoryRepository<TransferDetail>();
        var stocks = new MemoryRepository<BranchStock>();
        var products = new MemoryRepository<Product>();
        var branches = new MemoryRepository<Branch>();

        var branchFromId = Guid.NewGuid();
        var branchToId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        branches.Items.Add(new Branch { Id = branchFromId, TenantId = tenant.CurrentTenantId, Name = "Branch From", IsActive = true });
        branches.Items.Add(new Branch { Id = branchToId, TenantId = tenant.CurrentTenantId, Name = "Branch To", IsActive = true });
        products.Items.Add(new Product { Id = prodId, TenantId = tenant.CurrentTenantId, Name = "Product Transfer", ProductCode = "PTRF01" });

        var service = new TransferService(transfers, details, stocks, products, branches, tenant, uow);

        var trf = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            FromBranchId = branchFromId,
            ToBranchId = branchToId,
            Status = TransferStatus.InTransit,
            TransferCode = "TRF-REC-01"
        };
        transfers.Items.Add(trf);

        var detailLine = new TransferDetail
        {
            Id = Guid.NewGuid(),
            TransferId = trf.Id,
            ProductId = prodId,
            SentQuantity = 10m,
            BatchNumber = "B-REC-01",
            UnitPrice = 50000m
        };
        details.Items.Add(detailLine);

        // Act: Receive 8 normal, 2 damaged -> destStock created withOnHand=10, Quarantined=2
        var receiveReq = new ReceiveTransferRequest(
            Note: "Hỏng 2 cái do vận chuyển",
            Items:
            [
                new ReceiveTransferLineRequest(detailLine.Id, ReceivedQuantity: 8m, DamagedQuantity: 2m, DiscrepancyReason: "Vỡ hộp")
            ]
        );

        var receiveResult = await service.ReceiveTransferAsync(trf.Id, receiveReq, CancellationToken.None);
        Assert.NotNull(receiveResult);
        Assert.Equal(TransferStatus.Received.ToString(), receiveResult.Status);

        var destStock = stocks.Items.FirstOrDefault(s => s.BranchId == branchToId && s.ProductId == prodId);
        Assert.NotNull(destStock);
        Assert.Equal(10m, destStock.QuantityOnHand);
        Assert.Equal(2m, destStock.QuantityQuarantined);
    }

    [Fact]
    public async Task TransferService_CancelTransfer_RestoresStock_And_EnforcesPermissions()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();

        var transfers = new MemoryRepository<Transfer>();
        var details = new MemoryRepository<TransferDetail>();
        var stocks = new MemoryRepository<BranchStock>();
        var products = new MemoryRepository<Product>();
        var branches = new MemoryRepository<Branch>();

        var branchFromId = Guid.NewGuid();
        var branchToId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        branches.Items.Add(new Branch { Id = branchFromId, TenantId = tenant.CurrentTenantId, Name = "Branch From", IsActive = true });
        branches.Items.Add(new Branch { Id = branchToId, TenantId = tenant.CurrentTenantId, Name = "Branch To", IsActive = true });
        products.Items.Add(new Product { Id = prodId, TenantId = tenant.CurrentTenantId, Name = "Product Transfer", ProductCode = "PTRF01" });

        var stockSource = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchFromId,
            ProductId = prodId,
            BatchCode = "B-CANCEL",
            QuantityOnHand = 40m
        };
        stocks.Items.Add(stockSource);

        var service = new TransferService(transfers, details, stocks, products, branches, tenant, uow);

        var trf = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            FromBranchId = branchFromId,
            ToBranchId = branchToId,
            Status = TransferStatus.InTransit,
            TransferCode = "TRF-CAN-01"
        };
        transfers.Items.Add(trf);
        details.Items.Add(new TransferDetail
        {
            Id = Guid.NewGuid(),
            TransferId = trf.Id,
            ProductId = prodId,
            SentQuantity = 15m,
            BatchNumber = "B-CANCEL"
        });

        // 1. Cancel without reason throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelTransferAsync(trf.Id, new CancelTransferRequest(""), CancellationToken.None));

        // 2. Cancel InTransit as unauthorized role throws
        tenant.SetRole("CUSTOMER");
        tenant.CurrentBranchId = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CancelTransferAsync(trf.Id, new CancelTransferRequest("Lý do"), CancellationToken.None));

        // 3. Cancel InTransit as Owner restores stock
        tenant.SetRole("STORE_OWNER");
        tenant.CurrentBranchId = branchFromId;
        var cancelResult = await service.CancelTransferAsync(trf.Id, new CancelTransferRequest("Khách hủy đơn"), CancellationToken.None);
        Assert.NotNull(cancelResult);
        Assert.Equal(TransferStatus.Cancelled.ToString(), cancelResult.Status);
        Assert.Equal(55m, stockSource.QuantityOnHand); // 40 + 15 = 55

        // 4. Cancel already cancelled transfer throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelTransferAsync(trf.Id, new CancelTransferRequest("Hủy lại"), CancellationToken.None));
    }

    [Fact]
    public async Task TransferService_GetTransfers_FilterAndMatchStatus_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new LocalUnitOfWork();

        var transfers = new MemoryRepository<Transfer>();
        var details = new MemoryRepository<TransferDetail>();
        var stocks = new MemoryRepository<BranchStock>();
        var products = new MemoryRepository<Product>();
        var branches = new MemoryRepository<Branch>();

        var branchFromId = Guid.NewGuid();
        var branchToId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        branches.Items.Add(new Branch { Id = branchFromId, TenantId = tenant.CurrentTenantId, Name = "Branch From", IsActive = true });
        branches.Items.Add(new Branch { Id = branchToId, TenantId = tenant.CurrentTenantId, Name = "Branch To", IsActive = true });
        products.Items.Add(new Product { Id = prodId, TenantId = tenant.CurrentTenantId, Name = "Product Transfer", ProductCode = "PTRF01" });

        var service = new TransferService(transfers, details, stocks, products, branches, tenant, uow);

        // 1. Empty CurrentTenantId returns empty
        tenant.CurrentTenantId = "";
        var emptyResult = await service.GetTransfersAsync(new TransferFilterParams(null, null, null, null, null, null), CancellationToken.None);
        Assert.Empty(emptyResult.Items);
        tenant.CurrentTenantId = "tenant-sup-cov-01";

        // Seed 2 transfers: 1 match, 1 mismatch
        var trfMatch = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            FromBranchId = branchFromId,
            ToBranchId = branchToId,
            Status = TransferStatus.Received,
            TransferCode = "TRF-MATCH-01",
            ReceivedAt = DateTime.UtcNow
        };
        transfers.Items.Add(trfMatch);
        details.Items.Add(new TransferDetail
        {
            Id = Guid.NewGuid(),
            TransferId = trfMatch.Id,
            ProductId = prodId,
            SentQuantity = 5m,
            ReceivedQuantity = 5m,
            DamagedQuantity = 0m
        });

        var trfMismatch = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            FromBranchId = branchFromId,
            ToBranchId = branchToId,
            Status = TransferStatus.Received,
            TransferCode = "TRF-MISMATCH-02",
            ReceivedAt = DateTime.UtcNow
        };
        transfers.Items.Add(trfMismatch);
        details.Items.Add(new TransferDetail
        {
            Id = Guid.NewGuid(),
            TransferId = trfMismatch.Id,
            ProductId = prodId,
            SentQuantity = 5m,
            ReceivedQuantity = 4m,
            DamagedQuantity = 1m
        });

        // 2. Filter ReceiveStatus = "match"
        var matchFilter = new TransferFilterParams(null, null, null, null, null, null, DateField: "receive", ReceiveStatus: "match");
        var matchRes = await service.GetTransfersAsync(matchFilter, CancellationToken.None);
        Assert.Single(matchRes.Items);
        Assert.Equal("TRF-MATCH-01", matchRes.Items[0].TransferCode);

        // 3. Filter ReceiveStatus = "mismatch"
        var mismatchFilter = new TransferFilterParams(null, null, null, null, null, null, DateField: "receive", ReceiveStatus: "mismatch");
        var mismatchRes = await service.GetTransfersAsync(mismatchFilter, CancellationToken.None);
        Assert.Single(mismatchRes.Items);
        Assert.Equal("TRF-MISMATCH-02", mismatchRes.Items[0].TransferCode);

        // 4. Filter by Search
        var searchFilter = new TransferFilterParams("MISMATCH", null, null, null, null, null);
        var searchRes = await service.GetTransfersAsync(searchFilter, CancellationToken.None);
        Assert.Single(searchRes.Items);
    }

    // =========================================================================
    // SECTION 4: PurchaseOrderRepository Branches (InMemory DbContext)
    // =========================================================================

    [Fact]
    public async Task PurchaseOrderRepository_GetOrderMatrixProducts_And_DeliveryConfigs_Branches()
    {
        var tenant = new FakeTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PoRepoCov_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options, tenant);
        var repo = new PurchaseOrderRepository(dbContext, tenant);

        var branchId = Guid.NewGuid();
        var supplierActiveId = Guid.NewGuid();
        var supplierInactiveId = Guid.NewGuid();
        var prodAId = Guid.NewGuid();
        var prodBId = Guid.NewGuid();

        // 1. Calling with NO supplier assignments returns empty array
        var emptyMatrix = await repo.GetMatrixProductsAsync(branchId, supplierActiveId, CancellationToken.None);
        Assert.Empty(emptyMatrix);

        var branch = new Branch
        {
            Id = branchId,
            TenantId = tenant.CurrentTenantId,
            BranchCode = "BR-MAT",
            Name = "Chi nhánh Ma trận",
            IsActive = true
        };
        dbContext.Branches.Add(branch);

        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            CategoryCode = "CAT-01",
            Name = "Danh mục chung"
        };
        dbContext.Categories.Add(category);

        // 2. Seed suppliers and assignments
        var supActive = new Supplier
        {
            Id = supplierActiveId,
            TenantId = tenant.CurrentTenantId,
            Code = "SUP-ACT",
            Name = "NCC Hoat Dong",
            Status = SupplierStatus.Active,
            DeliveryDaysJson = "[\"mon\", \"thu3\", \"cn\"]", // mon(1), thu3(3), cn(0)
            DeliveryWeekdaysMask = (1 << 1) | (1 << 3),
            BlackoutDates = new List<SupplierBlackoutDate>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.CurrentTenantId,
                    SupplierId = supplierActiveId,
                    StartDate = DateTime.Today.AddDays(1),
                    EndDate = DateTime.Today.AddDays(2),
                    Reason = "Nghỉ lễ"
                }
            }
        };
        var supInactive = new Supplier
        {
            Id = supplierInactiveId,
            TenantId = tenant.CurrentTenantId,
            Code = "SUP-INACT",
            Name = "NCC Tam Ngung",
            Status = SupplierStatus.Inactive,
            DeliveryWeekdaysMask = (1 << 2) // tue(2)
        };
        dbContext.Suppliers.AddRange(supActive, supInactive);

        var asgActive = new SupplierBranchAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchId,
            SupplierId = supplierActiveId,
            Status = SupplierStatus.Active
        };
        var asgInactive = new SupplierBranchAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchId,
            SupplierId = supplierInactiveId,
            Status = SupplierStatus.Inactive,
            InactiveReason = "Ngừng phân phối chi nhánh này"
        };
        dbContext.SupplierBranchAssignments.AddRange(asgActive, asgInactive);

        var prodA = new Product
        {
            Id = prodAId,
            TenantId = tenant.CurrentTenantId,
            ProductCode = "SP-A",
            Name = "Sản phẩm A",
            CategoryId = category.Id,
            Category = category,
            Status = ProductStatus.Active,
            SellingPrice = 50000m,
            CostPrice = 35000m,
            MinStock = 10m
        };
        var prodB = new Product
        {
            Id = prodBId,
            TenantId = tenant.CurrentTenantId,
            ProductCode = "SP-B",
            Name = "Sản phẩm B",
            CategoryId = category.Id,
            Category = category,
            Status = ProductStatus.Active,
            SellingPrice = 80000m,
            CostPrice = 60000m,
            MinStock = 10m
        };
        dbContext.Products.AddRange(prodA, prodB);

        // Branch has products
        var bpActive = new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchId,
            ProductId = prodAId,
            Status = "Active"
        };
        var bpInactive = new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchId,
            ProductId = prodBId,
            Status = "Inactive"
        };
        dbContext.BranchProducts.AddRange(bpActive, bpInactive);

        var spActive = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            SupplierId = supplierActiveId,
            Supplier = supActive,
            ProductId = prodAId,
            Product = prodA,
            Status = SupplierProductStatus.Active,
            AgreedWholesalePrice = 30000m,
            MinimumOrderQuantity = 10,
            DeliveryUoM = "Hộp"
        };
        var spInactive = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            SupplierId = supplierInactiveId,
            Supplier = supInactive,
            ProductId = prodBId,
            Product = prodB,
            Status = SupplierProductStatus.Inactive,
            MinimumOrderQuantity = 0
        };
        dbContext.SupplierProducts.AddRange(spActive, spInactive);

        await dbContext.SaveChangesAsync();

        // 3. Query all suppliers on matrix
        var matrixRows = await repo.GetMatrixProductsAsync(branchId, null, CancellationToken.None);
        Assert.NotEmpty(matrixRows);
        Assert.Contains(matrixRows, r => r.Id == prodAId && r.SupplierId == supplierActiveId);
        Assert.Contains(matrixRows, r => r.Id == prodBId && r.SupplierId == supplierInactiveId);

        // Verify inactive reasons set on inactive rows
        var inactiveRow = matrixRows.First(r => r.Id == prodBId && r.SupplierId == supplierInactiveId);
        Assert.False(inactiveRow.IsSupplierActive);
        Assert.False(inactiveRow.IsSupplierProductActive);
        Assert.False(inactiveRow.IsBranchProductActive);
        Assert.Equal("Chi nhánh không kinh doanh sản phẩm này", inactiveRow.BranchProductInactiveReason);

        // 4. Query supplier delivery configs
        var configs = await repo.GetMatrixSupplierConfigsAsync(branchId, null, CancellationToken.None);
        Assert.Equal(2, configs.Count);

        var activeCfg = configs.First(c => c.SupplierId == supplierActiveId);
        Assert.True(activeCfg.IsActive);
        Assert.True(activeCfg.IsAssignedToBranch);
        Assert.NotEmpty(activeCfg.ActiveDeliveryDays);
        Assert.NotEmpty(activeCfg.BlackoutDates);

        var inactiveCfg = configs.First(c => c.SupplierId == supplierInactiveId);
        Assert.False(inactiveCfg.IsActive);
        Assert.False(inactiveCfg.IsAssignedToBranch);
    }
}
