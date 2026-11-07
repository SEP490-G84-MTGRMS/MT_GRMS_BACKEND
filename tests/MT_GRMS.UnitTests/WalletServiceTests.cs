using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class WalletServiceTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-001";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_WalletTestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options, new FakeTenantContext());
    }

    private IOptions<SePaySettings> CreateDefaultSettings()
    {
        return Options.Create(new SePaySettings
        {
            ApiKey = "TEST_API_KEY",
            BankCode = "MBBank",
            BankName = "MB Bank (Ngân hàng Quân Đội)",
            AccountNumber = "190065228888",
            AccountName = "CONG TY TNHH PHAN MEM MT-GRMS"
        });
    }

    [Fact]
    public async Task CreateTopupOrder_ValidAmount_ReturnsOrderResponseAndPersistsPendingOrder()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-001",
            Name = "Chuỗi Tạp Hóa Minh Đức",
            Code = "MD-RETAIL-01",
            Subdomain = "minhduc",
            WalletBalance = 500000
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var service = new WalletService(context, CreateDefaultSettings());

        // Act
        var result = await service.CreateTopupOrderAsync("tenant-001", new CreateTopupOrderRequest(1000000));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1000000, result.Amount);
        Assert.Contains("WAL", result.TransferContent);
        Assert.Contains("https://qr.sepay.vn/img", result.QrUrl);

        var savedOrder = await context.WalletTopupOrders.FirstOrDefaultAsync(o => o.OrderCode == result.OrderCode);
        Assert.NotNull(savedOrder);
        Assert.Equal("PENDING", savedOrder.Status);
        Assert.Equal("tenant-001", savedOrder.TenantId);
    }

    [Fact]
    public async Task CreateTopupOrder_AmountLessThan50k_ThrowsArgumentException()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new WalletService(context, CreateDefaultSettings());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateTopupOrderAsync("tenant-001", new CreateTopupOrderRequest(30000)));
    }

    [Fact]
    public async Task ProcessSepayWebhook_ValidPayload_CreditsWalletBalanceAndSavesTransaction()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-001",
            Code = "MD-RETAIL-01",
            Subdomain = "minhduc",
            WalletBalance = 200000
        };
        context.Tenants.Add(tenant);

        var order = new WalletTopupOrder
        {
            TenantId = "tenant-001",
            OrderCode = "839201",
            Amount = 1000000,
            TransferContent = "WAL 839201",
            BankCode = "MBBank",
            BankName = "MB Bank",
            AccountNumber = "190065228888",
            AccountName = "MT-GRMS",
            QrUrl = "https://qr.sepay.vn/img?...",
            Status = "PENDING",
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
        context.WalletTopupOrders.Add(order);
        await context.SaveChangesAsync();

        var service = new WalletService(context, CreateDefaultSettings());

        var webhookPayload = new SepayWebhookPayload
        {
            Id = 998811,
            Gateway = "MBBank",
            TransferType = "in",
            TransferAmount = 1000000,
            Content = "WAL 839201 chuyen tien",
            ReferenceCode = "FT2401019912",
            AccountNumber = "190065228888"
        };

        // Act
        var result = await service.ProcessSepayWebhookAsync(webhookPayload);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("tenant-001", result.TenantId);

        var updatedTenant = await context.Tenants.FindAsync("tenant-001");
        Assert.Equal(1200000, updatedTenant!.WalletBalance);

        var updatedOrder = await context.WalletTopupOrders.FirstOrDefaultAsync(o => o.OrderCode == "839201");
        Assert.Equal("COMPLETED", updatedOrder!.Status);

        var tx = await context.WalletTransactions.FirstOrDefaultAsync(t => t.ReferenceCode == "FT2401019912");
        Assert.NotNull(tx);
        Assert.Equal("TOPUP", tx.Type);
        Assert.Equal(1000000, tx.Amount);
        Assert.Equal(200000, tx.BalanceBefore);
        Assert.Equal(1200000, tx.BalanceAfter);
    }

    [Fact]
    public async Task ProcessSepayWebhook_DuplicateReferenceCode_ReturnsSuccessWithoutDoubleCredit()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-001",
            Code = "MD-RETAIL-01",
            WalletBalance = 1000000
        };
        context.Tenants.Add(tenant);

        var existingTx = new WalletTransaction
        {
            TenantId = "tenant-001",
            TransactionCode = "WAL-20261104-998811",
            Type = "TOPUP",
            Amount = 1000000,
            BalanceBefore = 0,
            BalanceAfter = 1000000,
            Description = "Nạp tiền SePay",
            PaymentMethod = "VietQR",
            Status = "SUCCESS",
            ReferenceCode = "FT_EXISTING_123"
        };
        context.WalletTransactions.Add(existingTx);
        await context.SaveChangesAsync();

        var service = new WalletService(context, CreateDefaultSettings());

        var duplicateWebhook = new SepayWebhookPayload
        {
            Id = 998811,
            TransferType = "in",
            TransferAmount = 1000000,
            Content = "WAL 839201",
            ReferenceCode = "FT_EXISTING_123"
        };

        // Act
        var result = await service.ProcessSepayWebhookAsync(duplicateWebhook);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Idempotent", result.Message);

        var updatedTenant = await context.Tenants.FindAsync("tenant-001");
        Assert.Equal(1000000, updatedTenant!.WalletBalance); // Không bị cộng thành 2.000.000
    }

    [Fact]
    public async Task ProcessSepayWebhook_OutgoingTransfer_IgnoredGracefully()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new WalletService(context, CreateDefaultSettings());

        var outgoingPayload = new SepayWebhookPayload
        {
            Id = 112233,
            TransferType = "out",
            TransferAmount = 500000,
            Content = "Chi tiền"
        };

        // Act
        var result = await service.ProcessSepayWebhookAsync(outgoingPayload);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("tiền ra", result.Message);
    }

    [Fact]
    public async Task DeductWallet_SufficientBalance_DeductsBalanceAndSavesPurchaseTransaction()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-001",
            WalletBalance = 5000000
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var service = new WalletService(context, CreateDefaultSettings());

        // Act
        await service.DeductWalletAsync("tenant-001", 4356000, "Thanh toán gói Chuyên Nghiệp", "SUB-001");

        // Assert
        var updatedTenant = await context.Tenants.FindAsync("tenant-001");
        Assert.Equal(644000, updatedTenant!.WalletBalance);

        var tx = await context.WalletTransactions.FirstOrDefaultAsync(t => t.ReferenceCode == "SUB-001");
        Assert.NotNull(tx);
        Assert.Equal("PURCHASE_PLAN", tx.Type);
        Assert.Equal(4356000, tx.Amount);
        Assert.Equal(5000000, tx.BalanceBefore);
        Assert.Equal(644000, tx.BalanceAfter);
    }

    [Fact]
    public async Task DeductWallet_InsufficientBalance_ThrowsInvalidOperationException()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-001",
            WalletBalance = 1000000
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var service = new WalletService(context, CreateDefaultSettings());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeductWalletAsync("tenant-001", 2000000, "Mua gói", "SUB-002"));

        Assert.Equal("INSUFFICIENT_WALLET_BALANCE", ex.Message);
    }
}

