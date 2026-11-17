using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SupplierReturnTests
{
    private readonly FakeSupplierReturnRepository _repo;
    private readonly FakeTenantContext _tenantContext;
    private readonly FakeUnitOfWork _uow;
    private readonly SupplierReturnService _service;

    private readonly Guid _grnId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly Guid _prodId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private readonly Guid _branchId = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private readonly Guid _supplierId = Guid.Parse("44444444-5555-6666-7777-888888888888");

    public SupplierReturnTests()
    {
        _repo = new FakeSupplierReturnRepository();
        _tenantContext = new FakeTenantContext("tenant-test-rtv");
        _uow = new FakeUnitOfWork();
        _service = new SupplierReturnService(_repo, _tenantContext, _uow);

        // Seed eligible GRN
        _repo.EligibleGrns.Add(new EligibleGrnLookupDto(
            _grnId, "GRN-20261030-001", _supplierId, "NCC Hương Quê", _branchId, "Chi nhánh 1", DateTime.UtcNow, 1));

        // Seed returnable line
        _repo.ReturnableLines.Add(new GrnReturnableLineDto(
            Guid.NewGuid(), _prodId, "SKU-01", "Sữa tươi TH", "Hộp", "B01", DateTime.UtcNow.AddDays(10), 100, 0, 0, 100, 30000));

        // Seed branch stock
        _repo.Stocks.Add(new BranchStock
        {
            TenantId = "tenant-test-rtv",
            BranchId = _branchId,
            ProductId = _prodId,
            BatchCode = "B01",
            QuantityOnHand = 100
        });
    }

    [Fact]
    public async Task CreateAsync_ShouldSucceed_WhenRequestedQtyIsWithinAvailable()
    {
        // Arrange
        var command = new CreateSupplierReturnCommand(
            GoodsReceiptId: _grnId,
            Reason: "Hàng sắp hết hạn",
            ReasonNotes: "Date còn dưới 10 ngày",
            AttachmentUrl: "https://example.com/evidence.jpg",
            Lines: new List<CreateSupplierReturnLineDto>
            {
                new CreateSupplierReturnLineDto(_prodId, null, "B01", null, 20, 30000, null)
            },
            AutoSubmit: false
        );

        // Act
        var result = await _service.CreateAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(20, result.TotalRequestedQty);
        Assert.Equal(600000, result.TotalEstimatedAmount);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowException_WhenRequestedQtyExceedsAvailable()
    {
        // Arrange
        var command = new CreateSupplierReturnCommand(
            GoodsReceiptId: _grnId,
            Reason: "Hàng sắp hết hạn",
            ReasonNotes: null,
            AttachmentUrl: null,
            Lines: new List<CreateSupplierReturnLineDto>
            {
                new CreateSupplierReturnLineDto(_prodId, null, "B01", null, 150, 30000, null) // Available is 100
            }
        );

        // Act & Assert
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(command));
    }

    [Fact]
    public async Task SubmitAsync_ShouldAutoConfirmByPolicy_WithoutWaitingSupplierApproval()
    {
        // Arrange
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-001",
            Status = "DRAFT",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            ResolutionType = "REDUCE_PAYABLE",
            TotalRequestedQty = 10,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine
                {
                    Id = Guid.NewGuid(),
                    ProductId = _prodId,
                    RequestedQty = 10,
                    UnitCost = 30000
                }
            }
        };
        _repo.Returns.Add(ret);

        // Act
        var result = await _service.SubmitAsync(ret.Id, "Đã gửi thông báo cho NCC");

        // Assert — không còn chờ NCC duyệt
        Assert.Equal("APPROVED", result.Status);
        Assert.Equal(10, result.TotalApprovedQty);
    }

    [Fact]
    public async Task RecordSupplierResponse_ShouldUpdateApprovedQty_AndStatus()
    {
        // Arrange
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-002",
            Status = "PENDING_SUPPLIER",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            TotalRequestedQty = 20,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = Guid.NewGuid(), ProductId = _prodId, RequestedQty = 20, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(ret);

        var command = new RecordSupplierResponseCommand(
            ResultType: "APPROVED_PARTIAL",
            SupplierResponseNote: "NCC chỉ nhận 15 hộp do 5 hộp đã móp",
            LineResponses: new List<SupplierResponseLineDto>
            {
                new SupplierResponseLineDto(ret.Lines.First().Id, 15, 5, "Vỏ hộp móp")
            }
        );

        // Act
        var result = await _service.RecordSupplierResponseAsync(ret.Id, command);

        // Assert
        Assert.Equal("APPROVED", result.Status);
        Assert.Equal(15, result.TotalApprovedQty);
    }

    [Fact]
    public async Task CompleteHandoverAsync_ShouldReduceBranchStock_AndSetCompleted()
    {
        // Arrange
        var lineId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-003",
            Status = "APPROVED",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            TotalApprovedQty = 15,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = lineId, ProductId = _prodId, BatchCode = "B01", ApprovedQty = 15, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(ret);

        var command = new CompleteHandoverCommand(
            HandedOverBy: "Thủ kho Tuấn",
            Notes: "Bàn giao cho tài xế xe tải 29H-12345",
            Lines: new List<HandoverLineDto>
            {
                new HandoverLineDto(lineId, 15)
            }
        );

        // Act
        var result = await _service.CompleteHandoverAsync(ret.Id, command);

        // Assert
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(15, result.TotalHandedOverQty);
        var stock = _repo.Stocks.First(s => s.ProductId == _prodId);
        Assert.Equal(85, stock.QuantityOnHand); // 100 - 15 = 85
    }

    [Fact]
    public async Task CancelAsync_ShouldThrowException_WhenAlreadyCompleted()
    {
        // Arrange
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-004",
            Status = "COMPLETED",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(ret);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelAsync(ret.Id, new CancelSupplierReturnCommand("Muốn hủy")));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingReturn_ReturnsDetailDto()
    {
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-GET-01",
            Status = "DRAFT",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            ResolutionType = "REDUCE_PAYABLE",
            TotalRequestedQty = 10,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = Guid.NewGuid(), ProductId = _prodId, RequestedQty = 10, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(ret);

        var result = await _service.GetByIdAsync(ret.Id);
        Assert.NotNull(result);
        Assert.Equal(ret.Id, result.Id);
        Assert.Equal("RTV-GET-01", result.Code);
        Assert.Equal("DRAFT", result.Status);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsPagedItems()
    {
        var result = await _service.GetPagedAsync(new SupplierReturnFilterQuery { PageIndex = 1, PageSize = 10 });
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Lookups_GetEligibleGrnsAndReturnableLines_ReturnExpected()
    {
        var grns = await _service.GetEligibleGrnsAsync(_branchId, _supplierId);
        Assert.NotEmpty(grns);

        var lines = await _service.GetGrnReturnableLinesAsync(_grnId);
        Assert.NotEmpty(lines);
    }

    [Fact]
    public async Task CreateAsync_ValidationFailures_ThrowBusinessException()
    {
        // Missing / non-existent GRN
        var missingGrnCmd = new CreateSupplierReturnCommand(
            GoodsReceiptId: Guid.NewGuid(),
            Reason: "Hàng hỏng",
            ReasonNotes: null,
            AttachmentUrl: null,
            Lines: new List<CreateSupplierReturnLineDto> { new CreateSupplierReturnLineDto(_prodId, null, "B01", null, 10, 30000, null) });
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(missingGrnCmd));

        // Empty lines
        var emptyLinesCmd = new CreateSupplierReturnCommand(
            GoodsReceiptId: _grnId,
            Reason: "Hàng hỏng",
            ReasonNotes: null,
            AttachmentUrl: null,
            Lines: new List<CreateSupplierReturnLineDto>());
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(emptyLinesCmd));

        // Invalid requested qty <= 0
        var zeroQtyCmd = new CreateSupplierReturnCommand(
            GoodsReceiptId: _grnId,
            Reason: "Hàng hỏng",
            ReasonNotes: null,
            AttachmentUrl: null,
            Lines: new List<CreateSupplierReturnLineDto> { new CreateSupplierReturnLineDto(_prodId, null, "B01", null, 0, 30000, null) });
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(zeroQtyCmd));

        // Product not in GRN
        var wrongProdCmd = new CreateSupplierReturnCommand(
            GoodsReceiptId: _grnId,
            Reason: "Hàng hỏng",
            ReasonNotes: null,
            AttachmentUrl: null,
            Lines: new List<CreateSupplierReturnLineDto> { new CreateSupplierReturnLineDto(Guid.NewGuid(), null, "B01", null, 5, 30000, null) });
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(wrongProdCmd));
    }

    [Fact]
    public async Task SubmitAsync_NotFoundOrNotDraft_ThrowsException()
    {
        // Not found
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SubmitAsync(Guid.NewGuid()));

        // Not draft
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-SUB-01",
            Status = "COMPLETED",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(ret);
        await Assert.ThrowsAsync<BusinessException>(() => _service.SubmitAsync(ret.Id));
    }

    [Fact]
    public async Task RecordSupplierResponse_NotFoundOrNotPending_ThrowsException()
    {
        var cmd = new RecordSupplierResponseCommand("APPROVED_FULL", "OK", null);

        // Not found
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RecordSupplierResponseAsync(Guid.NewGuid(), cmd));

        // Not pending supplier (DRAFT)
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-REC-01",
            Status = "DRAFT",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(ret);
        await Assert.ThrowsAsync<BusinessException>(() => _service.RecordSupplierResponseAsync(ret.Id, cmd));
    }

    [Fact]
    public async Task RecordSupplierResponse_Rejected_ReleasesStockReservation()
    {
        var lineId = Guid.NewGuid();
        var stock = _repo.Stocks.First(s => s.ProductId == _prodId);
        stock.QuantityReserved = 20;

        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-REC-02",
            Status = "PENDING_SUPPLIER",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            TotalRequestedQty = 20,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = lineId, ProductId = _prodId, BatchCode = "B01", RequestedQty = 20, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(ret);

        var cmd = new RecordSupplierResponseCommand("REJECTED", "NCC từ chối", null);
        var result = await _service.RecordSupplierResponseAsync(ret.Id, cmd);

        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(0, stock.QuantityReserved);
    }

    [Fact]
    public async Task CompleteHandoverAsync_ExceptionsAndExchangePreservation()
    {
        // 1. Not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CompleteHandoverAsync(Guid.NewGuid(), new CompleteHandoverCommand(null, null, null)));

        // 2. Status invalid (DRAFT)
        var draftRet = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-HO-01",
            Status = "DRAFT",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(draftRet);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteHandoverAsync(draftRet.Id, new CompleteHandoverCommand(null, null, null)));

        // 3. Exceeds approved qty
        var lineId = Guid.NewGuid();
        var approvedRet = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-HO-02",
            Status = "APPROVED",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            TotalApprovedQty = 5,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = lineId, ProductId = _prodId, BatchCode = "B01", ApprovedQty = 5, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(approvedRet);

        var overHandover = new CompleteHandoverCommand(
            "Tuấn",
            "Quá số lượng",
            new List<HandoverLineDto> { new HandoverLineDto(lineId, 10) }
        );
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteHandoverAsync(approvedRet.Id, overHandover));

        // 4. EXCHANGE resolution: does NOT reduce QuantityOnHand and keeps APPROVED status
        var stock = _repo.Stocks.First(s => s.ProductId == _prodId);
        var initialOnHand = stock.QuantityOnHand;

        var exchangeRet = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-EX-01",
            Status = "APPROVED",
            ResolutionType = "EXCHANGE",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            TotalApprovedQty = 5,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = Guid.NewGuid(), ProductId = _prodId, BatchCode = "B01", ApprovedQty = 5, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(exchangeRet);

        var validHandover = new CompleteHandoverCommand("Tuấn", "Bàn giao đổi", null);
        var exResult = await _service.CompleteHandoverAsync(exchangeRet.Id, validHandover);

        Assert.Equal("APPROVED", exResult.Status);
        Assert.Equal(initialOnHand, stock.QuantityOnHand); // Stock not deducted on handover for EXCHANGE
    }

    [Fact]
    public async Task CancelAsync_ValidationsAndStockUnreserve()
    {
        // 1. Not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CancelAsync(Guid.NewGuid(), new CancelSupplierReturnCommand("Hủy")));

        // 2. Empty reason
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-CAN-01",
            Status = "DRAFT",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(ret);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelAsync(ret.Id, new CancelSupplierReturnCommand("   ")));

        // 3. Approved status: unreserves stock
        var stock = _repo.Stocks.First(s => s.ProductId == _prodId);
        stock.QuantityReserved = 10;

        var approvedRet = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-CAN-02",
            Status = "APPROVED",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = Guid.NewGuid(), ProductId = _prodId, BatchCode = "B01", ApprovedQty = 10, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(approvedRet);

        var cancelResult = await _service.CancelAsync(approvedRet.Id, new CancelSupplierReturnCommand("Lý do hợp lệ"));
        Assert.Equal("CANCELLED", cancelResult.Status);
        Assert.Equal(0, stock.QuantityReserved);
    }

    [Fact]
    public async Task CreateExchangePurchaseOrdersAsync_ValidationsAndCreation()
    {
        var mockPoRepo = new Mock<IPurchaseOrderRepository>();
        var mockSupplierRepo = new Mock<ISupplierRepository>();
        var serviceWithRepos = new SupplierReturnService(_repo, mockPoRepo.Object, mockSupplierRepo.Object, _tenantContext, _uow);

        // 1. Not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            serviceWithRepos.CreateExchangePurchaseOrdersAsync(Guid.NewGuid()));

        // 2. Not exchange resolution
        var nonExchange = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-NON-EX",
            Status = "APPROVED",
            ResolutionType = "REDUCE_PAYABLE",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(nonExchange);
        var exNotExchange = await Assert.ThrowsAsync<BusinessException>(() =>
            serviceWithRepos.CreateExchangePurchaseOrdersAsync(nonExchange.Id));
        Assert.Equal("NOT_EXCHANGE", exNotExchange.ErrorCode);

        // 3. Invalid status (DRAFT)
        var draftExchange = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-DRAFT-EX",
            Status = "DRAFT",
            ResolutionType = "EXCHANGE",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(draftExchange);
        var exStatus = await Assert.ThrowsAsync<BusinessException>(() =>
            serviceWithRepos.CreateExchangePurchaseOrdersAsync(draftExchange.Id));
        Assert.Equal("INVALID_STATUS", exStatus.ErrorCode);

        // 4. Cost share unpaid
        var unpaidExchange = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-UNPAID-EX",
            Status = "APPROVED",
            ResolutionType = "EXCHANGE",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            StorePayableAmount = 500000,
            StorePaidAmount = 0,
            SettlementStatus = "UNPAID",
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = Guid.NewGuid(), ProductId = _prodId, ApprovedQty = 5, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(unpaidExchange);
        mockSupplierRepo.Setup(s => s.GetActiveReturnPolicyAsync(_supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierReturnPolicy { RequireStorePaymentBeforeReplacement = true });

        var exCostShare = await Assert.ThrowsAsync<BusinessException>(() =>
            serviceWithRepos.CreateExchangePurchaseOrdersAsync(unpaidExchange.Id));
        Assert.Equal("COST_SHARE_UNPAID", exCostShare.ErrorCode);

        // 5. Valid exchange: creates PO
        var validExchange = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-VALID-EX",
            Status = "APPROVED",
            ResolutionType = "EXCHANGE",
            GoodsReceiptId = _grnId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            StorePayableAmount = 0,
            SettlementStatus = "NOT_REQUIRED",
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine { Id = Guid.NewGuid(), ProductId = _prodId, ApprovedQty = 5, ExchangePoOrderedQty = 0, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(validExchange);

        mockPoRepo.Setup(p => p.GetBranchByIdAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = _branchId, BranchCode = "BR01", Name = "Chi nhánh 1" });
        mockPoRepo.Setup(p => p.GetSupplierByIdAsync(_supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = _supplierId, Code = "SUP01", Name = "NCC 1" });
        mockPoRepo.Setup(p => p.GeneratePoCodeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("PO-EX-20261101-001");

        PurchaseOrder? capturedPo = null;
        mockPoRepo.Setup(p => p.AddPurchaseOrderAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Callback<PurchaseOrder, CancellationToken>((po, _) => capturedPo = po)
            .Returns(Task.CompletedTask);

        var poSummaries = await serviceWithRepos.CreateExchangePurchaseOrdersAsync(validExchange.Id);

        Assert.NotEmpty(poSummaries);
        Assert.NotNull(capturedPo);
        Assert.Equal("SUPPLIER_RETURN", capturedPo.SourceType);
        Assert.Equal("CONFIRMED", capturedPo.Status);
        Assert.Equal(validExchange.Id, capturedPo.SourceSupplierReturnId);
        Assert.Equal("WAITING_RECEIPT", validExchange.ReplacementStatus);
    }

    [Fact]
    public async Task RecordCostSharePaymentAsync_Flow()
    {
        // 1. Not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.RecordCostSharePaymentAsync(Guid.NewGuid(), new RecordCostSharePaymentCommand(100000, null)));

        // 2. Invalid amount
        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-rtv",
            Code = "RTV-PAY-01",
            Status = "APPROVED",
            GoodsReceiptId = _grnId,
            StorePayableAmount = 500000,
            StorePaidAmount = 0,
            SettlementStatus = "UNPAID"
        };
        _repo.Returns.Add(ret);

        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RecordCostSharePaymentAsync(ret.Id, new RecordCostSharePaymentCommand(0, null)));

        // 3. Partial payment
        await _service.RecordCostSharePaymentAsync(ret.Id, new RecordCostSharePaymentCommand(200000, "CK đợt 1"));
        Assert.Equal(200000, ret.StorePaidAmount);
        Assert.Equal("PARTIALLY_PAID", ret.SettlementStatus);

        // 4. Full payment
        await _service.RecordCostSharePaymentAsync(ret.Id, new RecordCostSharePaymentCommand(300000, "CK đợt 2"));
        Assert.Equal(500000, ret.StorePaidAmount);
        Assert.Equal("PAID", ret.SettlementStatus);
    }

    [Fact]
    public async Task GetRelatedPurchaseOrdersAsync_NotFound_And_Success()
    {
        var mockPoRepo = new Mock<IPurchaseOrderRepository>();
        var mockSupRepo = new Mock<ISupplierRepository>();
        var serviceWithPo = new SupplierReturnService(
            _repo,
            mockPoRepo.Object,
            mockSupRepo.Object,
            _tenantContext,
            _uow);

        // 1. Not found throws
        await Assert.ThrowsAsync<NotFoundException>(() =>
            serviceWithPo.GetRelatedPurchaseOrdersAsync(Guid.NewGuid()));

        // 2. Found
        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-REL-01",
            Status = "APPROVED",
            GoodsReceiptId = _grnId
        };
        _repo.Returns.Add(ret);

        var poList = new List<PurchaseOrder>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "PO-EX-001",
                Status = "ORDERED",
                SourceSupplierReturnId = retId,
                TotalQty = 10,
                TotalAmount = 300_000m,
                CreatedAt = DateTime.UtcNow
            }
        };

        mockPoRepo
            .Setup(p => p.GetPagedPurchaseOrdersAsync(null, null, null, null, null, 1, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((poList, 1));

        var related = await serviceWithPo.GetRelatedPurchaseOrdersAsync(retId);
        Assert.Single(related);
        Assert.Equal("PO-EX-001", related[0].Code);
    }

    [Fact]
    public async Task ConfirmReturnAsync_WithEmailSender_SendsNotificationEmail()
    {
        var mockPoRepo = new Mock<IPurchaseOrderRepository>();
        var mockSupRepo = new Mock<ISupplierRepository>();
        var mockEmailSender = new Mock<ISupplierOrderEmailSender>();

        var service = new SupplierReturnService(
            _repo,
            mockPoRepo.Object,
            mockSupRepo.Object,
            _tenantContext,
            _uow,
            mockEmailSender.Object);

        var supplier = new Supplier
        {
            Id = _supplierId,
            TenantId = "tenant-test-rtv",
            Name = "Công ty TNHH Sữa Chuẩn",
            Email = "contact@suachuan.vn"
        };
        mockPoRepo.Setup(p => p.GetSupplierByIdAsync(_supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        var policy = new SupplierReturnPolicy
        {
            Id = Guid.NewGuid(),
            SupplierId = _supplierId,
            AllowReturn = true,
            MaxReturnDaysAfterReceipt = 30,
            DefaultResolutionType = "EXCHANGE",
            CostShareEnabled = false
        };
        mockSupRepo.Setup(s => s.GetActiveReturnPolicyAsync(_supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-EMAIL-01",
            Status = "DRAFT",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Reason = "Hàng bị lỗi hỏng do vận chuyển",
            ResolutionType = "EXCHANGE",
            Lines = new List<SupplierReturnLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = _prodId,
                    RequestedQty = 10,
                    ApprovedQty = 10,
                    UnitCost = 25000,
                    BatchCode = "B-EMAIL"
                }
            }
        };
        _repo.Returns.Add(ret);

        var result = await service.SubmitAsync(retId, "Gửi xác nhận và email thông báo");

        Assert.Equal("APPROVED", result.Status);
        mockEmailSender.Verify(e => e.SendSupplierOrderAsync(
            It.Is<SupplierOrderEmailMessage>(m => m.To == "contact@suachuan.vn" && m.Subject.Contains("TH-EMAIL-01")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmReturnAsync_WithCategoryCostShares_CalculatesLineSharesAccurately()
    {
        var mockPoRepo = new Mock<IPurchaseOrderRepository>();
        var mockSupRepo = new Mock<ISupplierRepository>();
        var catAId = Guid.NewGuid();
        var catBId = Guid.NewGuid();

        var policy = new SupplierReturnPolicy
        {
            Id = Guid.NewGuid(),
            SupplierId = _supplierId,
            AllowReturn = true,
            MaxReturnDaysAfterReceipt = 30,
            DefaultResolutionType = "EXCHANGE",
            CostShareEnabled = true,
            CostShareByCategoryEnabled = true,
            StoreCostSharePercent = 30m,
            SupplierCostSharePercent = 70m,
            CategoryCostShares = new List<SupplierReturnPolicyCategoryCostShare>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    CategoryId = catAId,
                    StoreCostSharePercent = 40m,
                    SupplierCostSharePercent = 60m,
                    IsDeleted = false
                }
            }
        };
        mockSupRepo.Setup(s => s.GetActiveReturnPolicyAsync(_supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var service = new SupplierReturnService(
            _repo,
            mockPoRepo.Object,
            mockSupRepo.Object,
            _tenantContext,
            _uow);

        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-CAT-SHARE",
            Status = "DRAFT",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Reason = "Hàng bị lỗi hỏng do vận chuyển",
            ResolutionType = "EXCHANGE",
            Lines = new List<SupplierReturnLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    CategoryId = catAId,
                    RequestedQty = 10,
                    ApprovedQty = 10,
                    UnitCost = 100_000,
                    ReplacementUnitPrice = 100_000
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    CategoryId = catBId,
                    RequestedQty = 10,
                    ApprovedQty = 10,
                    UnitCost = 100_000,
                    ReplacementUnitPrice = 100_000
                }
            }
        };
        _repo.Returns.Add(ret);

        var result = await service.SubmitAsync(retId, "Xác nhận chia sẻ chi phí");

        Assert.Equal("APPROVED", result.Status);
        // Line A: category specific 40% store -> 400_000 store, 600_000 supplier
        Assert.Equal(40m, result.Lines[0].StoreCostSharePercentSnapshot);
        Assert.Equal(60m, result.Lines[0].SupplierCostSharePercentSnapshot);
        Assert.Equal(400_000m, result.Lines[0].StorePayableAmount);
        Assert.Equal(600_000m, result.Lines[0].SupplierBearAmount);

        // Line B: fallback default 30% store -> 300_000 store, 700_000 supplier
        Assert.Equal(30m, result.Lines[1].StoreCostSharePercentSnapshot);
        Assert.Equal(70m, result.Lines[1].SupplierCostSharePercentSnapshot);
        Assert.Equal(300_000m, result.Lines[1].StorePayableAmount);
        Assert.Equal(700_000m, result.Lines[1].SupplierBearAmount);

        // Total store payable = 700_000m
        Assert.Equal(700_000m, result.StorePayableAmount);
        Assert.Equal("UNPAID", result.SettlementStatus);
        Assert.Equal("WAITING_PAYMENT", result.ReplacementStatus);
    }

    [Fact]
    public async Task RecordCostSharePaymentAsync_FullPaymentFlow_UpdatesSettlementAndTriggersAutoPo()
    {
        var mockPoRepo = new Mock<IPurchaseOrderRepository>();
        var mockSupRepo = new Mock<ISupplierRepository>();

        var service = new SupplierReturnService(
            _repo,
            mockPoRepo.Object,
            mockSupRepo.Object,
            _tenantContext,
            _uow);

        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-COST-PAY",
            Status = "APPROVED",
            SupplierId = _supplierId,
            BranchId = _branchId,
            ResolutionType = "EXCHANGE",
            StorePayableAmount = 400_000m,
            StorePaidAmount = 0m,
            SettlementStatus = "UNPAID",
            ReplacementStatus = "WAITING_PAYMENT",
            Lines = new List<SupplierReturnLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = _prodId,
                    ApprovedQty = 10,
                    UnitCost = 40000
                }
            }
        };
        _repo.Returns.Add(ret);

        // 1. Partial payment: 200,000 -> PARTIALLY_PAID
        var res1 = await service.RecordCostSharePaymentAsync(retId, new RecordCostSharePaymentCommand(200_000m, "Dợt 1"));
        Assert.Equal(200_000m, res1.StorePaidAmount);
        Assert.Equal("PARTIALLY_PAID", res1.SettlementStatus);

        // 2. Remaining payment: 200,000 -> PAID & WAITING_PURCHASE_ORDER
        var res2 = await service.RecordCostSharePaymentAsync(retId, new RecordCostSharePaymentCommand(200_000m, "Đợt 2"));
        Assert.Equal(400_000m, res2.StorePaidAmount);
        Assert.Equal("PAID", res2.SettlementStatus);
    }

    [Fact]
    public async Task CreateAsync_WithAutoSubmit_ReleasesQuarantineAndReservesStock()
    {
        var stock = _repo.Stocks.First(s => s.ProductId == _prodId);
        stock.QuantityQuarantined = 15;
        var initialReserved = stock.QuantityReserved;

        var command = new CreateSupplierReturnCommand(
            GoodsReceiptId: _grnId,
            Reason: "Hàng sắp hết hạn",
            ReasonNotes: "Date còn dưới 10 ngày",
            AttachmentUrl: "https://example.com/evidence.jpg",
            Lines: new List<CreateSupplierReturnLineDto>
            {
                new CreateSupplierReturnLineDto(_prodId, null, "B01", null, 20, 30000, null)
            },
            AutoSubmit: true
        );

        var result = await _service.CreateAsync(command);
        Assert.NotNull(result);
        Assert.Equal(0, stock.QuantityQuarantined);
        Assert.Equal(initialReserved + 20, stock.QuantityReserved);
    }

    [Fact]
    public async Task RecordSupplierResponse_ApprovedFull_CopiesRequestedQtyToApproved()
    {
        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-FULL-APPROVE",
            Status = "PENDING_SUPPLIER",
            SupplierId = _supplierId,
            BranchId = _branchId,
            ResolutionType = "EXCHANGE",
            TotalRequestedQty = 15,
            Lines = new List<SupplierReturnLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = _prodId,
                    RequestedQty = 15,
                    ApprovedQty = 0,
                    SupplierAcceptedQty = 0,
                    UnitCost = 30000
                }
            }
        };
        _repo.Returns.Add(ret);

        var cmd = new RecordSupplierResponseCommand("APPROVED_FULL", "Đồng ý đổi đủ 15 sản phẩm", null);
        var res = await _service.RecordSupplierResponseAsync(retId, cmd);

        Assert.Equal("APPROVED", res.Status);
        Assert.Equal(15, res.TotalApprovedQty);
        Assert.Equal(15, ret.Lines.First().ApprovedQty);
        Assert.Equal(15, ret.Lines.First().SupplierAcceptedQty);
        Assert.Equal(0, ret.Lines.First().RejectedQty);
    }

    [Fact]
    public async Task RecordSupplierResponse_InvalidResultType_ThrowsBusinessException()
    {
        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-INVALID-RES",
            Status = "PENDING_SUPPLIER",
            SupplierId = _supplierId,
            BranchId = _branchId,
            ResolutionType = "EXCHANGE",
            TotalRequestedQty = 10,
            Lines = new List<SupplierReturnLine>
            {
                new() { Id = Guid.NewGuid(), ProductId = _prodId, RequestedQty = 10, UnitCost = 30000 }
            }
        };
        _repo.Returns.Add(ret);

        var cmd = new RecordSupplierResponseCommand("UNKNOWN_TYPE", "Phản hồi lạ", null);
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RecordSupplierResponseAsync(retId, cmd));
        Assert.Contains("không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateExchangePurchaseOrdersAsync_ZeroRemainingEligibleQty_ThrowsNoQtyException()
    {
        var retId = Guid.NewGuid();
        var ret = new SupplierReturn
        {
            Id = retId,
            TenantId = "tenant-test-rtv",
            Code = "TH-NO-QTY",
            Status = "APPROVED",
            SupplierId = _supplierId,
            BranchId = _branchId,
            ResolutionType = "EXCHANGE",
            ReplacementStatus = "PENDING",
            TotalRequestedQty = 10,
            TotalApprovedQty = 10,
            Lines = new List<SupplierReturnLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = _prodId,
                    RequestedQty = 10,
                    ApprovedQty = 10,
                    SupplierAcceptedQty = 10,
                    ExchangePoOrderedQty = 10,
                    UnitCost = 30000
                }
            }
        };
        _repo.Returns.Add(ret);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateExchangePurchaseOrdersAsync(retId));
        Assert.Equal("NO_QTY", ex.ErrorCode);
    }

    private class FakeSupplierReturnRepository : ISupplierReturnRepository
    {
        public List<SupplierReturn> Returns { get; } = new();
        public List<EligibleGrnLookupDto> EligibleGrns { get; } = new();
        public List<GrnReturnableLineDto> ReturnableLines { get; } = new();
        public List<BranchStock> Stocks { get; } = new();

        public Task<PagedResult<SupplierReturnListItemDto>> GetPagedAsync(string tenantId, SupplierReturnFilterQuery query, CancellationToken cancellationToken = default)
        {
            var dtos = Returns.Where(r => r.TenantId == tenantId).Select(r => new SupplierReturnListItemDto(
                r.Id, r.Code, r.GoodsReceiptId, "GRN-01", r.SupplierId, "NCC-01", "NCC Hương Quê",
                r.BranchId, "BR01", "Chi nhánh 1", r.Status, r.Reason, r.Lines.Count,
                r.TotalRequestedQty, r.TotalApprovedQty, r.TotalHandedOverQty, r.TotalEstimatedAmount,
                r.CreatedAt, r.HandedOverAt)).ToList();

            return Task.FromResult(new PagedResult<SupplierReturnListItemDto>
            {
                Items = dtos,
                TotalItems = dtos.Count,
                Page = query.PageIndex,
                PageSize = query.PageSize
            });
        }

        public Task<SupplierReturn?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Returns.FirstOrDefault(r => r.TenantId == tenantId && r.Id == id));
        }

        public Task<SupplierReturn> AddAsync(SupplierReturn entity, CancellationToken cancellationToken = default)
        {
            Returns.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(SupplierReturn entity, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> GenerateCodeAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"RTV-20261030-{Returns.Count + 1:D3}");
        }

        public Task<List<EligibleGrnLookupDto>> GetEligibleGrnsAsync(string tenantId, Guid? branchId, Guid? supplierId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EligibleGrns.ToList());
        }

        public Task<List<GrnReturnableLineDto>> GetGrnReturnableLinesAsync(string tenantId, Guid grnId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReturnableLines.ToList());
        }

        public Task<BranchStock?> GetBranchStockAsync(string tenantId, Guid branchId, Guid productId, string? batchCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Stocks.FirstOrDefault(s => s.TenantId == tenantId && s.BranchId == branchId && s.ProductId == productId));
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { CurrentTenantId = tenantId; }
        public void SetRole(string? role) { CurrentRole = role ?? string.Empty; }
        public FakeTenantContext(string tenantId) { CurrentTenantId = tenantId; }
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
