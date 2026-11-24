using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BranchScopeRbacTests
{
    private static readonly Guid BranchA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BranchB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class StubTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    [Fact]
    public void BranchScopeGuard_StoreOwner_CanViewAllOrSpecificBranch()
    {
        var ctx = new StubTenantContext { CurrentRole = "STORE_OWNER", CurrentBranchId = null };
        Assert.Null(BranchScopeGuard.ResolveEffectiveBranchId(ctx, null));
        Assert.Equal(BranchA, BranchScopeGuard.ResolveEffectiveBranchId(ctx, BranchA));
        Assert.Equal(BranchB, BranchScopeGuard.ResolveEffectiveBranchId(ctx, BranchB));
    }

    [Fact]
    public void BranchScopeGuard_Employee_ForcesOwnBranchAndBlocksOtherBranch()
    {
        var ctx = new StubTenantContext { CurrentRole = "EMPLOYEE", CurrentBranchId = BranchA };
        Assert.Equal(BranchA, BranchScopeGuard.ResolveEffectiveBranchId(ctx, null));
        Assert.Equal(BranchA, BranchScopeGuard.ResolveEffectiveBranchId(ctx, BranchA));

        var ex = Assert.Throws<BusinessException>(() =>
            BranchScopeGuard.ResolveEffectiveBranchId(ctx, BranchB));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task BatchService_EmployeeScopedToOwnBranch_BlocksForeignBranch()
    {
        var repo = new Mock<IBatchRepository>();
        BatchFilterQuery? captured = null;
        repo.Setup(r => r.GetPagedBatchesAsync("tenant-01", It.IsAny<BatchFilterQuery>(), It.IsAny<CancellationToken>()))
            .Callback<string, BatchFilterQuery, CancellationToken>((_, q, _) => captured = q)
            .ReturnsAsync(new BatchPagedResultDto(
                Array.Empty<BatchListItemDto>(), 0, 1, 20, new BatchKpiSummaryDto(0, 0, 0, 0, 0, 0m)));

        var uow = new Mock<IUnitOfWork>();
        var ctx = new StubTenantContext { CurrentRole = "STAFF", CurrentBranchId = BranchA };
        var svc = new BatchService(repo.Object, ctx, uow.Object);

        await svc.GetPagedBatchesAsync(new BatchFilterQuery { BranchId = null });
        Assert.NotNull(captured);
        Assert.Equal(BranchA, captured!.BranchId);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.GetPagedBatchesAsync(new BatchFilterQuery { BranchId = BranchB }));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task InventoryService_EmployeeScopedToOwnBranch_BlocksForeignBranch()
    {
        var repo = new Mock<IInventoryRepository>();
        InventoryFilterQuery? captured = null;
        repo.Setup(r => r.GetPagedStockSummaryAsync("tenant-01", It.IsAny<InventoryFilterQuery>(), It.IsAny<CancellationToken>()))
            .Callback<string, InventoryFilterQuery, CancellationToken>((_, q, _) => captured = q)
            .ReturnsAsync(new InventoryPagedResultDto(
                Array.Empty<InventoryStockItemDto>(), 0, 1, 10, new InventoryKpiSummaryDto(0, 0m, 0, 0, 0, 0m)));

        var ctx = new StubTenantContext { CurrentRole = "EMPLOYEE", CurrentBranchId = BranchA };
        var svc = new InventoryService(repo.Object, ctx);

        await svc.GetPagedStockSummaryAsync(new InventoryFilterQuery { BranchId = null });
        Assert.NotNull(captured);
        Assert.Equal(BranchA, captured!.BranchId);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.GetPagedStockSummaryAsync(new InventoryFilterQuery { BranchId = BranchB }));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task PurchaseOrderService_EmployeeScopedToOwnBranch_StoreOwnerSeesAll()
    {
        var repo = new Mock<IPurchaseOrderRepository>();
        Guid? capturedBranch = Guid.Empty;
        repo.Setup(r => r.GetPagedPurchaseOrdersAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, Guid?, string?, DateTime?, DateTime?, int, int, string?, CancellationToken>(
                (b, _, _, _, _, _, _, _, _) => capturedBranch = b)
            .ReturnsAsync((Array.Empty<PurchaseOrder>(), 0));

        var ctx = new StubTenantContext { CurrentRole = "EMPLOYEE", CurrentBranchId = BranchA };
        var svc = new PurchaseOrderService(
            repo.Object,
            new Mock<IUnitOfWork>().Object,
            ctx,
            new Mock<ISupplierDispatchPackageService>().Object);

        await svc.GetPagedPurchaseOrdersAsync(new PurchaseOrderFilterQuery { BranchId = null });
        Assert.Equal(BranchA, capturedBranch);

        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.GetPagedPurchaseOrdersAsync(new PurchaseOrderFilterQuery { BranchId = BranchB }));

        ctx.SetRole("STORE_OWNER");
        ctx.CurrentBranchId = null;
        await svc.GetPagedPurchaseOrdersAsync(new PurchaseOrderFilterQuery { BranchId = null });
        Assert.Null(capturedBranch);
    }

    [Fact]
    public async Task GoodsReceiptAndSupplierReturn_EmployeeScopedToOwnBranch()
    {
        var grnRepo = new Mock<IGoodsReceiptRepository>();
        GoodsReceiptFilterQuery? capturedGrn = null;
        grnRepo.Setup(r => r.GetPagedAsync("tenant-01", It.IsAny<GoodsReceiptFilterQuery>(), It.IsAny<CancellationToken>()))
            .Callback<string, GoodsReceiptFilterQuery, CancellationToken>((_, q, _) => capturedGrn = q)
            .ReturnsAsync(new PagedResult<GoodsReceiptDto>());

        var rtvRepo = new Mock<ISupplierReturnRepository>();
        SupplierReturnFilterQuery? capturedRtv = null;
        rtvRepo.Setup(r => r.GetPagedAsync("tenant-01", It.IsAny<SupplierReturnFilterQuery>(), It.IsAny<CancellationToken>()))
            .Callback<string, SupplierReturnFilterQuery, CancellationToken>((_, q, _) => capturedRtv = q)
            .ReturnsAsync(new PagedResult<SupplierReturnListItemDto>());

        var ctx = new StubTenantContext { CurrentRole = "CASHIER", CurrentBranchId = BranchA };
        var uow = new Mock<IUnitOfWork>().Object;

        var grnSvc = new GoodsReceiptService(grnRepo.Object, uow, ctx);
        await grnSvc.GetGoodsReceiptsAsync(new GoodsReceiptFilterQuery { BranchId = null });
        Assert.Equal(BranchA, capturedGrn!.BranchId);
        await Assert.ThrowsAsync<BusinessException>(() =>
            grnSvc.GetGoodsReceiptsAsync(new GoodsReceiptFilterQuery { BranchId = BranchB }));

        var rtvSvc = new SupplierReturnService(rtvRepo.Object, ctx, uow);
        await rtvSvc.GetPagedAsync(new SupplierReturnFilterQuery { BranchId = null });
        Assert.Equal(BranchA, capturedRtv!.BranchId);
        await Assert.ThrowsAsync<BusinessException>(() =>
            rtvSvc.GetPagedAsync(new SupplierReturnFilterQuery { BranchId = BranchB }));
    }
}
