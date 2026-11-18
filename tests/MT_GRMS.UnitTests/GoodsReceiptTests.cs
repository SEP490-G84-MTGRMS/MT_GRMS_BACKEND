using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class GoodsReceiptTests
{
    private readonly FakeGoodsReceiptRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly GoodsReceiptService _service;

    private readonly Guid _branchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _supplierId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _poId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _milkProdId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly Guid _waterProdId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public GoodsReceiptTests()
    {
        _repo = new FakeGoodsReceiptRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new GoodsReceiptService(_repo, _uow, _tenantContext);

        // Seed PO
        var po = new PurchaseOrder
        {
            Id = _poId,
            TenantId = "tenant-test-1",
            Code = "PO-20261030-001",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "SENT",
            TotalQty = 100,
            TotalAmount = 2500000,
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Supplier = new Supplier
            {
                Id = _supplierId,
                TenantId = "tenant-test-1",
                Code = "SUP-01",
                Name = "Công ty Tươi Sạch"
            },
            Branch = new Branch
            {
                Id = _branchId,
                TenantId = "tenant-test-1",
                BranchCode = "BR-01",
                Name = "Chi nhánh Nguyễn Trãi"
            }
        };

        po.Details.Add(new PurchaseOrderDetail
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = _poId,
            ProductId = _milkProdId,
            OrderedQty = 100,
            UnitPrice = 25000,
            LineAmount = 2500000,
            Product = new Product
            {
                Id = _milkProdId,
                TenantId = "tenant-test-1",
                ProductCode = "SKU-MILK",
                Name = "Sữa Tươi Tiệt Trùng 1L",
                BaseUnit = "Hộp",
                HasBatches = true
            }
        });

        _repo.PurchaseOrders.Add(po);
    }

    [Fact]
    public async Task CreateGoodsReceipt_WithValidData_ReturnsDraftReceipt()
    {
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            InvoiceNo = "INV-001",
            FreightFee = 50000,
            DiscountAmount = 0,
            VatPercent = 10,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 95,
                    AcceptedQty = 90,
                    RejectedQty = 5,
                    UnitPrice = 25000,
                    BatchCode = "LOT-2026A",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6),
                    RejectionReason = "DAMAGE",
                    RejectionNote = "Hộp bị móp méo khi vận chuyển"
                }
            }
        };

        var result = await _service.CreateGoodsReceiptAsync(command);

        Assert.NotNull(result);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(90, result.TotalAcceptedQty);
        Assert.Equal(5, result.TotalRejectedQty);
        Assert.Equal(5, result.TotalShortageQty);
        Assert.True(result.HasMismatch);
    }

    [Fact]
    public async Task CreateGoodsReceipt_WhenAcceptedExceedsDelivered_ThrowsBusinessException()
    {
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 80,
                    AcceptedQty = 90, // Lỗi: Accepted > Delivered
                    UnitPrice = 25000
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(command));
        Assert.Equal("ACCEPTED_EXCEEDS_RECEIVED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_WhenAcceptedExceedsOrdered_ThrowsBusinessException()
    {
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 110,
                    AcceptedQty = 105, // Lỗi: không cho phép nhập kho vượt quá số lượng đặt PO
                    UnitPrice = 25000
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(command));
        Assert.Equal("EXCEEDS_ORDERED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_SplitLotsSameSku_WithinOrdered_Succeeds()
    {
        var podId = _repo.PurchaseOrders[0].Details.First().Id;
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            AutoComplete = true,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 100,
                    DeliveredQty = 40,
                    AcceptedQty = 40,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    BatchCode = "LOT-A",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-2),
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                },
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 100,
                    DeliveredQty = 60,
                    AcceptedQty = 60,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    BatchCode = "LOT-B",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(10)
                }
            }
        };

        var result = await _service.CreateGoodsReceiptAsync(command);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(100, result.TotalAcceptedQty);
        Assert.Equal(2, _repo.Stocks.Count);
    }

    [Fact]
    public async Task CreateGoodsReceipt_SplitLotsExceedOrdered_Throws()
    {
        var podId = _repo.PurchaseOrders[0].Details.First().Id;
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 100,
                    DeliveredQty = 40,
                    AcceptedQty = 40,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                },
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 100,
                    DeliveredQty = 70,
                    AcceptedQty = 70,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(8)
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(command));
        Assert.Equal("EXCEEDS_ORDERED", ex.ErrorCode);
    }

    [Fact]
    public async Task CompleteGoodsReceipt_UpdatesStockAndMarksPoCompleted()
    {
        // 1. Tạo receipt DRAFT
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    BatchCode = "LOT-100",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(12)
                }
            }
        };
        var draft = await _service.CreateGoodsReceiptAsync(command);

        // 2. Hoàn tất phiếu
        var completed = await _service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand
        {
            Note = "Đã kiểm đếm đủ 100 hộp"
        });

        Assert.Equal("COMPLETED", completed.Status);

        // Kiểm tra tồn kho BranchStock đã được cập nhật
        var stock = await _repo.GetBranchStockAsync("tenant-test-1", _branchId, _milkProdId, "LOT-100");
        Assert.NotNull(stock);
        Assert.Equal(100, stock.QuantityOnHand);

        // Kiểm tra lô hàng FEFO (Batch) đã được sinh tự động
        Assert.Single(_repo.Batches);
        Assert.Equal("LOT-100", _repo.Batches[0].BatchCode);
        Assert.Equal(100, _repo.Batches[0].InitialQuantity);

        // Kiểm tra công nợ nhà cung cấp (SupplierAPLedger) đã được sinh
        Assert.Single(_repo.Ledgers);
        Assert.Equal(2500000, _repo.Ledgers[0].Amount);

        // Kiểm tra đơn PO gốc đã chuyển sang COMPLETED
        var po = _repo.PurchaseOrders.First(p => p.Id == _poId);
        Assert.Equal("COMPLETED", po.Status);
    }

    [Fact]
    public async Task CancelGoodsReceipt_WhenDraft_CancelsAndReleasesPo()
    {
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 50,
                    AcceptedQty = 50,
                    UnitPrice = 25000
                }
            }
        };
        var draft = await _service.CreateGoodsReceiptAsync(command);

        var cancelled = await _service.CancelGoodsReceiptAsync(draft.Id, new CancelGoodsReceiptCommand
        {
            Reason = "Xe hàng bị tai nạn, hủy phiếu để đặt chuyến khác"
        });

        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Equal("Xe hàng bị tai nạn, hủy phiếu để đặt chuyến khác", cancelled.CancellationReason);

        // Đảm bảo không có tồn kho phát sinh
        var stock = await _repo.GetBranchStockAsync("tenant-test-1", _branchId, _milkProdId, null);
        Assert.Null(stock);
    }

    [Fact]
    public async Task CancelGoodsReceipt_WhenCompleted_ThrowsBusinessException()
    {
        var command = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            AutoComplete = true,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    BatchCode = "LOT-OK",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            }
        };
        var completed = await _service.CreateGoodsReceiptAsync(command);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelGoodsReceiptAsync(completed.Id, new CancelGoodsReceiptCommand { Reason = "Muốn hủy" }));

        Assert.Equal("CANNOT_CANCEL_COMPLETED", ex.ErrorCode);
    }

    [Fact]
    public async Task GetGoodsReceiptById_NonExistent_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetGoodsReceiptByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPoLookupAsync_ReturnsEligibleOrders()
    {
        var result = await _service.GetPoLookupAsync(_branchId);
        Assert.NotEmpty(result);
        Assert.Contains(result, p => p.Id == _poId);
    }

    [Fact]
    public async Task GetPoReceiptDetails_ByGuidAndByCode_Success()
    {
        // By GUID
        var byGuid = await _service.GetPoReceiptDetailsAsync(_poId.ToString());
        Assert.NotNull(byGuid);
        Assert.Equal(_poId, byGuid.Id);

        // By Code
        var byCode = await _service.GetPoReceiptDetailsAsync("PO-20261030-001");
        Assert.NotNull(byCode);
        Assert.Equal(_poId, byCode.Id);

        // Non-existent
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetPoReceiptDetailsAsync("PO-NON-EXISTENT"));
    }

    [Fact]
    public async Task GetPoReceiptDetails_ActiveReceiptExists_ThrowsBusinessException()
    {
        // GIVEN: Active draft receipt on PO
        var draftCmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    UnitPrice = 25000,
                    BatchCode = "LOT-01",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(11)
                }
            }
        };
        await _service.CreateGoodsReceiptAsync(draftCmd);

        // WHEN & THEN: Cannot query details to create another GRN
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.GetPoReceiptDetailsAsync(_poId.ToString()));
        Assert.Equal("PO_HAS_ACTIVE_RECEIPT", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_NegativeQuantities_ThrowsBusinessException()
    {
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = -10,
                    AcceptedQty = 10,
                    UnitPrice = 25000
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("INVALID_QTY", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_AcceptedExceedsDelivered_ThrowsBusinessException()
    {
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 50,
                    AcceptedQty = 60,
                    UnitPrice = 25000
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("ACCEPTED_EXCEEDS_RECEIVED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_DeliveredExceedsOrdered_ThrowsBusinessException()
    {
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 120,
                    AcceptedQty = 100,
                    UnitPrice = 25000
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("EXCEEDS_ORDERED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_RejectedWithoutReason_ThrowsBusinessException()
    {
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 80,
                    RejectedQty = 20,
                    RejectionReason = null, // Missing!
                    UnitPrice = 25000
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("REJECTION_REASON_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_BatchProduct_MissingMfgOrExp_ThrowsBusinessException()
    {
        // Missing MFG
        var cmdNoMfg = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    ManufactureDate = null,
                    ExpiryDate = DateTime.UtcNow.AddMonths(12)
                }
            }
        };
        var ex1 = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmdNoMfg));
        Assert.Equal("MFG_REQUIRED", ex1.ErrorCode);

        // Missing EXP
        var cmdNoExp = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = null
                }
            }
        };
        var ex2 = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmdNoExp));
        Assert.Equal("EXPIRY_DATE_REQUIRED", ex2.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceipt_BatchProduct_PastExpiry_ThrowsBusinessException()
    {
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    UnitPrice = 25000,
                    ManageBatch = true,
                    ManufactureDate = DateTime.UtcNow.AddMonths(-12),
                    ExpiryDate = DateTime.UtcNow.AddDays(-2) // Past expiry!
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("EXPIRY_PAST", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateDraftGoodsReceipt_NotDraftOrNotFound_ThrowsAppropriateException()
    {
        // Not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(Guid.NewGuid(), new CreateGoodsReceiptCommand()));

        // Completed receipt cannot be edited
        var completedReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-COMPLETED",
            Status = "COMPLETED",
            BranchId = _branchId,
            SupplierId = _supplierId
        };
        _repo.Receipts.Add(completedReceipt);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(completedReceipt.Id, new CreateGoodsReceiptCommand()));
        Assert.Equal("GRN_NOT_DRAFT", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateDraftGoodsReceipt_ValidDraft_UpdatesLinesAndTotals()
    {
        // GIVEN: Draft receipt
        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-DRAFT-01",
            Status = "DRAFT",
            BranchId = _branchId,
            SupplierId = _supplierId,
            PurchaseOrderId = _poId
        };
        _repo.Receipts.Add(draft);

        var updateCmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            InvoiceNo = "INV-UPDATED",
            FreightFee = 20000,
            DiscountAmount = 10000,
            VatPercent = 10,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = _repo.PurchaseOrders.First().Details.First().Id,
                    OrderedQty = 100,
                    DeliveredQty = 90,
                    AcceptedQty = 90,
                    UnitPrice = 25000,
                    BatchCode = "LOT-UPDATED",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(11),
                    ManageBatch = true
                }
            }
        };

        // WHEN
        var updated = await _service.UpdateDraftGoodsReceiptAsync(draft.Id, updateCmd);

        // THEN
        Assert.Equal("DRAFT", updated.Status);
        Assert.Equal("INV-UPDATED", updated.InvoiceNo);
        Assert.Equal(90m * 25000m, updated.SubTotal);
    }

    [Fact]
    public async Task CancelGoodsReceipt_Validations()
    {
        // Non existent
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CancelGoodsReceiptAsync(Guid.NewGuid(), new CancelGoodsReceiptCommand { Reason = "test" }));

        // Already cancelled
        var cancelledReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-ALREADY-CANCELLED",
            Status = "CANCELLED",
            BranchId = _branchId,
            SupplierId = _supplierId
        };
        _repo.Receipts.Add(cancelledReceipt);

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelGoodsReceiptAsync(cancelledReceipt.Id, new CancelGoodsReceiptCommand { Reason = "test" }));
        Assert.Equal("ALREADY_CANCELLED", ex1.ErrorCode);

        // Missing reason
        var draftReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-DRAFT-CANCEL",
            Status = "DRAFT",
            BranchId = _branchId,
            SupplierId = _supplierId
        };
        _repo.Receipts.Add(draftReceipt);

        var ex2 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelGoodsReceiptAsync(draftReceipt.Id, new CancelGoodsReceiptCommand { Reason = "" }));
        Assert.Equal("REASON_REQUIRED", ex2.ErrorCode);
    }

    [Fact]
    public async Task CompleteGoodsReceipt_ExchangePo_DoesNotCreateApLedger()
    {
        // GIVEN: Exchange PO
        var exPoId = Guid.NewGuid();
        var exPo = new PurchaseOrder
        {
            Id = exPoId,
            TenantId = "tenant-test-1",
            Code = "PO-EXCHANGE-001",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "SENT",
            SourceType = "SUPPLIER_RETURN",
            TotalQty = 50,
            TotalAmount = 1250000,
            Supplier = _repo.PurchaseOrders.First().Supplier,
            Branch = _repo.PurchaseOrders.First().Branch
        };
        exPo.Details.Add(new PurchaseOrderDetail
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = exPoId,
            ProductId = _milkProdId,
            OrderedQty = 50,
            UnitPrice = 25000,
            Product = _repo.PurchaseOrders.First().Details.First().Product
        });
        _repo.PurchaseOrders.Add(exPo);

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = exPoId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            AutoComplete = true,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = exPo.Details.First().Id,
                    OrderedQty = 50,
                    DeliveredQty = 50,
                    AcceptedQty = 50,
                    UnitPrice = 25000,
                    BatchCode = "BAT-EX-TEST-01",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(11),
                    ManageBatch = true
                }
            }
        };

        // WHEN
        var res = await _service.CreateGoodsReceiptAsync(cmd);

        // THEN
        Assert.Equal("COMPLETED", res.Status);
        // Verify NO AP ledger entry created for exchange PO
        Assert.DoesNotContain(_repo.Ledgers, l => l.DocNo == res.Code);
    }

    [Fact]
    public async Task CompleteGoodsReceipt_CashOrBankImmediatePayment_CreatesInvoiceAndVoucherPair_BalanceUnchanged()
    {
        // GIVEN: PO for CASH payment
        var cashPoId = Guid.NewGuid();
        var cashPo = new PurchaseOrder
        {
            Id = cashPoId,
            TenantId = "tenant-test-1",
            Code = "PO-CASH-001",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "SENT",
            TotalQty = 40,
            TotalAmount = 1000000,
            Supplier = _repo.PurchaseOrders.First().Supplier,
            Branch = _repo.PurchaseOrders.First().Branch
        };
        cashPo.Details.Add(new PurchaseOrderDetail
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = cashPoId,
            ProductId = _milkProdId,
            OrderedQty = 40,
            UnitPrice = 25000,
            Product = _repo.PurchaseOrders.First().Details.First().Product
        });
        _repo.PurchaseOrders.Add(cashPo);

        var initialDebt = cashPo.Supplier!.CurrentDebt;

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = cashPoId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            AutoComplete = true,
            PaymentMethod = "CASH",
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = _milkProdId,
                    PurchaseOrderDetailId = cashPo.Details.First().Id,
                    OrderedQty = 40,
                    DeliveredQty = 40,
                    AcceptedQty = 40,
                    UnitPrice = 25000,
                    BatchCode = "BAT-CASH-01",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(11),
                    ManageBatch = true
                }
            }
        };

        // WHEN
        var res = await _service.CreateGoodsReceiptAsync(cmd);

        // THEN
        Assert.Equal("COMPLETED", res.Status);
        // Pair created: PurchaseInvoice and PaymentVoucher
        var invoice = _repo.Ledgers.FirstOrDefault(l => l.DocNo == res.Code && l.DocType == APDocType.PurchaseInvoice);
        var voucher = _repo.Ledgers.FirstOrDefault(l => l.DocNo == $"PAY-{res.Code}" && l.DocType == APDocType.PaymentVoucher);
        Assert.NotNull(invoice);
        Assert.NotNull(voucher);
        Assert.Equal(initialDebt, cashPo.Supplier.CurrentDebt); // Current debt unchanged!
    }

    [Fact]
    public async Task GetGoodsReceiptsAsync_Paged_ReturnsReceipts()
    {
        var filter = new GoodsReceiptFilterQuery();
        var result = await _service.GetGoodsReceiptsAsync(filter);
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WithRejectedLines_CreatesSupplierReturnAutomatically()
    {
        var mockReturnRepo = new Mock<ISupplierReturnRepository>();
        mockReturnRepo.Setup(r => r.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("TH-REJECT-001");

        SupplierReturn? capturedReturn = null;
        mockReturnRepo.Setup(r => r.AddAsync(It.IsAny<SupplierReturn>(), It.IsAny<CancellationToken>()))
            .Callback<SupplierReturn, CancellationToken>((sr, ct) => capturedReturn = sr)
            .ReturnsAsync((SupplierReturn sr, CancellationToken ct) => sr);

        var service = new GoodsReceiptService(_repo, _uow, _tenantContext, mockReturnRepo.Object);

        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-REJECT-01",
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "DRAFT",
            ReceiverUserId = Guid.NewGuid().ToString(),
            ReceivedAtUtc = DateTime.UtcNow,
            Lines = new List<GoodsReceiptLine>
            {
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 10,
                    DeliveredQty = 10,
                    AcceptedQty = 7,
                    RejectedQty = 3,
                    UnitPrice = 25000,
                    RejectionReason = "DAMAGED",
                    Note = "Bao bì rách hỏng do vận chuyển",
                    BatchCode = "BATCH-REJECT",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            }
        };
        _repo.Receipts.Add(draft);

        var result = await service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand { Note = "Xong" });

        Assert.Equal("COMPLETED", result.Status);
        Assert.NotNull(capturedReturn);
        Assert.Equal("TH-REJECT-001", capturedReturn!.Code);
        Assert.Equal("COMPLETED", capturedReturn.Status);
        Assert.Equal(3, capturedReturn.TotalRequestedQty);
        Assert.Equal("TH-REJECT-001", result.CreatedReturnCode);
        Assert.Equal(capturedReturn.Id, result.CreatedReturnId);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WithExchangePO_UpdatesSupplierReturn()
    {
        var mockReturnRepo = new Mock<ISupplierReturnRepository>();
        var sourceReturnId = Guid.NewGuid();
        var sourceLineId = Guid.NewGuid();

        var supplierReturn = new SupplierReturn
        {
            Id = sourceReturnId,
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Code = "TH-EXCHANGE-001",
            Status = "PENDING",
            Lines =
            [
                new SupplierReturnLine
                {
                    Id = sourceLineId,
                    SupplierReturnId = sourceReturnId,
                    ProductId = _milkProdId,
                    ApprovedQty = 20,
                    SupplierAcceptedQty = 20,
                    HandedOverQty = 0,
                    BatchCode = "OLD-BATCH-01"
                }
            ]
        };

        mockReturnRepo.Setup(r => r.GetByIdAsync("tenant-test-1", sourceReturnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplierReturn);

        var exchangePoId = Guid.NewGuid();
        var exchangePodId = Guid.NewGuid();
        var exchangePo = new PurchaseOrder
        {
            Id = exchangePoId,
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Code = "PO-EXCHANGE-001",
            Status = "ORDERED",
            SourceType = "SUPPLIER_RETURN",
            SourceSupplierReturnId = sourceReturnId,
            Details =
            [
                new PurchaseOrderDetail
                {
                    Id = exchangePodId,
                    PurchaseOrderId = exchangePoId,
                    ProductId = _milkProdId,
                    SourceSupplierReturnLineId = sourceLineId,
                    DeliveryUoM = "Hộp",
                    OrderedQty = 20,
                    FinalQty = 20,
                    EmployeeQty = 20,
                    StoreQty = 20,
                    UnitPrice = 25000,
                    Product = _repo.PurchaseOrders.First().Details.First().Product
                }
            ]
        };
        _repo.PurchaseOrders.Add(exchangePo);

        var service = new GoodsReceiptService(
            _repo,
            _uow,
            _tenantContext,
            mockReturnRepo.Object);

        // 1. Create draft receipt for exchange PO
        var draft = await service.CreateGoodsReceiptAsync(new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = exchangePoId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines =
            [
                new CreateGoodsReceiptLineCommand
                {
                    PurchaseOrderDetailId = exchangePodId,
                    ProductId = _milkProdId,
                    OrderedQty = 20,
                    DeliveredQty = 20,
                    AcceptedQty = 20,
                    RejectedQty = 0,
                    BatchCode = "NEW-BATCH-01",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            ]
        });

        // 2. Complete goods receipt
        var result = await service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand { Note = "Nhập hàng đổi" });

        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(20, supplierReturn.TotalHandedOverQty);
        Assert.Equal("COMPLETED", supplierReturn.Status);
        mockReturnRepo.Verify(r => r.UpdateAsync(supplierReturn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_PartialExchangePo_SetsPartiallyReceived()
    {
        var mockReturnRepo = new Mock<ISupplierReturnRepository>();
        var sourceReturnId = Guid.NewGuid();
        var sourceLineId = Guid.NewGuid();

        var supplierReturn = new SupplierReturn
        {
            Id = sourceReturnId,
            TenantId = "tenant-test-1",
            Code = "TH-PARTIAL-001",
            Status = "PROCESSING",
            TotalHandedOverQty = 0,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine
                {
                    Id = sourceLineId,
                    SupplierReturnId = sourceReturnId,
                    ProductId = _milkProdId,
                    HandedOverQty = 0,
                    ApprovedQty = 30
                }
            }
        };

        mockReturnRepo.Setup(r => r.GetByIdAsync("tenant-test-1", sourceReturnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplierReturn);

        var exchangePoId = Guid.NewGuid();
        var exchangePodId = Guid.NewGuid();
        var exchangePo = new PurchaseOrder
        {
            Id = exchangePoId,
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Code = "PO-EXCHANGE-PARTIAL",
            Status = "ORDERED",
            SourceType = "SUPPLIER_RETURN",
            SourceSupplierReturnId = sourceReturnId,
            Details =
            [
                new PurchaseOrderDetail
                {
                    Id = exchangePodId,
                    PurchaseOrderId = exchangePoId,
                    ProductId = _milkProdId,
                    SourceSupplierReturnLineId = sourceLineId,
                    DeliveryUoM = "Hộp",
                    OrderedQty = 30,
                    FinalQty = 30,
                    UnitPrice = 25000,
                    Product = _repo.PurchaseOrders.First().Details.First().Product
                }
            ]
        };
        _repo.PurchaseOrders.Add(exchangePo);

        var service = new GoodsReceiptService(_repo, _uow, _tenantContext, mockReturnRepo.Object);

        var draft = await service.CreateGoodsReceiptAsync(new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = exchangePoId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines =
            [
                new CreateGoodsReceiptLineCommand
                {
                    PurchaseOrderDetailId = exchangePodId,
                    ProductId = _milkProdId,
                    OrderedQty = 30,
                    DeliveredQty = 15,
                    AcceptedQty = 15,
                    RejectedQty = 0,
                    BatchCode = "NEW-BATCH-PARTIAL",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            ]
        });

        var result = await service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand { Note = "Nhận đợt 1" });
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("PARTIALLY_RECEIVED", exchangePo.Status);
        Assert.Contains("[EXCHANGE]", exchangePo.Note);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_ShortRegularPo_AppendsShortNoteAndClosesPo()
    {
        var shortPoId = Guid.NewGuid();
        var shortPodId = Guid.NewGuid();
        var shortPo = new PurchaseOrder
        {
            Id = shortPoId,
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Code = "PO-REGULAR-SHORT",
            Status = "ORDERED",
            SourceType = null,
            Details =
            [
                new PurchaseOrderDetail
                {
                    Id = shortPodId,
                    PurchaseOrderId = shortPoId,
                    ProductId = _milkProdId,
                    DeliveryUoM = "Hộp",
                    OrderedQty = 40,
                    FinalQty = 40,
                    UnitPrice = 25000,
                    Product = _repo.PurchaseOrders.First().Details.First().Product
                }
            ]
        };
        _repo.PurchaseOrders.Add(shortPo);

        var service = new GoodsReceiptService(_repo, _uow, _tenantContext);

        var draft = await service.CreateGoodsReceiptAsync(new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = shortPoId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines =
            [
                new CreateGoodsReceiptLineCommand
                {
                    PurchaseOrderDetailId = shortPodId,
                    ProductId = _milkProdId,
                    OrderedQty = 40,
                    DeliveredQty = 25,
                    AcceptedQty = 25,
                    RejectedQty = 0,
                    BatchCode = "BATCH-SHORT-01",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            ]
        });

        var result = await service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand { Note = "Nhận 25 hộp" });
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("COMPLETED", shortPo.Status);
        Assert.Contains("[P-SHORT-A]", shortPo.Note);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_MultipleRejectionReasons_FormatsReasonLabels()
    {
        var mockReturnRepo = new Mock<ISupplierReturnRepository>();
        SupplierReturn? capturedReturn = null;
        mockReturnRepo.Setup(r => r.AddAsync(It.IsAny<SupplierReturn>(), It.IsAny<CancellationToken>()))
            .Callback<SupplierReturn, CancellationToken>((sr, _) =>
            {
                capturedReturn = sr;
                sr.Code = "TH-MULTIREASONS";
            })
            .ReturnsAsync((SupplierReturn sr, CancellationToken _) => sr);

        var service = new GoodsReceiptService(_repo, _uow, _tenantContext, mockReturnRepo.Object);

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Status = "DRAFT",
            ReceiverUserId = Guid.NewGuid().ToString(),
            ReceivedAtUtc = DateTime.UtcNow,
            Lines = new List<GoodsReceiptLine>
            {
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 5,
                    AcceptedQty = 0,
                    RejectedQty = 5,
                    UnitPrice = 25000,
                    RejectionReason = "EXPIRED",
                    Note = "Hết date",
                    BatchCode = "B-EXP",
                    ExpiryDate = DateTime.UtcNow.AddMonths(-1)
                },
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 50,
                    AcceptedQty = 0,
                    RejectedQty = 5,
                    UnitPrice = 25000,
                    RejectionReason = "NEAR_EXPIRY",
                    Note = "Cận date",
                    BatchCode = "B-NEAR",
                    ExpiryDate = DateTime.UtcNow.AddDays(5)
                },
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 50,
                    AcceptedQty = 0,
                    RejectedQty = 5,
                    UnitPrice = 25000,
                    RejectionReason = "WRONG_SPEC",
                    Note = "Sai quy cách",
                    BatchCode = "B-SPEC"
                },
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 50,
                    AcceptedQty = 0,
                    RejectedQty = 5,
                    UnitPrice = 25000,
                    RejectionReason = "SHORT_DELIVERY",
                    Note = "Giao thiếu",
                    BatchCode = "B-SHORT"
                },
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 50,
                    AcceptedQty = 0,
                    RejectedQty = 5,
                    UnitPrice = 25000,
                    RejectionReason = "OTHER",
                    Note = "Khác",
                    BatchCode = "B-OTHER"
                }
            }
        };
        _repo.Receipts.Add(receipt);

        var result = await service.CompleteGoodsReceiptAsync(receipt.Id, new CompleteGoodsReceiptCommand { Note = "Hoàn tất kiểm đếm nhiều lý do" });
        Assert.Equal("COMPLETED", result.Status);
        Assert.NotNull(capturedReturn);
        Assert.Equal(5, capturedReturn!.Lines.Count);
    }

    [Fact]
    public async Task CreateDraftGoodsReceiptAsync_NonExistentPo_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CreateGoodsReceiptAsync(new CreateGoodsReceiptCommand
            {
                PurchaseOrderId = Guid.NewGuid(),
                SupplierId = _supplierId,
                BranchId = _branchId,
                ReceivedAtUtc = DateTime.UtcNow,
                Lines = new List<CreateGoodsReceiptLineCommand>()
            }));
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_NonExistentReceipt_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CompleteGoodsReceiptAsync(Guid.NewGuid(), new CompleteGoodsReceiptCommand()));
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_StatusNotDraft_ThrowsAppropriateException()
    {
        // 1. Completed
        var completedReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-CMP",
            Status = "COMPLETED"
        };
        _repo.Receipts.Add(completedReceipt);

        var exCompleted = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(completedReceipt.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("ALREADY_COMPLETED", exCompleted.ErrorCode);

        // 2. Cancelled
        var cancelledReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-CAN",
            Status = "CANCELLED"
        };
        _repo.Receipts.Add(cancelledReceipt);

        var exCancelled = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(cancelledReceipt.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("CANNOT_COMPLETE_CANCELLED", exCancelled.ErrorCode);
    }

    [Fact]
    public async Task UpdateDraftGoodsReceipt_ValidationErrors_ThrowsBusinessException()
    {
        var po = _repo.PurchaseOrders.First();

        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-DRAFT-VAL",
            PurchaseOrderId = po.Id,
            PurchaseOrder = po,
            Status = "DRAFT",
            Lines = new List<GoodsReceiptLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 10,
                    DeliveredQty = 10,
                    AcceptedQty = 10,
                    UnitPrice = 25000
                }
            }
        };
        _repo.Receipts.Add(draft);

        // 1. Negative quantity
        var exNeg = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(draft.Id, new CreateGoodsReceiptCommand
            {
                PurchaseOrderId = po.Id,
                SupplierId = po.SupplierId,
                BranchId = po.BranchId,
                Lines = new List<CreateGoodsReceiptLineCommand>
                {
                    new()
                    {
                        ProductId = _milkProdId,
                        OrderedQty = 10,
                        DeliveredQty = -1,
                        AcceptedQty = 0,
                        UnitPrice = 25000
                    }
                }
            }));
        Assert.Equal("INVALID_QTY", exNeg.ErrorCode);

        // 2. Accepted exceeds delivered
        var exAcc = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(draft.Id, new CreateGoodsReceiptCommand
            {
                PurchaseOrderId = po.Id,
                SupplierId = po.SupplierId,
                BranchId = po.BranchId,
                Lines = new List<CreateGoodsReceiptLineCommand>
                {
                    new()
                    {
                        ProductId = _milkProdId,
                        OrderedQty = 10,
                        DeliveredQty = 5,
                        AcceptedQty = 6,
                        UnitPrice = 25000
                    }
                }
            }));
        Assert.Equal("ACCEPTED_EXCEEDS_RECEIVED", exAcc.ErrorCode);

        // 3. Rejected without reason
        var exRej = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(draft.Id, new CreateGoodsReceiptCommand
            {
                PurchaseOrderId = po.Id,
                SupplierId = po.SupplierId,
                BranchId = po.BranchId,
                Lines = new List<CreateGoodsReceiptLineCommand>
                {
                    new()
                    {
                        ProductId = _milkProdId,
                        OrderedQty = 10,
                        DeliveredQty = 10,
                        AcceptedQty = 8,
                        RejectionReason = null,
                        UnitPrice = 25000
                    }
                }
            }));
        Assert.Equal("REJECTION_REASON_REQUIRED", exRej.ErrorCode);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_DamageWrongItemQualityIssue_FormatsReasonLabels()
    {
        var mockSupplierReturn = new Mock<ISupplierReturnRepository>();
        SupplierReturn? capturedReturn = null;
        mockSupplierReturn
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) => capturedReturn != null && capturedReturn.Id == id ? capturedReturn : null);
        mockSupplierReturn
            .Setup(r => r.AddAsync(It.IsAny<SupplierReturn>(), It.IsAny<CancellationToken>()))
            .Callback<SupplierReturn, CancellationToken>((ret, _) => capturedReturn = ret)
            .ReturnsAsync((SupplierReturn ret, CancellationToken _) => ret);
        mockSupplierReturn
            .Setup(r => r.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("SRN-20261116-0001");

        var service = new GoodsReceiptService(_repo, _uow, _tenantContext, mockSupplierReturn.Object);
        var po = _repo.PurchaseOrders.First();

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-DAMAGE-TEST",
            PurchaseOrderId = po.Id,
            PurchaseOrder = po,
            SupplierId = po.SupplierId,
            BranchId = po.BranchId,
            Status = "DRAFT",
            Lines = new List<GoodsReceiptLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 10,
                    AcceptedQty = 0,
                    RejectedQty = 10,
                    UnitPrice = 25000,
                    RejectionReason = "DAMAGE",
                    Note = "Hàng vỡ"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 10,
                    AcceptedQty = 0,
                    RejectedQty = 10,
                    UnitPrice = 25000,
                    RejectionReason = "WRONG_ITEM",
                    Note = "Giao sai mẫu"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 50,
                    DeliveredQty = 10,
                    AcceptedQty = 0,
                    RejectedQty = 10,
                    UnitPrice = 25000,
                    RejectionReason = "QUALITY_ISSUE",
                    Note = "Kém chất lượng"
                }
            }
        };
        _repo.Receipts.Add(receipt);

        var result = await service.CompleteGoodsReceiptAsync(receipt.Id, new CompleteGoodsReceiptCommand { Note = "Kiểm tra nhãn lý do" });
        Assert.Equal("COMPLETED", result.Status);
        Assert.NotNull(capturedReturn);
        Assert.Equal(3, capturedReturn!.Lines.Count);
    }

    [Fact]
    public async Task CreateGoodsReceiptAsync_PoAlreadyHasReceipt_ThrowsBusinessException()
    {
        var existingReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "COMPLETED"
        };
        _repo.Receipts.Add(existingReceipt);

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 10, AcceptedQty = 10 }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("PO_ALREADY_HAS_RECEIPT", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceiptAsync_AcceptedExceedsOrdered_ThrowsBusinessException()
    {
        var poSingle = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Details = new List<PurchaseOrderDetail>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, UnitPrice = 10000m }
            }
        };
        _repo.PurchaseOrders.Add(poSingle);

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = poSingle.Id,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 20, AcceptedQty = 15 }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("EXCEEDS_ORDERED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceiptAsync_ExchangePoWithDraftReceipt_ThrowsPoAlreadyHasReceipt()
    {
        var exchangePo = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "PO-EX-999",
            SourceType = "SUPPLIER_RETURN",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Details = new List<PurchaseOrderDetail>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, UnitPrice = 10000m }
            }
        };
        _repo.PurchaseOrders.Add(exchangePo);

        var existingDraftReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-EX-DRAFT",
            PurchaseOrderId = exchangePo.Id,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "DRAFT"
        };
        _repo.Receipts.Add(existingDraftReceipt);

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = exchangePo.Id,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 10, AcceptedQty = 10 }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("PO_ALREADY_HAS_RECEIPT", ex.ErrorCode);
        Assert.Contains("PO đổi hàng", ex.Message);
    }

    [Fact]
    public async Task CreateGoodsReceiptAsync_AcceptedExceedsOrderedDirectly_ThrowsAcceptedExceedsOrdered()
    {
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "PO-DIRECT-ACCEPT",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Details = new List<PurchaseOrderDetail>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, UnitPrice = 10000m }
            }
        };
        _repo.PurchaseOrders.Add(po);

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = po.Id,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 10, AcceptedQty = 12 }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("ACCEPTED_EXCEEDS_ORDERED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateGoodsReceiptAsync_SumAcceptedLessThanOrdered_WithGroupRejectionReason_NormalizesRejectedQty()
    {
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "PO-NORMALIZE-REJECT",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Details = new List<PurchaseOrderDetail>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, UnitPrice = 10000m }
            }
        };
        _repo.PurchaseOrders.Add(po);

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = po.Id,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 8, AcceptedQty = 8, RejectedQty = 0, RejectionReason = "DAMAGED" }
            }
        };

        var result = await _service.CreateGoodsReceiptAsync(cmd);
        var createdReceipt = _repo.Receipts.First(r => r.Id == result.Id);
        Assert.Equal(2, createdReceipt.Lines.First().RejectedQty);
        Assert.Equal(10, createdReceipt.Lines.First().DeliveredQty);
        Assert.Equal("DAMAGED", createdReceipt.Lines.First().RejectionReason);
    }

    [Fact]
    public async Task UpdateDraftGoodsReceipt_ExceedsOrderedAndAcceptedExceedsOrdered()
    {
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "PO-UPDATE-EXCEED",
            BranchId = _branchId,
            SupplierId = _supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Details = new List<PurchaseOrderDetail>
            {
                new() { ProductId = _milkProdId, OrderedQty = 10, UnitPrice = 10000m }
            }
        };
        _repo.PurchaseOrders.Add(po);

        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-UPDATE-EXCEED",
            PurchaseOrderId = po.Id,
            PurchaseOrder = po,
            Status = "DRAFT",
            Lines = new List<GoodsReceiptLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    OrderedQty = 10,
                    DeliveredQty = 10,
                    AcceptedQty = 10,
                    UnitPrice = 25000
                }
            }
        };
        _repo.Receipts.Add(draft);

        // 1. DeliveredQty > OrderedQty
        var exDelivered = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(draft.Id, new CreateGoodsReceiptCommand
            {
                PurchaseOrderId = po.Id,
                SupplierId = po.SupplierId,
                BranchId = po.BranchId,
                Lines = new List<CreateGoodsReceiptLineCommand>
                {
                    new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 15, AcceptedQty = 10 }
                }
            }));
        Assert.Equal("EXCEEDS_ORDERED", exDelivered.ErrorCode);

        // 2. AcceptedQty > OrderedQty
        var exAccepted = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.UpdateDraftGoodsReceiptAsync(draft.Id, new CreateGoodsReceiptCommand
            {
                PurchaseOrderId = po.Id,
                SupplierId = po.SupplierId,
                BranchId = po.BranchId,
                Lines = new List<CreateGoodsReceiptLineCommand>
                {
                    new() { ProductId = _milkProdId, OrderedQty = 10, DeliveredQty = 10, AcceptedQty = 15 }
                }
            }));
        Assert.Equal("ACCEPTED_EXCEEDS_ORDERED", exAccepted.ErrorCode);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_LineValidation_ThrowsExpectedBusinessExceptions()
    {
        var po = _repo.PurchaseOrders.First();

        GoodsReceipt CreateDraftWithLine(Action<GoodsReceiptLine> configure)
        {
            var line = new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                ProductId = _milkProdId,
                PurchaseOrderDetailId = po.Details.First().Id,
                Product = po.Details.First().Product,
                OrderedQty = 10,
                DeliveredQty = 10,
                AcceptedQty = 10,
                RejectedQty = 0,
                UnitPrice = 25000,
                BatchCode = "BATCH01",
                ExpiryDate = DateTime.UtcNow.AddYears(1)
            };
            configure(line);
            var grn = new GoodsReceipt
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                Code = $"GRN-{Guid.NewGuid():N}",
                PurchaseOrderId = po.Id,
                PurchaseOrder = po,
                SupplierId = po.SupplierId,
                BranchId = po.BranchId,
                Status = "DRAFT",
                ReceivedAtUtc = DateTime.UtcNow,
                Lines = new List<GoodsReceiptLine> { line }
            };
            _repo.Receipts.Add(grn);
            return grn;
        }

        // 1. Negative AcceptedQty
        var grn1 = CreateDraftWithLine(l => l.AcceptedQty = -1);
        var ex1 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn1.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("INVALID_QTY", ex1.ErrorCode);

        // 2. AcceptedQty > DeliveredQty
        var grn2 = CreateDraftWithLine(l => { l.DeliveredQty = 5; l.AcceptedQty = 10; });
        var ex2 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn2.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("ACCEPTED_EXCEEDS_RECEIVED", ex2.ErrorCode);

        // 3. RejectedQty > 0 without RejectionReason
        var grn3 = CreateDraftWithLine(l => { l.DeliveredQty = 10; l.AcceptedQty = 8; l.RejectedQty = 2; l.RejectionReason = null; });
        var ex3 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn3.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("REJECTION_REASON_REQUIRED", ex3.ErrorCode);

        // 4. ExpiryDate in past
        var grn4 = CreateDraftWithLine(l => l.ExpiryDate = DateTime.UtcNow.AddDays(-10));
        var ex4 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn4.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("EXPIRY_PAST", ex4.ErrorCode);

        // 5. Product HasBatches = true but BatchCode is empty
        var grn5 = CreateDraftWithLine(l => l.BatchCode = null);
        var ex5 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn5.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("BATCH_CODE_REQUIRED", ex5.ErrorCode);

        // 6. Product HasBatches = true but ExpiryDate is null
        var grn6 = CreateDraftWithLine(l => l.ExpiryDate = null);
        var ex6 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn6.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("EXPIRY_DATE_REQUIRED", ex6.ErrorCode);

        // 7. Product HasBatches = false with ManufactureDate but no ExpiryDate
        var nonBatchProd = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            ProductCode = "NO-BATCH",
            Name = "Khăn giấy",
            HasBatches = false
        };
        var grn7 = CreateDraftWithLine(l =>
        {
            l.ProductId = nonBatchProd.Id;
            l.Product = nonBatchProd;
            l.BatchCode = null;
            l.ManufactureDate = DateTime.UtcNow.AddDays(-3);
            l.ExpiryDate = null;
        });
        var ex7 = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(grn7.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("BATCH_DATES_REQUIRED", ex7.ErrorCode);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_ExchangePoOverReceipt_ThrowsExchangeExceedsOrdered()
    {
        var exchangePoId = Guid.NewGuid();
        var exchangePodId = Guid.NewGuid();
        var exchangePo = new PurchaseOrder
        {
            Id = exchangePoId,
            TenantId = "tenant-test-1",
            Code = "PO-EX-OVER",
            SourceType = "SUPPLIER_RETURN",
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.AddDays(-1),
            Details = new List<PurchaseOrderDetail>
            {
                new()
                {
                    Id = exchangePodId,
                    ProductId = _milkProdId,
                    Product = _repo.PurchaseOrders.First().Details.First().Product,
                    OrderedQty = 10,
                    ReceivedQty = 9,
                    UnitPrice = 25000m
                }
            }
        };
        _repo.PurchaseOrders.Add(exchangePo);

        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-EX-OVER",
            PurchaseOrderId = exchangePoId,
            PurchaseOrder = exchangePo,
            SupplierId = _supplierId,
            BranchId = _branchId,
            Status = "DRAFT",
            ReceivedAtUtc = DateTime.UtcNow,
            Lines = new List<GoodsReceiptLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    Product = exchangePo.Details.First().Product,
                    PurchaseOrderDetailId = exchangePodId,
                    OrderedQty = 10,
                    DeliveredQty = 2,
                    AcceptedQty = 2,
                    RejectedQty = 0,
                    UnitPrice = 25000,
                    BatchCode = "LOT-EX-01",
                    ExpiryDate = DateTime.UtcNow.AddYears(1)
                }
            }
        };
        _repo.Receipts.Add(draft);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand()));
        Assert.Equal("EXCHANGE_EXCEEDS_ORDERED", ex.ErrorCode);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WithExistingStock_UpdatesQuantityAndAveragesLandedCost()
    {
        var existingStock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = _milkProdId,
            BatchCode = "LOT-2026A",
            QuantityOnHand = 20,
            QuantityReserved = 0,
            LandedCostPerUnit = 20000,
            ExpiryDate = DateTime.UtcNow.AddMonths(6)
        };
        _repo.Stocks.Add(existingStock);

        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-20261116-001",
            BranchId = _branchId,
            SupplierId = _supplierId,
            PurchaseOrderId = _poId,
            Status = "DRAFT",
            Lines = new List<GoodsReceiptLine>
            {
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _milkProdId,
                    Product = _repo.PurchaseOrders.SelectMany(p => p.Details).First(d => d.ProductId == _milkProdId).Product,
                    PurchaseOrderDetailId = _repo.PurchaseOrders.SelectMany(p => p.Details).First(d => d.ProductId == _milkProdId).Id,
                    OrderedQty = 10,
                    DeliveredQty = 10,
                    AcceptedQty = 10,
                    RejectedQty = 0,
                    UnitPrice = 26000,
                    LandedUnitCost = 26000,
                    BatchCode = "LOT-2026A",
                    ExpiryDate = DateTime.UtcNow.AddMonths(12)
                }
            }
        };
        _repo.Receipts.Add(draft);

        var result = await _service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand());

        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(30, existingStock.QuantityOnHand);
        Assert.Equal(existingStock.Id, draft.Lines.First().BranchStockId);
        Assert.True(existingStock.LandedCostPerUnit > 20000 && existingStock.LandedCostPerUnit < 26000);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WithExistingStock_NonBatchedProduct_ClearsBatchDates()
    {
        var nonBatchedProduct = new Product
        {
            Id = _waterProdId,
            TenantId = "tenant-test-1",
            ProductCode = "SKU-WATER",
            Name = "Nước khoáng 500ml",
            BaseUnit = "Chai",
            HasBatches = false
        };
        var poDetailWater = new PurchaseOrderDetail
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = _poId,
            ProductId = _waterProdId,
            OrderedQty = 50,
            UnitPrice = 5000,
            LineAmount = 250000,
            Product = nonBatchedProduct
        };
        _repo.PurchaseOrders[0].Details.Add(poDetailWater);

        var existingStock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            BranchId = _branchId,
            ProductId = _waterProdId,
            BatchCode = null,
            ManufactureDate = DateTime.UtcNow.AddYears(-1),
            ExpiryDate = DateTime.UtcNow.AddMonths(1),
            QuantityOnHand = 15,
            LandedCostPerUnit = 5000
        };
        _repo.Stocks.Add(existingStock);

        var draft = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-20261116-002",
            BranchId = _branchId,
            SupplierId = _supplierId,
            PurchaseOrderId = _poId,
            Status = "DRAFT",
            Lines = new List<GoodsReceiptLine>
            {
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-test-1",
                    ProductId = _waterProdId,
                    Product = nonBatchedProduct,
                    PurchaseOrderDetailId = poDetailWater.Id,
                    OrderedQty = 5,
                    DeliveredQty = 5,
                    AcceptedQty = 5,
                    RejectedQty = 0,
                    UnitPrice = 5000,
                    LandedUnitCost = 5000
                }
            }
        };
        _repo.Receipts.Add(draft);

        var result = await _service.CompleteGoodsReceiptAsync(draft.Id, new CompleteGoodsReceiptCommand());

        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(20, existingStock.QuantityOnHand);
        Assert.Null(existingStock.BatchCode);
        Assert.Null(existingStock.ManufactureDate);
        Assert.Null(existingStock.ExpiryDate);
    }

    [Fact]
    public async Task ResolveActorUserIdAsync_StaffWithNoValidCandidate_ThrowsBusinessException()
    {
        _tenantContext.CurrentUserId = Guid.Empty;
        _tenantContext.CurrentRole = "EMPLOYEE";
        _repo.UserExistsPredicate = _ => false;

        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = _poId,
            Lines = new List<CreateGoodsReceiptLineCommand>()
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("ACTOR_USER_MISSING", ex.ErrorCode);

        // Reset
        _tenantContext.CurrentUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _tenantContext.CurrentRole = "STORE_OWNER";
        _repo.UserExistsPredicate = null;
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_StatusValidation_HandlesCorrectly()
    {
        var completedReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-20261116-CMP",
            BranchId = _branchId,
            SupplierId = _supplierId,
            PurchaseOrderId = _poId,
            Status = "COMPLETED"
        };
        _repo.Receipts.Add(completedReceipt);

        var exCmp = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelGoodsReceiptAsync(completedReceipt.Id, new CancelGoodsReceiptCommand { Reason = "Cancel test" }));
        Assert.Equal("CANNOT_CANCEL_COMPLETED", exCmp.ErrorCode);

        var cancelledReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-20261116-CAN",
            BranchId = _branchId,
            SupplierId = _supplierId,
            PurchaseOrderId = _poId,
            Status = "CANCELLED"
        };
        _repo.Receipts.Add(cancelledReceipt);

        var exCan = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelGoodsReceiptAsync(cancelledReceipt.Id, new CancelGoodsReceiptCommand { Reason = "Cancel test" }));
        Assert.Equal("ALREADY_CANCELLED", exCan.ErrorCode);

        var draftReceipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            Code = "GRN-20261116-DRF",
            BranchId = _branchId,
            SupplierId = _supplierId,
            PurchaseOrderId = _poId,
            Status = "DRAFT"
        };
        _repo.Receipts.Add(draftReceipt);

        var result = await _service.CancelGoodsReceiptAsync(draftReceipt.Id, new CancelGoodsReceiptCommand { Reason = "Huỷ nháp" });
        Assert.Equal("CANCELLED", result.Status);
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

public class FakeGoodsReceiptRepository : IGoodsReceiptRepository
{
    public List<GoodsReceipt> Receipts { get; } = new();
    public List<PurchaseOrder> PurchaseOrders { get; } = new();
    public List<BranchStock> Stocks { get; } = new();
    public List<Batch> Batches { get; } = new();
    public List<SupplierAPLedger> Ledgers { get; } = new();

    public Func<string, bool>? UserExistsPredicate { get; set; }
    public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserExistsPredicate != null ? UserExistsPredicate(userId) : true);

    public Task<PagedResult<GoodsReceiptDto>> GetPagedAsync(string tenantId, GoodsReceiptFilterQuery query, CancellationToken cancellationToken = default)
    {
        var dtos = Receipts.Where(r => r.TenantId == tenantId && !r.IsDeleted).Select(r => new GoodsReceiptDto
        {
            Id = r.Id,
            Code = r.Code,
            Status = r.Status,
            TotalAcceptedQty = r.Lines.Sum(l => l.AcceptedQty),
            TotalRejectedQty = r.Lines.Sum(l => l.RejectedQty),
            TotalOrderedQty = r.Lines.Sum(l => l.OrderedQty),
            HasMismatch = r.Lines.Any(l => l.AcceptedQty != l.OrderedQty || l.RejectedQty > 0)
        }).ToList();

        return Task.FromResult(new PagedResult<GoodsReceiptDto>
        {
            Items = dtos,
            TotalItems = dtos.Count,
            Page = 1,
            PageSize = 20
        });
    }

    public Task<GoodsReceipt?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Receipts.FirstOrDefault(r => r.TenantId == tenantId && r.Id == id && !r.IsDeleted));
    }

    public Task<GoodsReceipt> AddAsync(GoodsReceipt goodsReceipt, CancellationToken cancellationToken = default)
    {
        Receipts.Add(goodsReceipt);
        return Task.FromResult(goodsReceipt);
    }

    public Task UpdateAsync(GoodsReceipt goodsReceipt, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string> GenerateCodeAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"GRN-20261030-{Receipts.Count + 1:D3}");
    }

    public Task<List<PurchaseOrder>> GetEligiblePurchaseOrdersAsync(string tenantId, Guid? branchId, CancellationToken cancellationToken = default)
    {
        var activePoIds = Receipts.Where(r => r.Status == "DRAFT" || r.Status == "COMPLETED").Select(r => r.PurchaseOrderId).ToList();
        return Task.FromResult(PurchaseOrders.Where(p => !activePoIds.Contains(p.Id)).ToList());
    }

    public Task<PurchaseOrder?> GetPurchaseOrderWithDetailsAsync(string tenantId, Guid poId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PurchaseOrders.FirstOrDefault(p => p.Id == poId));
    }

    public Task<PurchaseOrder?> GetPurchaseOrderByCodeWithDetailsAsync(string tenantId, string poCode, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PurchaseOrders.FirstOrDefault(p => string.Equals(p.Code, poCode, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<GoodsReceipt?> GetActiveReceiptByPoIdAsync(string tenantId, Guid poId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Receipts.FirstOrDefault(r => r.PurchaseOrderId == poId && (r.Status == "DRAFT" || r.Status == "COMPLETED")));
    }

    public Task<BranchStock?> GetBranchStockAsync(string tenantId, Guid branchId, Guid productId, string? batchCode, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Stocks.FirstOrDefault(s => s.TenantId == tenantId && s.BranchId == branchId && s.ProductId == productId && s.BatchCode == batchCode));
    }

    public Task AddBranchStockAsync(BranchStock stock, CancellationToken cancellationToken = default)
    {
        Stocks.Add(stock);
        return Task.CompletedTask;
    }

    public Task AddBatchAsync(Batch batch, CancellationToken cancellationToken = default)
    {
        Batches.Add(batch);
        return Task.CompletedTask;
    }

    public Task<Product?> GetProductByIdAsync(string tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        var prod = PurchaseOrders.SelectMany(p => p.Details).Select(d => d.Product).FirstOrDefault(p => p != null && p.Id == productId);
        return Task.FromResult(prod);
    }

    public Task<decimal> GetTotalProductStockAsync(string tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        var sum = Stocks.Where(s => s.TenantId == tenantId && s.ProductId == productId).Sum(s => s.QuantityOnHand);
        return Task.FromResult(sum);
    }

    public Task<Supplier?> GetSupplierByIdAsync(string tenantId, Guid supplierId, CancellationToken cancellationToken = default)
    {
        var sup = PurchaseOrders.Select(p => p.Supplier).FirstOrDefault(s => s != null && s.Id == supplierId);
        return Task.FromResult(sup);
    }

    
    public Task<IReadOnlyDictionary<Guid, Product>> GetProductsByIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var dict = (IReadOnlyDictionary<Guid, Product>)PurchaseOrders
            .SelectMany(p => p.Details)
            .Select(d => d.Product)
            .Where(p => p != null && productIds.Contains(p.Id))
            .DistinctBy(p => p!.Id)
            .ToDictionary(p => p!.Id, p => p!);
        return Task.FromResult(dict);
    }
    public Task AddSupplierLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default)
    {
        Ledgers.Add(ledger);
        return Task.CompletedTask;
    }
}
