using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
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
            DeliveryDate = DateTime.UtcNow.AddDays(1),
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
        Assert.Equal("ACCEPTED_EXCEEDS_ORDERED", ex.ErrorCode);
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
                    BatchCode = "LOT-100",
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
                    BatchCode = "LOT-OK",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            }
        };
        var completed = await _service.CreateGoodsReceiptAsync(command);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CancelGoodsReceiptAsync(completed.Id, new CancelGoodsReceiptCommand { Reason = "Muốn hủy" }));

        Assert.Equal("CANNOT_CANCEL_COMPLETED", ex.ErrorCode);
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
}
