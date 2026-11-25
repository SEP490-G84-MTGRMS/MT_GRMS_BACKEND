using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class TenantServiceExtendedTests
{
    private readonly Mock<ITenantRepository> _mockRepo;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly TenantService _service;

    public TenantServiceExtendedTests()
    {
        _mockRepo = new Mock<ITenantRepository>();
        _mockUow = new Mock<IUnitOfWork>();
        _service = new TenantService(_mockRepo.Object, _mockUow.Object);
    }

    private static Tenant CreateSampleTenant(string id)
    {
        return new Tenant
        {
            Id = id,
            Code = "TNT-01",
            Subdomain = "tnt01",
            Name = "Shop 1",
            Status = "ACTIVE",
            CurrentPlan = "PRO",
            SubscriptionStatus = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1),
            WalletBalance = 1000000m,
            CreatedAt = DateTime.UtcNow,
            Branches = new List<Branch>(),
            TenantUsers = new List<TenantUser>()
        };
    }

    [Fact]
    public async Task GetByIdAsync_WhenFound_ShouldReturnDetails()
    {
        var tenant = CreateSampleTenant("t-01");
        _mockRepo.Setup(r => r.GetByIdWithDetailsAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await _service.GetByIdAsync("t-01");
        Assert.NotNull(result);
        Assert.Equal("Shop 1", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldThrowKeyNotFoundException()
    {
        _mockRepo.Setup(r => r.GetByIdWithDetailsAsync("t-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByIdAsync("t-missing"));
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenFound_ShouldUpdateAndReturn()
    {
        var tenant = CreateSampleTenant("t-01");
        _mockRepo.Setup(r => r.GetByIdAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _mockRepo.Setup(r => r.GetByIdWithDetailsAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var request = new UpdateTenantRequest(
            Name: "Updated Shop",
            TaxCode: "0101234567",
            Hotline: "0901234567",
            Address: "456 Le Loi",
            City: "HCM"
        );

        var result = await _service.UpdateTenantAsync("t-01", request);
        Assert.NotNull(result);
        _mockRepo.Verify(r => r.Update(It.Is<Tenant>(t => t.Name == "Updated Shop")), Times.Once);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTenantAsync_WhenNotFound_ShouldThrow()
    {
        _mockRepo.Setup(r => r.GetByIdAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var request = new UpdateTenantRequest("Name", null, null, null, null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateTenantAsync("t-01", request));
    }

    [Fact]
    public async Task LockTenantAsync_ValidLock_ShouldSetLocked()
    {
        var tenant = CreateSampleTenant("t-01");
        _mockRepo.Setup(r => r.GetByIdAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var request = new LockTenantRequest("SOFT", "Late payment");
        await _service.LockTenantAsync("t-01", request, "admin");

        Assert.Equal("SOFT", tenant.LockType);
        Assert.Equal("Late payment", tenant.LockReason);
        Assert.Equal("LOCKED", tenant.Status);
        _mockRepo.Verify(r => r.Update(tenant), Times.Once);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LockTenantAsync_InvalidLockType_ShouldThrowArgumentException()
    {
        var tenant = CreateSampleTenant("t-01");
        _mockRepo.Setup(r => r.GetByIdAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var request = new LockTenantRequest("INVALID_TYPE", "Reason");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.LockTenantAsync("t-01", request, "admin"));
    }

    [Fact]
    public async Task UnlockTenantAsync_ShouldResetLockAndRestoreStatus()
    {
        var tenant = CreateSampleTenant("t-01");
        tenant.LockType = "HARD";
        tenant.LockReason = "Violation";
        tenant.Status = "LOCKED";

        _mockRepo.Setup(r => r.GetByIdAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        await _service.UnlockTenantAsync("t-01");

        Assert.Null(tenant.LockType);
        Assert.Null(tenant.LockReason);
        Assert.Equal("ACTIVE", tenant.Status);
        _mockRepo.Verify(r => r.Update(tenant), Times.Once);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtendGracePeriodAsync_ValidDays_ShouldExtend()
    {
        var tenant = CreateSampleTenant("t-01");
        tenant.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-1);
        _mockRepo.Setup(r => r.GetByIdAsync("t-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var request = new ExtendGraceRequest(7, "Customer requested extension");
        await _service.ExtendGracePeriodAsync("t-01", request);

        Assert.NotNull(tenant.GracePeriodExpiresAt);
        Assert.True(tenant.GracePeriodExpiresAt > DateTime.UtcNow);
        _mockRepo.Verify(r => r.Update(tenant), Times.Once);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtendGracePeriodAsync_InvalidDays_ShouldThrowArgumentException()
    {
        var request = new ExtendGraceRequest(0, "Reason");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtendGracePeriodAsync("t-01", request));
    }
}
