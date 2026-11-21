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

    [Fact]
    public async Task GetPagedAsync_CallsRepository_ReturnsPagedResult()
    {
        var (svc, repo, _, _, _) = BuildServiceWithTwoLots();
        var query = new StocktakeFilterQuery { PageIndex = 1, PageSize = 10, Status = "ALL" };
        var expected = new PagedResult<StocktakeListItemDto>
        {
            Items = new List<StocktakeListItemDto>(),
            Page = 1,
            PageSize = 10,
            TotalItems = 0
        };
        repo.Setup(r => r.GetPagedAsync("tenant-test", query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await svc.GetPagedAsync(query, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task GetByIdAsync_ExistsAndNotFound()
    {
        var (svc, repo, _, _, stocktakeId) = BuildServiceWithTwoLots();

        // Found
        var result = await svc.GetByIdAsync(stocktakeId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(stocktakeId, result.Id);

        // Not found
        var nonExistentId = Guid.NewGuid();
        repo.Setup(r => r.GetDetailDtoByIdAsync("tenant-test", nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StocktakeDetailDto?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.GetByIdAsync(nonExistentId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_EmployeeAssignedDifferentBranch_ThrowsUnauthorized()
    {
        var tenant = new FakeTenantContext
        {
            CurrentRole = "STAFF",
            CurrentBranchId = Guid.NewGuid() // Different branch
        };
        var repo = new Mock<IStocktakeRepository>();
        var svc = new StocktakeService(
            repo.Object,
            new Mock<ISupplierReturnService>().Object,
            new Mock<ISupplierReturnRepository>().Object,
            new Mock<ISupplierRepository>().Object,
            tenant,
            new Mock<IUnitOfWork>().Object);

        var cmd = new CreateStocktakeCommand(
            BranchId: Guid.NewGuid(), // Mismatched
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: "Test",
            BranchStockIds: null,
            CategoryId: null,
            Lines: null
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAsync(cmd, CancellationToken.None));
        Assert.Contains("Nhân viên chỉ được lập phiếu kiểm kê", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_NotFoundOrNotDraft_ThrowsAppropriateException()
    {
        var (svc, repo, _, _, stocktakeId) = BuildServiceWithTwoLots();

        // Not found
        var nonExistentId = Guid.NewGuid();
        repo.Setup(r => r.GetByIdAsync("tenant-test", nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.UpdateAsync(nonExistentId, new UpdateStocktakeCommand(null, null, new List<StocktakeLineInputDto>())));

        // Not draft (PENDING_APPROVAL)
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.UpdateAsync(stocktakeId, new UpdateStocktakeCommand(null, null, new List<StocktakeLineInputDto>())));
        Assert.Contains("Chỉ được sửa phiếu kiểm kê ở trạng thái DRAFT", ex.Message);
    }

    [Fact]
    public async Task SubmitAsync_NotDraft_ThrowsBusinessException()
    {
        var (svc, repo, _, _, stocktakeId) = BuildServiceWithTwoLots();

        // Stocktake is PENDING_APPROVAL -> not DRAFT
        var exStatus = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.SubmitAsync(stocktakeId, CancellationToken.None));
        Assert.Contains("Chỉ có thể gửi duyệt", exStatus.Message);
    }

    [Fact]
    public async Task ReviewAsync_RejectAndRecount_UpdatesStatusAndAudits()
    {
        var (svc, repo, _, _, stocktakeId) = BuildServiceWithTwoLots();
        var entity = await repo.Object.GetByIdAsync("tenant-test", stocktakeId, CancellationToken.None);

        // Invalid decision throws
        var exInvalid = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.ReviewAsync(stocktakeId, new ReviewStocktakeCommand("UNKNOWN_DECISION", "Test")));
        Assert.Contains("không hợp lệ", exInvalid.Message);

        // Valid Reject
        await svc.ReviewAsync(stocktakeId, new ReviewStocktakeCommand("REJECT", "Từ chối do số liệu sai lệch"));
        Assert.Equal("REJECTED", entity!.Status);
        Assert.Equal("Từ chối do số liệu sai lệch", entity.ReviewNote);

        // Reset to PENDING_APPROVAL for Recount
        entity.Status = "PENDING_APPROVAL";
        await svc.ReviewAsync(stocktakeId, new ReviewStocktakeCommand("NEEDS_RECOUNT", "Yêu cầu đếm lại kệ 3"));
        Assert.Equal("NEEDS_RECOUNT", entity.Status);
        Assert.Equal("Yêu cầu đếm lại kệ 3", entity.ReviewNote);
    }

    [Fact]
    public async Task CreateReturnFromStocktake_NotApproved_ThrowsBusinessException()
    {
        var (svc, _, _, _, stocktakeId) = BuildServiceWithTwoLots();

        var cmd = new CreateReturnFromStocktakeCommand(
            GoodsReceiptId: Guid.NewGuid(),
            ResolutionType: "EXCHANGE",
            ReasonNotes: "Hàng hỏng",
            AttachmentUrl: null,
            Lines: new List<CreateReturnFromStocktakeLineItemDto>()
        );

        // Status is PENDING_APPROVAL, not APPROVED
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, cmd, CancellationToken.None));
        Assert.Contains("chưa được phê duyệt", ex.Message);
    }

    [Fact]
    public async Task CreateReturnFromStocktake_ValidationRules_EnforcedCorrectly()
    {
        var tenant = new FakeTenantContext();
        var repo = new Mock<IStocktakeRepository>();
        var returnSvc = new Mock<ISupplierReturnService>();
        var returnRepo = new Mock<ISupplierReturnRepository>();
        var supplierRepo = new Mock<ISupplierRepository>();
        var uow = new Mock<IUnitOfWork>();

        var stocktakeId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = stocktakeId,
            TenantId = "tenant-test",
            Code = "KK-APPROVED-001",
            Status = "APPROVED",
            Lines = new List<StocktakeLine>
            {
                new()
                {
                    Id = lineId,
                    TenantId = "tenant-test",
                    StocktakeId = stocktakeId,
                    DamagedQty = 5m,
                    DamagedAction = "RETURN_SUPPLIER",
                    ReturnedQty = 2m
                }
            }
        };

        repo.Setup(r => r.GetByIdAsync("tenant-test", stocktakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        var svc = new StocktakeService(repo.Object, returnSvc.Object, returnRepo.Object, supplierRepo.Object, tenant, uow.Object);

        // Case 1: Employee role throws
        tenant.SetRole("EMPLOYEE");
        var exRole = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, new CreateReturnFromStocktakeCommand(Guid.NewGuid(), "EXCHANGE", "Notes", null, new List<CreateReturnFromStocktakeLineItemDto>())));
        Assert.Contains("Chỉ Store Owner", exRole.Message);

        // Reset to Owner
        tenant.SetRole("STORE_OWNER");

        // Case 2: Empty lines throws
        var exEmpty = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, new CreateReturnFromStocktakeCommand(Guid.NewGuid(), "EXCHANGE", "Notes", null, new List<CreateReturnFromStocktakeLineItemDto>())));
        Assert.Contains("ít nhất một dòng", exEmpty.Message);

        // Case 3: Line not found throws
        var exLineNotFound = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, new CreateReturnFromStocktakeCommand(Guid.NewGuid(), "EXCHANGE", "Notes", null, new List<CreateReturnFromStocktakeLineItemDto>
            {
                new(Guid.NewGuid(), null, null, 1m, null)
            })));
        Assert.Contains("không thuộc phiếu", exLineNotFound.Message);

        // Case 4: Requested qty <= 0 throws
        var exZero = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, new CreateReturnFromStocktakeCommand(Guid.NewGuid(), "EXCHANGE", "Notes", null, new List<CreateReturnFromStocktakeLineItemDto>
            {
                new(lineId, null, null, 0m, null)
            })));
        Assert.Contains("lớn hơn 0", exZero.Message);

        // Case 5: Requested qty > remaining (5 - 2 = 3 remaining, requested 4)
        var exExceed = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, new CreateReturnFromStocktakeCommand(Guid.NewGuid(), "EXCHANGE", "Notes", null, new List<CreateReturnFromStocktakeLineItemDto>
            {
                new(lineId, null, null, 4m, null)
            })));
        Assert.Contains("vượt quá số lượng", exExceed.Message);

        // Case 6: No GRN resolved throws
        var exNoGrn = await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateReturnFromStocktakeAsync(stocktakeId, new CreateReturnFromStocktakeCommand(Guid.Empty, "EXCHANGE", "Notes", null, new List<CreateReturnFromStocktakeLineItemDto>
            {
                new(lineId, null, null, 2m, null)
            })));
        Assert.Contains("Phiếu nhập kho gốc", exNoGrn.Message);
    }

    [Fact]
    public void StaticHelpers_EvaluateQuantitiesAndDispositions()
    {
        // Eligible return qty: DamagedQty (when DamagedAction is RETURN_SUPPLIER) + ExpiredQty (when ExpiredAction is RETURN_SUPPLIER)
        var line1 = new StocktakeLine
        {
            DamagedQty = 5m,
            DamagedAction = "RETURN_SUPPLIER",
            ExpiredQty = 3m,
            ExpiredAction = "RETURN_SUPPLIER",
            ReturnedQty = 2m
        };
        Assert.Equal(8m, StocktakeService.GetLineEligibleReturnQty(line1)); // 5 + 3 = 8

        // Write-off qty: DamagedQty (when WRITE_OFF) + ExpiredQty (when WRITE_OFF)
        var line2 = new StocktakeLine
        {
            DamagedQty = 5m,
            DamagedAction = "WRITE_OFF",
            ExpiredQty = 3m,
            ExpiredAction = "WRITE_OFF"
        };
        Assert.Equal(8m, StocktakeService.GetLineWriteOffQty(line2));

        // IsReturnDisposition
        Assert.True(StocktakeService.IsReturnDisposition("RETURN_SUPPLIER"));
        Assert.False(StocktakeService.IsReturnDisposition("WRITE_OFF"));
        Assert.False(StocktakeService.IsReturnDisposition(null));
    }
}
