using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Stocktakes;

public class StocktakeServiceFullTests
{
    private readonly Mock<IStocktakeRepository> _mockRepo;
    private readonly Mock<ISupplierReturnService> _mockReturnService;
    private readonly Mock<ISupplierReturnRepository> _mockReturnRepo;
    private readonly Mock<ISupplierRepository> _mockSupplierRepo;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly StocktakeService _service;
    private const string TenantId = "tenant-stocktake-01";

    public StocktakeServiceFullTests()
    {
        _mockRepo = new Mock<IStocktakeRepository>();
        _mockReturnService = new Mock<ISupplierReturnService>();
        _mockReturnRepo = new Mock<ISupplierReturnRepository>();
        _mockSupplierRepo = new Mock<ISupplierRepository>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.StoreOwner);
        _mockUow = new Mock<IUnitOfWork>();

        _service = new StocktakeService(
            _mockRepo.Object,
            _mockReturnService.Object,
            _mockReturnRepo.Object,
            _mockSupplierRepo.Object,
            _mockTenant.Object,
            _mockUow.Object);
    }

    private static StocktakeDetailDto CreateDetailDto(Guid id, Guid branchId, string status = "DRAFT", string code = "ST-001")
    {
        return new StocktakeDetailDto(
            Id: id,
            Code: code,
            BranchId: branchId,
            BranchCode: "BR-01",
            BranchName: "Main Branch",
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Status: status,
            CreatedByName: "Tester",
            StartedAt: DateTime.UtcNow,
            SubmittedAt: null,
            ReviewedByName: null,
            ReviewedAt: null,
            ReviewNote: null,
            Note: null,
            Lines: new List<StocktakeLineDto>()
        );
    }

    [Fact]
    public async Task GetPagedAsync_ShouldCallRepository()
    {
        var query = new StocktakeFilterQuery();
        var paged = new PagedResult<StocktakeListItemDto>
        {
            Items = new List<StocktakeListItemDto>(),
            Page = 1,
            PageSize = 20,
            TotalItems = 0
        };
        _mockRepo.Setup(r => r.GetPagedAsync(TenantId, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _service.GetPagedAsync(query);
        Assert.NotNull(result);
        _mockRepo.Verify(r => r.GetPagedAsync(TenantId, query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenFound_ShouldReturnDetail()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var detail = CreateDetailDto(id, branchId, "DRAFT", "ST-001");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _service.GetByIdAsync(id);
        Assert.NotNull(result);
        Assert.Equal("ST-001", result.Code);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldThrowNotFoundException()
    {
        var id = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StocktakeDetailDto?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(id));
    }

    [Fact]
    public async Task CreateAsync_EmptyBranchId_ShouldThrowBusinessException()
    {
        var command = new CreateStocktakeCommand(
            BranchId: Guid.Empty,
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: null,
            BranchStockIds: null,
            CategoryId: null,
            Lines: null
        );

        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(command));
    }

    [Fact]
    public async Task CreateAsync_NoStocksFound_ShouldThrowBusinessException()
    {
        var branchId = Guid.NewGuid();
        var command = new CreateStocktakeCommand(
            BranchId: branchId,
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: null,
            BranchStockIds: null,
            CategoryId: null,
            Lines: new List<StocktakeLineInputDto>()
        );
        _mockRepo.Setup(r => r.GetBranchStocksForScopeAsync(TenantId, branchId, "ALL", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BranchStock>());

        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(command));
    }

    [Fact]
    public async Task SubmitAsync_WhenNotFound_ShouldThrowNotFoundException()
    {
        var id = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.SubmitAsync(id));
    }

    [Fact]
    public async Task SubmitAsync_InvalidStatus_ShouldThrowBusinessException()
    {
        var id = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = Guid.NewGuid(),
            Status = "APPROVED"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        await Assert.ThrowsAsync<BusinessException>(() => _service.SubmitAsync(id));
    }

    [Fact]
    public async Task SubmitAsync_ValidStatus_ShouldTransitionToPendingApproval()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = branchId,
            Status = "DRAFT"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        _mockRepo.Setup(r => r.UpdateAsync(stocktake, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var detail = CreateDetailDto(id, branchId, "PENDING_APPROVAL", "ST-001");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _service.SubmitAsync(id);
        Assert.Equal("PENDING_APPROVAL", result.Status);
        Assert.Equal("PENDING_APPROVAL", stocktake.Status);
    }

    [Fact]
    public async Task ReviewAsync_EmployeeRole_ShouldThrowBusinessException()
    {
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.Staff);
        var id = Guid.NewGuid();
        var command = new ReviewStocktakeCommand("APPROVE", null);

        await Assert.ThrowsAsync<BusinessException>(() => _service.ReviewAsync(id, command));
    }

    [Fact]
    public async Task ReviewAsync_InvalidStatus_ShouldThrowBusinessException()
    {
        var id = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = Guid.NewGuid(),
            Status = "DRAFT"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        var command = new ReviewStocktakeCommand("APPROVE", null);
        await Assert.ThrowsAsync<BusinessException>(() => _service.ReviewAsync(id, command));
    }

    [Fact]
    public async Task ReviewAsync_RejectDecision_ShouldSetStatusRejected()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = branchId,
            Status = "PENDING_APPROVAL"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        _mockRepo.Setup(r => r.UpdateAsync(stocktake, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var detail = CreateDetailDto(id, branchId, "REJECTED", "ST-001");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var command = new ReviewStocktakeCommand("REJECT", "Rejected by manager");
        var result = await _service.ReviewAsync(id, command);

        Assert.Equal("REJECTED", stocktake.Status);
        Assert.Equal("REJECTED", result.Status);
    }

    [Fact]
    public async Task ReviewAsync_NeedsRecountDecision_ShouldSetStatusRecount()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = branchId,
            Status = "PENDING_APPROVAL"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        _mockRepo.Setup(r => r.UpdateAsync(stocktake, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var detail = CreateDetailDto(id, branchId, "NEEDS_RECOUNT", "ST-001");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var command = new ReviewStocktakeCommand("NEEDS_RECOUNT", "Please recount");
        var result = await _service.ReviewAsync(id, command);

        Assert.Equal("NEEDS_RECOUNT", stocktake.Status);
        Assert.Equal("NEEDS_RECOUNT", result.Status);
    }

    [Fact]
    public async Task ReviewAsync_InvalidDecision_ShouldThrowBusinessException()
    {
        var id = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = Guid.NewGuid(),
            Status = "PENDING_APPROVAL"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        var command = new ReviewStocktakeCommand("INVALID_DECISION", null);
        await Assert.ThrowsAsync<BusinessException>(() => _service.ReviewAsync(id, command));
    }

    [Fact]
    public async Task CreateReturnFromStocktakeAsync_NotApproved_ShouldThrowBusinessException()
    {
        var id = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = Guid.NewGuid(),
            Status = "DRAFT"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        var command = new CreateReturnFromStocktakeCommand(
            GoodsReceiptId: Guid.NewGuid(),
            ResolutionType: "REFUND",
            ReasonNotes: "Damage return",
            AttachmentUrl: null,
            Lines: new List<CreateReturnFromStocktakeLineItemDto>()
        );
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateReturnFromStocktakeAsync(id, command));
    }

    [Fact]
    public async Task CreateAsync_HappyPath_StoreOwner_ShouldCreateAndReturnDetail()
    {
        var branchId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        var stock = new BranchStock
        {
            Id = stockId,
            TenantId = TenantId,
            BranchId = branchId,
            ProductId = productId,
            SupplierId = supplierId,
            QuantityOnHand = 100,
            BatchCode = "LOT-01",
            LandedCostPerUnit = 10000
        };

        _mockRepo.Setup(r => r.GetBranchStocksForScopeAsync(TenantId, branchId, "ALL", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BranchStock> { stock });
        _mockRepo.Setup(r => r.GetGrnTraceByStockIdsAsync(TenantId, It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, (Guid GrnId, Guid GrnLineId, string GrnCode, Guid SupplierId)>
            {
                [stockId] = (Guid.NewGuid(), Guid.NewGuid(), "GRN-01", supplierId)
            });
        _mockRepo.Setup(r => r.GenerateCodeAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("STK-20261116-001");
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake s, CancellationToken ct) => s);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var detail = CreateDetailDto(Guid.NewGuid(), branchId, "PENDING_APPROVAL", "STK-20261116-001");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var command = new CreateStocktakeCommand(
            BranchId: branchId,
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: "Periodic inventory check",
            BranchStockIds: null,
            CategoryId: null,
            Lines: new List<StocktakeLineInputDto>
            {
                new(
                    Id: null,
                    ProductId: productId,
                    BranchStockId: stockId,
                    GoodQty: 90,
                    DefectiveQty: 5,
                    DamagedQty: 2,
                    NearExpiryQty: 2,
                    ExpiredQty: 1,
                    DefectiveReasonCode: "DEFECTIVE_SUPPLIER",
                    NearExpiryAction: "NEAR_EXPIRY_MARKDOWN",
                    EvidenceUrl: "https://img.com/evidence.jpg",
                    Note: "Damaged packaging"
                )
            },
            AutoSubmit: true,
            CreatedByName: "Store Owner"
        );

        var result = await _service.CreateAsync(command);

        Assert.NotNull(result);
        Assert.Equal("STK-20261116-001", result.Code);
    }

    [Fact]
    public async Task CreateAsync_NegativeQty_ShouldThrowBusinessException()
    {
        var branchId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var stock = new BranchStock
        {
            Id = stockId,
            TenantId = TenantId,
            BranchId = branchId,
            ProductId = productId,
            QuantityOnHand = 50
        };

        _mockRepo.Setup(r => r.GetBranchStocksForScopeAsync(TenantId, branchId, "ALL", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BranchStock> { stock });

        var command = new CreateStocktakeCommand(
            BranchId: branchId,
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: null,
            BranchStockIds: null,
            CategoryId: null,
            Lines: new List<StocktakeLineInputDto>
            {
                new(
                    Id: null,
                    ProductId: productId,
                    BranchStockId: stockId,
                    GoodQty: -10, // Invalid negative
                    DefectiveQty: 0,
                    DamagedQty: 0,
                    NearExpiryQty: 0,
                    ExpiredQty: 0,
                    DefectiveReasonCode: null,
                    NearExpiryAction: null,
                    EvidenceUrl: null,
                    Note: null
                )
            }
        );

        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(command));
    }

    [Fact]
    public async Task UpdateAsync_DraftAndRecount_ShouldUpdateLinesAndAutoSubmit()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = branchId,
            Status = "NEEDS_RECOUNT",
            Lines = new List<StocktakeLine>
            {
                new()
                {
                    Id = lineId,
                    BranchStockId = stockId,
                    SystemQty = 50,
                    GoodQty = 40,
                    ActualQty = 40,
                    VarianceQty = -10
                }
            }
        };

        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        _mockRepo.Setup(r => r.GetBranchStockByIdAsync(TenantId, stockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchStock { Id = stockId, QuantityOnHand = 55 });
        _mockRepo.Setup(r => r.UpdateAsync(stocktake, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var detail = CreateDetailDto(id, branchId, "PENDING_APPROVAL", "ST-002");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var updateCmd = new UpdateStocktakeCommand(
            ReasonType: "RECOUNT",
            Note: "Recounted by manager",
            Lines: new List<StocktakeLineInputDto>
            {
                new(
                    Id: lineId,
                    ProductId: Guid.NewGuid(),
                    BranchStockId: stockId,
                    GoodQty: 50,
                    DefectiveQty: 2,
                    DamagedQty: 1,
                    NearExpiryQty: 1,
                    ExpiredQty: 1,
                    DefectiveReasonCode: "DEFECTIVE_SUPPLIER",
                    NearExpiryAction: "NEAR_EXPIRY_MARKDOWN",
                    EvidenceUrl: "url",
                    Note: "Updated note"
                )
            },
            AutoSubmit: true
        );

        var result = await _service.UpdateAsync(id, updateCmd);

        Assert.Equal("PENDING_APPROVAL", stocktake.Status);
        Assert.Equal(55, stocktake.Lines.First().SystemQty); // Updated from current stock in recount
        Assert.Equal(55, stocktake.Lines.First().ActualQty);
        Assert.Equal(0, stocktake.Lines.First().VarianceQty);
    }

    [Fact]
    public async Task ReviewAsync_Approve_WithSurplus_RequiresConfirmation()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = branchId,
            Status = "PENDING_APPROVAL",
            Lines = new List<StocktakeLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    BranchStockId = Guid.NewGuid(),
                    SystemQty = 50,
                    GoodQty = 60,
                    ActualQty = 60,
                    VarianceQty = 10 // Surplus
                }
            }
        };

        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        // When ConfirmSurplusFound is false, must throw BusinessException
        var cmdWithoutConfirm = new ReviewStocktakeCommand("APPROVE", "Approving", ConfirmSurplusFound: false);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ReviewAsync(id, cmdWithoutConfirm));
        Assert.Equal("SURPLUS_CONFIRM_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task ReviewAsync_Approve_HappyPath_UpdatesStockBalances()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        var stock = new BranchStock
        {
            Id = stockId,
            TenantId = TenantId,
            BranchId = branchId,
            QuantityOnHand = 100,
            QuantityQuarantined = 0
        };

        var line = new StocktakeLine
        {
            Id = Guid.NewGuid(),
            BranchStockId = stockId,
            SupplierId = supplierId,
            SystemQty = 100,
            GoodQty = 80,
            DefectiveQty = 10,
            DamagedQty = 5,
            DamagedAction = "RETURN_SUPPLIER",
            ExpiredQty = 5,
            ExpiredAction = "WRITE_OFF",
            ActualQty = 100,
            VarianceQty = 0,
            EvidenceUrl = "https://example.com/damaged_evidence.jpg"
        };

        var stocktake = new Stocktake
        {
            Id = id,
            TenantId = TenantId,
            BranchId = branchId,
            Status = "PENDING_APPROVAL",
            Lines = new List<StocktakeLine> { line }
        };

        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        _mockRepo.Setup(r => r.GetBranchStockByIdAsync(TenantId, stockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _mockSupplierRepo.Setup(s => s.GetActiveReturnPolicyAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MT_GRMS.Domain.Entities.Supplier.SupplierReturnPolicy?)null);
        _mockRepo.Setup(r => r.UpdateAsync(stocktake, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var detail = CreateDetailDto(id, branchId, "APPROVED", "ST-003");
        _mockRepo.Setup(r => r.GetDetailDtoByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var approveCmd = new ReviewStocktakeCommand("APPROVE", "Looks good", ConfirmSurplusFound: true);
        var result = await _service.ReviewAsync(id, approveCmd);

        Assert.Equal("APPROVED", stocktake.Status);
        // ExpiredAction = WRITE_OFF (5) ->OnHand reduced by 5
        // DefectiveQty (10) + DamagedAction RETURN_SUPPLIER (5) -> Quarantined increased by 15
        Assert.Equal(95, stock.QuantityOnHand);
        Assert.Equal(15, stock.QuantityQuarantined);
    }

    [Fact]
    public async Task CreateReturnFromStocktakeAsync_HappyPath_CreatesSupplierReturn()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var grnId = Guid.NewGuid();
        var grnLineId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        var line = new StocktakeLine
        {
            Id = lineId,
            ProductId = Guid.NewGuid(),
            BranchStockId = stockId,
            SupplierId = supplierId,
            OriginalGrnId = grnId,
            OriginalGrnLineId = grnLineId,
            DefectiveQty = 10,
            ReturnedQty = 0,
            UnitCost = 15000,
            BatchCode = "LOT-RET-01"
        };

        var stocktake = new Stocktake
        {
            Id = id,
            Code = "STK-RET-01",
            TenantId = TenantId,
            BranchId = branchId,
            Status = "APPROVED",
            Lines = new List<StocktakeLine> { line }
        };

        _mockRepo.Setup(r => r.GetByIdAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        _mockRepo.Setup(r => r.UpdateAsync(stocktake, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var returnId = Guid.NewGuid();
        var retDto = new SupplierReturnDetailDto(
            Id: returnId,
            Code: "RET-001",
            GoodsReceiptId: grnId,
            GoodsReceiptCode: "GRN-01",
            SupplierId: supplierId,
            SupplierCode: "SUP-01",
            SupplierName: "Supplier One",
            BranchId: branchId,
            BranchCode: "BR-01",
            BranchName: "Main Branch",
            Status: "DRAFT",
            Reason: "Hàng lỗi",
            ReasonNotes: "Notes",
            SupplierResponseNote: null,
            CancellationReason: null,
            AttachmentUrl: null,
            TotalRequestedQty: 10,
            TotalApprovedQty: 0,
            TotalHandedOverQty: 0,
            TotalEstimatedAmount: 150000,
            CreatedAt: DateTime.UtcNow,
            SubmittedAt: null,
            RespondedAt: null,
            HandedOverAt: null,
            HandedOverBy: null,
            Lines: new List<SupplierReturnLineDto>()
        );

        _mockReturnService.Setup(s => s.CreateAsync(It.IsAny<CreateSupplierReturnCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retDto);
        _mockReturnService.Setup(s => s.GetByIdAsync(returnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retDto);
        _mockReturnRepo.Setup(r => r.GetByIdAsync(TenantId, returnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MT_GRMS.Domain.Entities.Supplier.SupplierReturn?)null);
        _mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var command = new CreateReturnFromStocktakeCommand(
            GoodsReceiptId: grnId,
            ResolutionType: "REFUND",
            ReasonNotes: "Return damaged items",
            AttachmentUrl: null,
            Lines: new List<CreateReturnFromStocktakeLineItemDto>
            {
                new(
                    StocktakeLineId: lineId,
                    RequestedQty: 10,
                    GoodsReceiptId: grnId,
                    GoodsReceiptLineId: grnLineId,
                    Notes: "Full return"
                )
            }
        );

        var returns = await _service.CreateReturnFromStocktakeAsync(id, command);

        Assert.Single(returns);
        Assert.Equal("RET-001", returns[0].Code);
        Assert.Equal(10, line.ReturnedQty);
    }
}
