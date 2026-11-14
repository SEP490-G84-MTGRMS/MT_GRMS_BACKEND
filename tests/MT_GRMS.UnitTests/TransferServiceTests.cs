using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

public class TransferServiceTests
{
    private readonly FakeRepository<TransferDetail> _detailsRepo = new();
    private readonly FakeRepository<BranchStock> _stocksRepo = new();
    private readonly FakeRepository<Product> _productsRepo = new();
    private readonly FakeRepository<Branch> _branchesRepo = new();
    private readonly FakeRepository<Transfer> _transfersRepo = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly Mock<IUnitOfWork> _mockUow = new();
    private readonly TransferService _service;

    private readonly Guid _fromBranchId = Guid.NewGuid();
    private readonly Guid _toBranchId = Guid.NewGuid();
    private readonly Guid _unrelatedBranchId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public TransferServiceTests()
    {
        _mockUow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockUow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockUow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockUow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _service = new TransferService(
            _transfersRepo,
            _detailsRepo,
            _stocksRepo,
            _productsRepo,
            _branchesRepo,
            _tenantContext,
            _mockUow.Object);

        // Seed default branches
        _branchesRepo.Items.Add(new Branch
        {
            Id = _fromBranchId,
            TenantId = _tenantContext.CurrentTenantId,
            Code = "BR-FROM",
            Name = "Chi nhánh Kho Tổng",
            IsActive = true
        });

        _branchesRepo.Items.Add(new Branch
        {
            Id = _toBranchId,
            TenantId = _tenantContext.CurrentTenantId,
            Code = "BR-TO",
            Name = "Chi nhánh Quận 1",
            IsActive = true
        });

        _branchesRepo.Items.Add(new Branch
        {
            Id = _unrelatedBranchId,
            TenantId = _tenantContext.CurrentTenantId,
            Code = "BR-OTHER",
            Name = "Chi nhánh Bình Thạnh",
            IsActive = true
        });

        // Seed product
        _productsRepo.Items.Add(new Product
        {
            Id = _productId,
            TenantId = _tenantContext.CurrentTenantId,
            ProductCode = "SP001",
            Name = "Sữa Tươi 1L",
            BaseUnit = "Hộp"
        });

        // Seed stock at from-branch
        _stocksRepo.Items.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = _fromBranchId,
            ProductId = _productId,
            BatchCode = "BATCH-01",
            ExpiryDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            QuantityOnHand = 100m,
            QuantityReserved = 10m,
            QuantityQuarantined = 5m,
            LandedCostPerUnit = 25000m
        });
    }

    [Fact]
    public async Task CreateTransferAsync_Draft_Success()
    {
        // Arrange
        var request = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: DateTime.UtcNow.AddDays(2),
            Note: "Chuyển hàng định kỳ",
            Items: new List<TransferLineRequest>
            {
                new(_productId, 20m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act
        var result = await _service.CreateTransferAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Draft", result.Status);
        Assert.Equal(500000m, result.TotalAmount);
        Assert.Single(result.Items);
        Assert.Equal(20m, result.Items[0].SentQuantity);

        // Verify stock at from-branch was NOT deducted for Draft
        var stock = _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId);
        Assert.Equal(100m, stock.QuantityOnHand);

        _mockUow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTransfersAsync_StatusesAndReceiveStatus_FiltersBeforePaging()
    {
        var matchingLine = new TransferDetail { Id = Guid.NewGuid(), ProductId = _productId, SentQuantity = 5m, ReceivedQuantity = 5m, DamagedQuantity = 0m };
        var mismatchedLine = new TransferDetail { Id = Guid.NewGuid(), ProductId = _productId, SentQuantity = 5m, ReceivedQuantity = 4m, DamagedQuantity = 0m };
        var matching = new Transfer
        {
            TenantId = _tenantContext.CurrentTenantId,
            FromBranchId = _fromBranchId,
            ToBranchId = _toBranchId,
            Status = TransferStatus.Received,
            CreatedAt = DateTime.UtcNow,
            Details = new List<TransferDetail>
            {
                matchingLine
            }
        };
        var mismatched = new Transfer
        {
            TenantId = _tenantContext.CurrentTenantId,
            FromBranchId = _fromBranchId,
            ToBranchId = _toBranchId,
            Status = TransferStatus.Received,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            Details = new List<TransferDetail>
            {
                mismatchedLine
            }
        };
        _transfersRepo.Items.AddRange([matching, mismatched]);

        var result = await _service.GetTransfersAsync(
            new TransferFilterParams(null, null, null, null, null, null, 1, 10, [TransferStatus.Received], null, "match"),
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(matching.Id, result.Items[0].Id);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task CreateTransferAsync_DispatchNow_SufficientStock_DeductsFromBranchStock()
    {
        // Arrange
        var request = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: DateTime.UtcNow.AddDays(1),
            Note: "Xuất chuyển ngay",
            Items: new List<TransferLineRequest>
            {
                new(_productId, 30m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act
        var result = await _service.CreateTransferAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("InTransit", result.Status);
        Assert.NotNull(result.DispatchedAt);

        // Available was 100 - 10 - 5 = 85. Stock after dispatch: 100 - 30 = 70
        var stock = _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId);
        Assert.Equal(70m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task CreateTransferAsync_SameBranch_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = new CreateTransferRequest(
            _fromBranchId,
            _fromBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: "Lỗi trùng chi nhánh",
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateTransferAsync(request, CancellationToken.None));
        Assert.Equal("Chi nhánh gửi và nhận phải khác nhau.", ex.Message);
    }

    [Fact]
    public async Task CreateTransferAsync_CrossTenantBranch_ThrowsInvalidOperationException()
    {
        // Arrange: ToBranch belongs to different tenant
        _branchesRepo.Items.Add(new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = "different-tenant",
            Code = "BR-DIFF",
            Name = "Chi nhánh Khác Tenant",
            IsActive = true
        });
        var foreignBranchId = _branchesRepo.Items.Last().Id;

        var request = new CreateTransferRequest(
            _fromBranchId,
            foreignBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: "Lỗi cross-tenant",
            Items: new List<TransferLineRequest>
            {
                new(_productId, 5m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateTransferAsync(request, CancellationToken.None));
        Assert.Equal("Hai chi nhánh phải thuộc cùng tenant hiện tại.", ex.Message);
    }

    [Fact]
    public async Task CreateTransferAsync_CrossTenantProduct_ThrowsInvalidOperationException()
    {
        // Arrange: Product belongs to different tenant
        var foreignProductId = Guid.NewGuid();
        _productsRepo.Items.Add(new Product
        {
            Id = foreignProductId,
            TenantId = "different-tenant",
            ProductCode = "SP-DIFF",
            Name = "Sản phẩm khác tenant",
            BaseUnit = "Hộp"
        });

        var request = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: "Lỗi sản phẩm khác tenant",
            Items: new List<TransferLineRequest>
            {
                new(foreignProductId, 5m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateTransferAsync(request, CancellationToken.None));
        Assert.Equal("Sản phẩm không thuộc tenant hiện tại.", ex.Message);
    }

    [Fact]
    public async Task CreateTransferAsync_StaffFromDifferentBranch_ThrowsUnauthorizedAccessException()
    {
        // Arrange: Staff from Branch B trying to transfer out of Branch A
        _tenantContext.SetRole("STAFF");
        _tenantContext.CurrentBranchId = _toBranchId;

        var request = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 5m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CreateTransferAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateTransferAsync_DispatchNow_InsufficientStock_ThrowsInvalidOperationException()
    {
        // Arrange: Available is 100 - 10 - 5 = 85. Requesting 90.
        var request = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 90m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateTransferAsync(request, CancellationToken.None));
        Assert.Contains("Không đủ tồn khả dụng", ex.Message);
    }

    [Fact]
    public async Task DispatchTransferAsync_Draft_Success_DeductsStock()
    {
        // Arrange: Create a draft transfer
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: "Phiếu nháp chờ xuất",
            Items: new List<TransferLineRequest>
            {
                new(_productId, 25m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);

        // Act: Dispatch the draft
        var result = await _service.DispatchTransferAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.Equal("InTransit", result.Status);
        Assert.NotNull(result.DispatchedAt);

        var stock = _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId);
        Assert.Equal(75m, stock.QuantityOnHand); // 100 - 25 = 75
    }

    [Fact]
    public async Task DispatchTransferAsync_NonDraftStatus_ThrowsInvalidOperationException()
    {
        // Arrange: Transfer is already InTransit
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DispatchTransferAsync(created.Id, CancellationToken.None));
        Assert.Equal("Chỉ có thể xuất chuyển phiếu nháp.", ex.Message);
    }

    [Fact]
    public async Task DispatchTransferAsync_StaffFromWrongBranch_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);

        // Act as Staff of destination branch (not from-branch)
        _tenantContext.SetRole("STAFF");
        _tenantContext.CurrentBranchId = _toBranchId;

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.DispatchTransferAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ReceiveTransferAsync_FullMatch_UpdatesToBranchStockAndStatus()
    {
        // Arrange: InTransit transfer with 20 items
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 20m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);
        var detailId = created.Items[0].Id;

        var receiveRequest = new ReceiveTransferRequest(
            Note: "Nhận đủ 20 hộp nguyên vẹn",
            Items: new List<ReceiveTransferLineRequest>
            {
                new(detailId, ReceivedQuantity: 20m, DamagedQuantity: 0m, DiscrepancyReason: null)
            });

        // Act
        var result = await _service.ReceiveTransferAsync(created.Id, receiveRequest, CancellationToken.None);

        // Assert
        Assert.Equal("Received", result.Status);
        Assert.NotNull(result.ReceivedAt);
        Assert.Equal(20m, result.Items[0].ReceivedQuantity);
        Assert.Equal(0m, result.Items[0].DamagedQuantity);

        // Check ToBranch stock: was created with 20OnHand and 0Quarantined
        var destStock = _stocksRepo.Items.FirstOrDefault(s => s.BranchId == _toBranchId && s.ProductId == _productId);
        Assert.NotNull(destStock);
        Assert.Equal(20m, destStock.QuantityOnHand);
        Assert.Equal(0m, destStock.QuantityQuarantined);
    }

    [Fact]
    public async Task ReceiveTransferAsync_WithDamaged_AddsToQuarantineAndSetsDamagedStatus()
    {
        // Arrange: InTransit transfer with 20 items
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 20m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);
        var detailId = created.Items[0].Id;

        // Receive 16 good, 4 damaged
        var receiveRequest = new ReceiveTransferRequest(
            Note: "Có 4 hộp bị móp méo chảy sữa",
            Items: new List<ReceiveTransferLineRequest>
            {
                new(detailId, ReceivedQuantity: 16m, DamagedQuantity: 4m, DiscrepancyReason: "Móp méo khi vận chuyển")
            });

        // Act
        var result = await _service.ReceiveTransferAsync(created.Id, receiveRequest, CancellationToken.None);

        // Assert
        Assert.Equal("Received", result.Status);
        Assert.Equal(16m, result.Items[0].ReceivedQuantity);
        Assert.Equal(4m, result.Items[0].DamagedQuantity);
        Assert.Equal("Móp méo khi vận chuyển", result.Items[0].DiscrepancyReason);

        // Check destination stock: TotalOnHand = 16 + 4 = 20, Quarantined = 4 (Available = 16)
        var destStock = _stocksRepo.Items.FirstOrDefault(s => s.BranchId == _toBranchId && s.ProductId == _productId);
        Assert.NotNull(destStock);
        Assert.Equal(20m, destStock.QuantityOnHand);
        Assert.Equal(4m, destStock.QuantityQuarantined);
    }

    [Fact]
    public async Task ReceiveTransferAsync_DamageWithoutReason_ThrowsInvalidOperationException()
    {
        // Arrange
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);
        var detailId = created.Items[0].Id;

        var receiveRequest = new ReceiveTransferRequest(
            Note: null,
            Items: new List<ReceiveTransferLineRequest>
            {
                new(detailId, ReceivedQuantity: 8m, DamagedQuantity: 2m, DiscrepancyReason: "   ") // empty reason
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReceiveTransferAsync(created.Id, receiveRequest, CancellationToken.None));
        Assert.Equal("Cần nêu lý do hàng hỏng.", ex.Message);
    }

    [Fact]
    public async Task ReceiveTransferAsync_ExceedsSentQuantity_ThrowsInvalidOperationException()
    {
        // Arrange
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);
        var detailId = created.Items[0].Id;

        // Received + Damaged = 10 + 1 = 11 > 10
        var receiveRequest = new ReceiveTransferRequest(
            Note: null,
            Items: new List<ReceiveTransferLineRequest>
            {
                new(detailId, ReceivedQuantity: 10m, DamagedQuantity: 1m, DiscrepancyReason: "Thừa 1")
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReceiveTransferAsync(created.Id, receiveRequest, CancellationToken.None));
        Assert.Equal("Số nhận/hỏng không hợp lệ hoặc vượt số xuất.", ex.Message);
    }

    [Fact]
    public async Task ReceiveTransferAsync_StaffOfWrongBranch_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);
        var detailId = created.Items[0].Id;

        // User is staff of FromBranch, not ToBranch
        _tenantContext.SetRole("STAFF");
        _tenantContext.CurrentBranchId = _fromBranchId;

        var receiveRequest = new ReceiveTransferRequest(
            Note: null,
            Items: new List<ReceiveTransferLineRequest>
            {
                new(detailId, ReceivedQuantity: 10m, DamagedQuantity: 0m, DiscrepancyReason: null)
            });

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ReceiveTransferAsync(created.Id, receiveRequest, CancellationToken.None));
    }

    [Fact]
    public async Task CancelTransferAsync_Draft_StaffOfFromBranch_Success()
    {
        // Arrange
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);

        // Switch to Staff of FromBranch
        _tenantContext.SetRole("STAFF");
        _tenantContext.CurrentBranchId = _fromBranchId;

        // Act
        var result = await _service.CancelTransferAsync(created.Id, new CancelTransferRequest("Không cần chuyển nữa"), CancellationToken.None);

        // Assert
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal("Không cần chuyển nữa", result.CancelReason);

        // Stock was never deducted, remains 100
        var stock = _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId);
        Assert.Equal(100m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task CancelTransferAsync_InTransit_SendingBranchStaff_Success_RefundsStock()
    {
        // Arrange: InTransit transfer
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 10m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);

        // Sending-branch staff may cancel an in-transit transfer per FE-15 authorization rules.
        _tenantContext.SetRole("STAFF");
        _tenantContext.CurrentBranchId = _fromBranchId;

        // Act
        var result = await _service.CancelTransferAsync(created.Id, new CancelTransferRequest("Hủy giữa đường"), CancellationToken.None);

        // Assert
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(100m, _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId).QuantityOnHand);
    }

    [Fact]
    public async Task CancelTransferAsync_InTransit_Owner_Success_RefundsStock()
    {
        // Arrange: InTransit transfer created by Owner
        var createRequest = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 20m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });
        var created = await _service.CreateTransferAsync(createRequest, CancellationToken.None);

        // Stock was 100 - 20 = 80
        var stockBefore = _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId);
        Assert.Equal(80m, stockBefore.QuantityOnHand);

        // Act: Owner cancels InTransit transfer
        var result = await _service.CancelTransferAsync(created.Id, new CancelTransferRequest("Xe hỏng quay đầu kho"), CancellationToken.None);

        // Assert
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal("Xe hỏng quay đầu kho", result.CancelReason);

        // Stock is refunded back: 80 + 20 = 100
        var stockAfter = _stocksRepo.Items.First(s => s.BranchId == _fromBranchId && s.ProductId == _productId);
        Assert.Equal(100m, stockAfter.QuantityOnHand);
    }

    [Fact]
    public async Task GetTransfersAsync_StaffIsolation_OnlySeesTransfersInvolvingOwnBranch()
    {
        // Arrange: Create 3 transfers:
        // 1: FromBranch -> ToBranch
        // 2: ToBranch -> UnrelatedBranch
        // 3: UnrelatedBranch -> FromBranch
        var t1 = new Transfer { Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, TransferCode = "TRF-1", FromBranchId = _fromBranchId, ToBranchId = _toBranchId, Status = TransferStatus.Draft, CreatedAt = DateTime.UtcNow };
        var t2 = new Transfer { Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, TransferCode = "TRF-2", FromBranchId = _toBranchId, ToBranchId = _unrelatedBranchId, Status = TransferStatus.Draft, CreatedAt = DateTime.UtcNow };
        var t3 = new Transfer { Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, TransferCode = "TRF-3", FromBranchId = _unrelatedBranchId, ToBranchId = _fromBranchId, Status = TransferStatus.Draft, CreatedAt = DateTime.UtcNow };
        _transfersRepo.Items.AddRange(new[] { t1, t2, t3 });

        // User is STAFF at UnrelatedBranch
        _tenantContext.SetRole("STAFF");
        _tenantContext.CurrentBranchId = _unrelatedBranchId;

        // Act
        var result = await _service.GetTransfersAsync(new TransferFilterParams(null, null, null, null, null, null), CancellationToken.None);

        // Assert: Should only see t2 and t3 (involving UnrelatedBranch), but NOT t1
        Assert.Equal(2, result.TotalItems);
        Assert.Contains(result.Items, x => x.TransferCode == "TRF-2");
        Assert.Contains(result.Items, x => x.TransferCode == "TRF-3");
        Assert.DoesNotContain(result.Items, x => x.TransferCode == "TRF-1");
    }

    [Fact]
    public async Task GetStockBatchesAsync_CalculatesAvailableStockCorrectly()
    {
        // Arrange: BranchStock has QuantityOnHand = 100, Reserved = 10, Quarantined = 5
        // Expected available = 85

        // Act
        var result = await _service.GetStockBatchesAsync(_fromBranchId, CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.Equal("BATCH-01", result[0].BatchCode);
        Assert.Equal(85m, result[0].AvailableQuantity);
    }

    [Fact]
    public async Task TransferService_TransactionRollback_WhenExceptionOccurs()
    {
        // Arrange: Invalid request (requesting more stock than available)
        var request = new CreateTransferRequest(
            _fromBranchId,
            _toBranchId,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: null,
            Items: new List<TransferLineRequest>
            {
                new(_productId, 999m, "Hộp", "BATCH-01", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25000m)
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateTransferAsync(request, CancellationToken.None));

        // Verify transaction rolled back
        _mockUow.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Fakes

    private class FakeRepository<T> : IRepository<T> where T : BaseTenantEntity
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
        if (!Items.Any(item => item.Id == entity.Id)) Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity)
        {
            var index = Items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) Items[index] = entity;
            else Items.Add(entity);
        }

        public void Delete(T entity)
        {
            entity.IsDeleted = true;
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-001";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    #endregion
}
