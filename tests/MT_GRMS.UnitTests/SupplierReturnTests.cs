using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
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
            AttachmentUrl: null,
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
    public async Task SubmitAsync_ShouldChangeStatusToPendingSupplier()
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
            BranchId = _branchId
        };
        _repo.Returns.Add(ret);

        // Act
        var result = await _service.SubmitAsync(ret.Id, "Đã gửi thông báo cho NCC");

        // Assert
        Assert.Equal("PENDING_SUPPLIER", result.Status);
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
