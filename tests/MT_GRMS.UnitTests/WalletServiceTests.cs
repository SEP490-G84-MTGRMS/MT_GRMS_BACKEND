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

        var savedOrder = await context.WalletTransactions.SingleAsync(o => o.OrderCode == result.OrderCode);
        Assert.NotNull(savedOrder);
        Assert.Equal("PENDING", savedOrder.Status);
        Assert.Equal("tenant-001", savedOrder.TenantId);
        Assert.Equal("PENDING", (await service.GetTopupOrderStatusAsync("tenant-001", result.OrderCode))!.Status);
        Assert.Null(await service.GetTopupOrderStatusAsync("other-tenant", result.OrderCode));
        Assert.Empty(await service.GetWalletTransactionsAsync("tenant-001"));
        Assert.Equal(0, (await service.GetWalletBalanceAsync("tenant-001")).TotalTopup);
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

        var order = new WalletTransaction
        {
            TenantId = "tenant-001",
            OrderCode = "839201",
            TransactionCode = "WAL-ORDER-839201",
            Type = "TOPUP",
            Amount = 1000000,
            RequestedAmount = 1000000,
            TransferContent = "WAL 839201",
            Status = "PENDING",
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
        context.WalletTransactions.Add(order);
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

        var updatedOrder = await service.GetTopupOrderStatusAsync("tenant-001", "839201");
        Assert.Equal("COMPLETED", updatedOrder!.Status);

        var tx = await context.WalletTransactions.SingleAsync();
        Assert.NotNull(tx);
        Assert.Equal(order.Id, tx.Id);
        Assert.Equal("FT2401019912", tx.ReferenceCode);
        Assert.Equal("SUCCESS", tx.Status);
        Assert.Equal("TOPUP", tx.Type);
        Assert.Equal(1000000, tx.Amount);
        Assert.Equal(200000, tx.BalanceBefore);
        Assert.Equal(1200000, tx.BalanceAfter);

        webhookPayload.Id++;
        webhookPayload.ReferenceCode = "FT_SECOND_TRANSFER";
        Assert.True((await service.ProcessSepayWebhookAsync(webhookPayload)).Success);
        var review = await context.WalletTransactions.SingleAsync(t => t.Status == "REVIEW");
        Assert.Equal(order.OrderCode, review.ReconciliationOrderCode);
        Assert.Equal(1200000, updatedTenant.WalletBalance);
        Assert.Single(await service.GetWalletTransactionsAsync("tenant-001"));
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

    [Fact]
    public async Task CreateTopupOrder_TenantNotFound_ThrowsKeyNotFoundException()
    {
        using var context = CreateInMemoryDbContext();
        var service = new WalletService(context, CreateDefaultSettings());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateTopupOrderAsync("unknown-tenant", new CreateTopupOrderRequest(100000)));
    }

    [Fact]
    public async Task CreateTopupOrder_EmptySettings_FallsBackToDefaultValues()
    {
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant { Id = "tenant-001", Name = "Store", Code = "ST1" };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var emptySettings = Options.Create(new SePaySettings());
        var service = new WalletService(context, emptySettings);

        var result = await service.CreateTopupOrderAsync("tenant-001", new CreateTopupOrderRequest(50000));
        Assert.NotNull(result);
        Assert.Equal("MBBank", result.BankCode);
        Assert.Equal("190065228888", result.AccountNumber);
    }

    [Fact]
    public async Task GetTopupOrderStatus_ShouldHandle_NotFound_Expired_AndCompleted()
    {
        using var context = CreateInMemoryDbContext();
        var tenant = new Tenant { Id = "tenant-001", Name = "Store", Code = "ST1", WalletBalance = 50000 };
        context.Tenants.Add(tenant);

        var pendingExpiredOrder = new WalletTransaction
        {
            TenantId = "tenant-001",
            TransactionCode = "WAL-ORDER-111222",
            Type = "TOPUP",
            OrderCode = "111222",
            Amount = 100000,
            RequestedAmount = 100000,
            TransferContent = "WAL 111222",
            Status = "PENDING",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var completedAt = DateTime.UtcNow.AddMinutes(-1);
        context.WalletTransactions.AddRange(pendingExpiredOrder, new WalletTransaction
        {
            TenantId = tenant.Id,
            TransactionCode = "WAL-ORDER-333444",
            Type = "TOPUP",
            OrderCode = "333444",
            Amount = 80_000m,
            RequestedAmount = 75_000m,
            Status = "SUCCESS",
            CompletedAt = completedAt
        });
        await context.SaveChangesAsync();

        var service = new WalletService(context, CreateDefaultSettings());

        // Not found
        var notFound = await service.GetTopupOrderStatusAsync("tenant-001", "999999");
        Assert.Null(notFound);

        // Expired order
        var expired = await service.GetTopupOrderStatusAsync("tenant-001", "111222");
        Assert.NotNull(expired);
        Assert.Equal("EXPIRED", expired.Status);

        var completed = await service.GetTopupOrderStatusAsync("tenant-001", "333444");
        Assert.NotNull(completed);
        Assert.Equal("COMPLETED", completed.Status);
        Assert.Equal(80_000m, completed.Amount);
        Assert.Equal(completedAt, completed.CompletedAt);
        Assert.Null(await service.GetTopupOrderStatusAsync("unknown", "111222"));
    }

    [Fact]
    public async Task GetWalletBalance_And_Transactions_ShouldHandle_VariousFilters()
    {
        using var context = CreateInMemoryDbContext();
        var service = new WalletService(context, CreateDefaultSettings());

        // Unknown tenant
        var emptyBalance = await service.GetWalletBalanceAsync("unknown");
        Assert.Equal(0, emptyBalance.WalletBalance);
        var emptyTx = await service.GetWalletTransactionsAsync("unknown");
        Assert.Empty(emptyTx);

        // Tenant with transactions
        var tenant = new Tenant { Id = "tenant-001", Code = "ST1", WalletBalance = 700000 };
        context.Tenants.Add(tenant);
        context.WalletTransactions.AddRange(
            new WalletTransaction
            {
                TenantId = "tenant-001",
                TransactionCode = "TX1",
                Type = "TOPUP",
                Amount = 1000000,
                Status = "SUCCESS"
            },
            new WalletTransaction
            {
                TenantId = "tenant-001",
                TransactionCode = "TX2",
                Type = "PURCHASE_PLAN",
                Amount = 200000,
                Status = "SUCCESS"
            },
            new WalletTransaction
            {
                TenantId = "tenant-001",
                TransactionCode = "TX3",
                Type = "PURCHASE_ADDON",
                Amount = 100000,
                Status = "SUCCESS"
            }
        );
        await context.SaveChangesAsync();

        var balance = await service.GetWalletBalanceAsync("tenant-001");
        Assert.Equal(700000, balance.WalletBalance);
        Assert.Equal(1000000, balance.TotalTopup);
        Assert.Equal(300000, balance.TotalSpent);

        // Transactions filtered by type
        var topupTx = await service.GetWalletTransactionsAsync("tenant-001", "TOPUP");
        Assert.Single(topupTx);

        // Transactions with "ALL" filter
        var allTx = await service.GetWalletTransactionsAsync("tenant-001", "ALL");
        Assert.Equal(3, allTx.Count);
    }

    [Fact]
    public async Task ProcessSepayWebhook_CandidateCodeFallback_And_NoTenantFound()
    {
        using var context = CreateInMemoryDbContext();
        var service = new WalletService(context, CreateDefaultSettings());

        // No tenant in DB at all
        var payloadNoTenant = new SepayWebhookPayload
        {
            Id = 1,
            TransferType = "in",
            TransferAmount = 100000,
            Content = "Khong co code gi ca"
        };
        var resNoTenant = await service.ProcessSepayWebhookAsync(payloadNoTenant);
        Assert.False(resNoTenant.Success);

        // Tenant found via candidate code matching Tenant.Code
        var tenant = new Tenant { Id = "tenant-code-01", Code = "STORE99", WalletBalance = 0 };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var payloadStore = new SepayWebhookPayload
        {
            Id = 2,
            TransferType = "in",
            TransferAmount = 250000,
            Content = "WAL STORE99 nap tien",
            ReferenceCode = null // Will fallback to SEPAY-2
        };
        var resStore = await service.ProcessSepayWebhookAsync(payloadStore);
        Assert.True(resStore.Success);
        Assert.Equal("tenant-code-01", resStore.TenantId);
    }

    [Fact]
    public async Task DeductWallet_TenantNotFound_ThrowsKeyNotFoundException()
    {
        using var context = CreateInMemoryDbContext();
        var service = new WalletService(context, CreateDefaultSettings());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeductWalletAsync("unknown", 50000, "Desc", "REF01"));
    }
}

