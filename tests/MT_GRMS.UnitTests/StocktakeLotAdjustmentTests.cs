using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests;

/// <summary>
/// Kiểm kê theo lô: submit không đổi tồn; approve chỉ chỉnh đúng BranchStockId.
/// </summary>
public class StocktakeLotAdjustmentTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
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

    private static StocktakeDetailDto EmptyDetail(
        Guid id,
        Guid branchId,
        string code,
        string status) =>
        new(
            id,
            code,
            branchId,
            "BR",
            "Branch",
            "LOT",
            "PERIODIC",
            status,
            "Actor",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            Array.Empty<StocktakeLineDto>());

    private static (StocktakeService svc, Mock<IStocktakeRepository> repo, BranchStock lotA, BranchStock lotB, Guid stocktakeId)
        BuildServiceWithTwoLots()
    {
        var tenant = new FakeTenantContext();
        var repo = new Mock<IStocktakeRepository>(MockBehavior.Strict);
        var returnSvc = new Mock<ISupplierReturnService>(MockBehavior.Loose);
        var returnRepo = new Mock<ISupplierReturnRepository>(MockBehavior.Loose);
        var supplierRepo = new Mock<ISupplierRepository>(MockBehavior.Loose);
        supplierRepo
            .Setup(x => x.GetActiveReturnPolicyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.Supplier.SupplierReturnPolicy?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var branchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var lotAId = Guid.NewGuid();
        var lotBId = Guid.NewGuid();
        var stocktakeId = Guid.NewGuid();

        var lotA = new BranchStock
        {
            Id = lotAId,
            TenantId = "tenant-test",
            BranchId = branchId,
            ProductId = productId,
            BatchCode = "LOT-A",
            QuantityOnHand = 100m,
            QuantityQuarantined = 0m,
            QuantityReserved = 0m,
        };
        var lotB = new BranchStock
        {
            Id = lotBId,
            TenantId = "tenant-test",
            BranchId = branchId,
            ProductId = productId,
            BatchCode = "LOT-B",
            QuantityOnHand = 50m,
            QuantityQuarantined = 0m,
            QuantityReserved = 0m,
        };

        var stocktake = new Stocktake
        {
            Id = stocktakeId,
            TenantId = "tenant-test",
            Code = "KK-TEST-001",
            BranchId = branchId,
            Status = "PENDING_APPROVAL",
            Lines = new List<StocktakeLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test",
                    StocktakeId = stocktakeId,
                    ProductId = productId,
                    BranchStockId = lotAId,
                    BatchCode = "LOT-A",
                    SystemQty = 100m,
                    GoodQty = 90m,
                    DamagedQty = 5m,
                    ExpiredQty = 0m,
                    ActualQty = 95m,
                    VarianceQty = -5m,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test",
                    StocktakeId = stocktakeId,
                    ProductId = productId,
                    BranchStockId = lotBId,
                    BatchCode = "LOT-B",
                    SystemQty = 50m,
                    GoodQty = 50m,
                    DamagedQty = 0m,
                    ExpiredQty = 0m,
                    ActualQty = 50m,
                    VarianceQty = 0m,
                },
            },
        };

        repo.Setup(r => r.GetByIdAsync("tenant-test", stocktakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        repo.Setup(r => r.GetBranchStockByIdAsync("tenant-test", lotAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lotA);
        repo.Setup(r => r.GetBranchStockByIdAsync("tenant-test", lotBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lotB);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetDetailDtoByIdAsync("tenant-test", stocktakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyDetail(stocktakeId, branchId, "KK-TEST-001", "APPROVED"));

        var svc = new StocktakeService(
            repo.Object,
            returnSvc.Object,
            returnRepo.Object,
            supplierRepo.Object,
            tenant,
            uow.Object);

        return (svc, repo, lotA, lotB, stocktakeId);
    }

    [Fact]
    public async Task Approve_ShouldAdjustOnlyMatchingBranchStock_NotOtherLot()
    {
        var (svc, _, lotA, lotB, stocktakeId) = BuildServiceWithTwoLots();

        await svc.ReviewAsync(
            stocktakeId,
            new ReviewStocktakeCommand("APPROVE", null, null, false),
            CancellationToken.None);

        // LOT-A: write-off damaged 5 + missing variance 5 → 100 - 5 - 5 = 90
        Assert.Equal(90m, lotA.QuantityOnHand);
        // LOT-B không đổi
        Assert.Equal(50m, lotB.QuantityOnHand);
        Assert.Equal(0m, lotA.QuantityQuarantined);
        Assert.Equal(0m, lotB.QuantityQuarantined);
    }

    [Fact]
    public async Task Submit_ShouldNotChangeOnHandOrQuarantine()
    {
        var branchId = Guid.NewGuid();
        var tenant = new FakeTenantContext
        {
            CurrentRole = "STAFF",
            CurrentBranchId = branchId,
        };
        var repo = new Mock<IStocktakeRepository>(MockBehavior.Strict);
        var returnSvc = new Mock<ISupplierReturnService>(MockBehavior.Loose);
        var returnRepo = new Mock<ISupplierReturnRepository>(MockBehavior.Loose);
        var supplierRepo = new Mock<ISupplierRepository>(MockBehavior.Loose);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var stockId = Guid.NewGuid();
        var stocktakeId = Guid.NewGuid();
        var stock = new BranchStock
        {
            Id = stockId,
            TenantId = "tenant-test",
            BranchId = branchId,
            ProductId = Guid.NewGuid(),
            QuantityOnHand = 40m,
            QuantityQuarantined = 0m,
        };

        var entity = new Stocktake
        {
            Id = stocktakeId,
            TenantId = "tenant-test",
            Code = "KK-DRAFT",
            BranchId = branchId,
            Status = "DRAFT",
            Lines =
            [
                new StocktakeLine
                {
                    Id = Guid.NewGuid(),
                    BranchStockId = stockId,
                    ProductId = stock.ProductId,
                    SystemQty = 40m,
                    GoodQty = 30m,
                    DamagedQty = 5m,
                    ExpiredQty = 5m,
                    ActualQty = 40m,
                    VarianceQty = 0m,
                },
            ],
        };

        repo.Setup(r => r.GetByIdAsync("tenant-test", stocktakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetDetailDtoByIdAsync("tenant-test", stocktakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyDetail(stocktakeId, branchId, "KK-DRAFT", "PENDING_APPROVAL"));

        // Submit không được gọi GetBranchStockByIdAsync
        repo.Setup(r => r.GetBranchStockByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Submit không được đụng BranchStock"));

        var svc = new StocktakeService(
            repo.Object,
            returnSvc.Object,
            returnRepo.Object,
            supplierRepo.Object,
            tenant,
            uow.Object);

        var result = await svc.SubmitAsync(stocktakeId, CancellationToken.None);

        Assert.Equal("PENDING_APPROVAL", entity.Status);
        Assert.Equal(40m, stock.QuantityOnHand);
        Assert.Equal(0m, stock.QuantityQuarantined);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Approve_WithSurplus_RequiresConfirmSurplusFound()
    {
        var (svc, repo, lotA, _, stocktakeId) = BuildServiceWithTwoLots();
        var entity = await repo.Object.GetByIdAsync("tenant-test", stocktakeId, CancellationToken.None);
        entity!.Lines.First().GoodQty = 110m;
        entity.Lines.First().DamagedQty = 0m;
        entity.Lines.First().ActualQty = 110m;
        entity.Lines.First().VarianceQty = 10m;

        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.ReviewAsync(
                stocktakeId,
                new ReviewStocktakeCommand("APPROVE", null, null, false),
                CancellationToken.None));

        Assert.Equal(100m, lotA.QuantityOnHand);
    }

    [Fact]
    public async Task Approve_WhenDamagedMarkedReturn_ShouldNotWriteOffDamaged()
    {
        var (svc, repo, lotA, lotB, stocktakeId) = BuildServiceWithTwoLots();
        var entity = await repo.Object.GetByIdAsync("tenant-test", stocktakeId, CancellationToken.None);
        var lineA = entity!.Lines.First();
        // Chỉ hư hỏng 5, khớp tồn (không thiếu) — variance 0
        lineA.GoodQty = 95m;
        lineA.DamagedQty = 5m;
        lineA.ActualQty = 100m;
        lineA.VarianceQty = 0m;
        lineA.SupplierId = Guid.NewGuid();
        lineA.EvidenceUrl = "https://example.com/evidence.jpg";

        await svc.ReviewAsync(
            stocktakeId,
            new ReviewStocktakeCommand(
                "APPROVE",
                null,
                null,
                false,
                new List<ReviewStocktakeLineActionDto>
                {
                    new(lineA.Id, "RETURN_SUPPLIER", null),
                }),
            CancellationToken.None);

        // Damaged RETURN → không trừ OnHand; đưa vào quarantine; LOT-B không đổi
        Assert.Equal(100m, lotA.QuantityOnHand);
        Assert.Equal(5m, lotA.QuantityQuarantined);
        Assert.Equal(50m, lotB.QuantityOnHand);
        Assert.Equal("RETURN_SUPPLIER", lineA.DamagedAction);
        Assert.Equal(5m, StocktakeService.GetLineEligibleReturnQty(lineA));
    }

    [Fact]
    public async Task Approve_WhenReturnWithoutSupplier_ShouldFail()
    {
        var (svc, repo, lotA, _, stocktakeId) = BuildServiceWithTwoLots();
        var entity = await repo.Object.GetByIdAsync("tenant-test", stocktakeId, CancellationToken.None);
        var lineA = entity!.Lines.First();
        lineA.GoodQty = 95m;
        lineA.DamagedQty = 5m;
        lineA.ActualQty = 100m;
        lineA.VarianceQty = 0m;
        lineA.SupplierId = null;
        lineA.EvidenceUrl = "https://example.com/e.jpg";

        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.ReviewAsync(
                stocktakeId,
                new ReviewStocktakeCommand(
                    "APPROVE",
                    null,
                    null,
                    false,
                    new List<ReviewStocktakeLineActionDto>
                    {
                        new(lineA.Id, "RETURN_SUPPLIER", null),
                    }),
                CancellationToken.None));

        Assert.Equal(100m, lotA.QuantityOnHand);
        Assert.Equal(0m, lotA.QuantityQuarantined);
    }
}
