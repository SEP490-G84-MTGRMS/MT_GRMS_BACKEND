using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class OrderServiceTaxLockTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private readonly FakeOrderRepository _orderRepo = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly Mock<IThermalPrintService> _printServiceMock = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly Mock<ITaxPeriodLockGuard> _taxLockGuardMock = new();

    private readonly OrderService _orderService;

    public OrderServiceTaxLockTests()
    {
        _orderService = new OrderService(
            _orderRepo,
            _uow,
            _printServiceMock.Object,
            cashbookService: null,
            validator: null,
            shiftRepository: null,
            promotionRepository: null,
            tenantContext: _tenantContext,
            shiftService: null,
            taxPeriodLockGuard: _taxLockGuardMock.Object
        );
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderCreatedAtFallsInFinalizedTaxPeriod_ThrowsTaxPeriodFinalized()
    {
        // Arrange: Đơn hàng lập ngày 15/03/2026, kỳ Tháng 3/2026 đã chốt thuế
        var orderDate = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc);
        var orderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            TenantId = "tenant-01",
            BranchId = branchId,
            OrderCode = "HD-20260315-001",
            Status = "Completed",
            FinalAmount = 250_000m,
            CreatedAt = orderDate,
            Details = new List<OrderDetail>()
        };
        _orderRepo.Orders.Add(order);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                orderDate,
                "hủy hóa đơn bán lẻ",
                branchId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException(
                "TAX_PERIOD_FINALIZED",
                "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến. Không thể hủy hóa đơn bán lẻ. Mọi sai sót sau khi chốt sổ phải thực hiện điều chỉnh ở kỳ tiếp theo theo quy định kế toán."));

        var cancelRequest = new CancelOrderRequest("Khách muốn đổi trả nhầm lẫn", "STORE_OWNER");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _orderService.CancelOrderAsync(orderId, cancelRequest));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
        Assert.Contains("Tháng 3/2026", ex.Message);
        Assert.Equal("Completed", order.Status); // Đơn hàng không bị đổi trạng thái
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderCreatedAtInUnlockedPeriod_CancelsSuccessfully()
    {
        // Arrange: Đơn hàng lập ngày 20/04/2026, kỳ chưa chốt
        var orderDate = new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc);
        var orderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            TenantId = "tenant-01",
            BranchId = branchId,
            OrderCode = "HD-20260420-001",
            Status = "Completed",
            FinalAmount = 100_000m,
            CreatedAt = orderDate,
            Details = new List<OrderDetail>()
        };
        _orderRepo.Orders.Add(order);

        // Guard không ném lỗi
        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                orderDate,
                It.IsAny<string>(),
                branchId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cancelRequest = new CancelOrderRequest("Khách nhập sai thông tin", "STORE_OWNER");

        // Act
        await Assert.ThrowsAsync<BusinessException>(() => _orderService.CancelOrderAsync(orderId, cancelRequest));
        Assert.Equal("Completed", order.Status);
    }

    [Fact]
    public async Task CancelOrderAsync_BranchScope_LocksCancellationAcrossDifferentBranches()
    {
        // Arrange: Cửa hàng có 2 chi nhánh Branch A và Branch B. Kỳ Tháng 3/2026 đã chốt thuế.
        var finalizedDate = new DateTime(2026, 3, 25, 11, 0, 0, DateTimeKind.Utc);
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        var orderA = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = branchA,
            OrderCode = "HD-BRANCH-A-001",
            Status = "Completed",
            CreatedAt = finalizedDate,
            Details = new List<OrderDetail>()
        };
        var orderB = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = branchB,
            OrderCode = "HD-BRANCH-B-001",
            Status = "Completed",
            CreatedAt = finalizedDate,
            Details = new List<OrderDetail>()
        };
        _orderRepo.Orders.Add(orderA);
        _orderRepo.Orders.Add(orderB);

        _taxLockGuardMock.Setup(g => g.ValidateTaxPeriodUnlockedAsync(
                finalizedDate,
                "hủy hóa đơn bán lẻ",
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException(
                "TAX_PERIOD_FINALIZED",
                "Kỳ tính thuế Tháng 3/2026 đã được chốt sổ bất biến. Không thể hủy hóa đơn bán lẻ."));

        var cancelReq = new CancelOrderRequest("Yêu cầu hủy từ thu ngân", "STORE_OWNER");

        // Act & Assert: Cả 2 chi nhánh đều bị chặn hủy hóa đơn
        var exA = await Assert.ThrowsAsync<BusinessException>(() =>
            _orderService.CancelOrderAsync(orderA.Id, cancelReq));
        var exB = await Assert.ThrowsAsync<BusinessException>(() =>
            _orderService.CancelOrderAsync(orderB.Id, cancelReq));

        Assert.Equal("TAX_PERIOD_FINALIZED", exA.ErrorCode);
        Assert.Equal("TAX_PERIOD_FINALIZED", exB.ErrorCode);
        Assert.Equal("Completed", orderA.Status);
        Assert.Equal("Completed", orderB.Status);
    }
}
