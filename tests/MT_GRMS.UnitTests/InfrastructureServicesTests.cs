using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class InfrastructureServicesTests
{
    private const string TenantId = "tenant-infra-01";

    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = TenantId;
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

    private AppDbContext CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"InfraDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task SupplierOrderExcelGenerator_GenerateAsync_ShouldReturnValidBytes()
    {
        var generator = new SupplierOrderExcelGenerator();
        var package = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SupplierCodeSnapshot = "SUP-01",
            SupplierNameSnapshot = "Cong ty TNHH Thuc Pham Sapa"
        };
        var branchId = Guid.NewGuid();
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchId = branchId,
            Code = "PO-2026-001",
            StoreCodeSnapshot = "BR-01",
            StoreNameSnapshot = "Chi nhanh Quan 1",
            StoreAddressSnapshot = "123 Le Loi",
            StorePhoneSnapshot = "0901234567",
            RevisionNumber = 1,
            DeliveryDate = DateTime.UtcNow.Date.AddDays(2),
            Details = new List<PurchaseOrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    ProductCodeSnapshot = "PRD-01",
                    ProductNameSnapshot = "Sua tuoi Vinamilk",
                    PurchaseUnitNameSnapshot = "Thung",
                    DeliveryUoM = "Thung",
                    OrderedQty = 10m,
                    UnitPrice = 350000m,
                    LineAmount = 3500000m,
                    DeliveryDate = DateTime.UtcNow.Date.AddDays(2)
                }
            }
        };

        var bytes = await generator.GenerateAsync(package, new[] { po });

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task LocalFileStorage_Save_Read_Exists_AndValidation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"StorageTest_{Guid.NewGuid()}");
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "SUPPLIER_ORDER_STORAGE_PATH", tempDir },
            { "SUPPLIER_ORDER_STORAGE_PROVIDER", "LOCAL" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var storage = new LocalFileStorage(config, NullLogger<LocalFileStorage>.Instance);

        try
        {
            var content = "Hello World Excel Content";
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // 1. Save
            var result = await storage.SaveAsync("folder/test.txt", ms, "text/plain");
            Assert.Equal("folder/test.txt", result.StorageKey);
            Assert.True(result.SizeBytes > 0);
            Assert.False(string.IsNullOrEmpty(result.Sha256Hex));

            // 2. Exists
            Assert.True(await storage.ExistsAsync("folder/test.txt"));
            Assert.False(await storage.ExistsAsync("non_existent.txt"));

            // 3. Read
            await using var readStream = await storage.OpenReadAsync("folder/test.txt");
            using var reader = new StreamReader(readStream);
            var text = await reader.ReadToEndAsync();
            Assert.Equal(content, text);

            // 4. Path traversal should throw
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("../outside.txt"));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(""));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EmailService_DevModeFallback_ShouldReturnFalseWithoutSmtp()
    {
        var options = Options.Create(new EmailSettings
        {
            SmtpUser = "", // Empty triggers dev fallback
            SmtpPass = ""
        });
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var service = new EmailService(options, NullLogger<EmailService>.Instance, envMock.Object);

        await service.SendPasswordResetOtpAsync("test@example.com", "Test User", "123456");
        var res1 = await service.SendLoginOtpAsync("test@example.com", "Test User", "123456");
        var res2 = await service.SendRegistrationOtpAsync("test@example.com", "Test User", "My Store", "123456");

        Assert.False(res1);
        Assert.False(res2);
    }

    [Fact]
    public async Task SmtpSupplierOrderEmailSender_MissingConfig_ShouldLogWarningAndReturn()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var sender = new SmtpSupplierOrderEmailSender(config, NullLogger<SmtpSupplierOrderEmailSender>.Instance);

        var msg = new SupplierOrderEmailMessage
        {
            To = "supplier@example.com",
            Subject = "PO dispatch",
            BodyText = "PO",
            AttachmentFileName = "PO.xlsx",
            AttachmentBytes = new byte[] { 1, 2, 3 }
        };

        // Should return gracefully without throwing
        await sender.SendSupplierOrderAsync(msg);
    }

    [Fact]
    public async Task StoreOwnerDashboardService_GetAsync_ShouldCalculateDashboard()
    {
        using var db = CreateDbContext();
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BranchCode = "BR-01",
            Name = "Chi nhanh Quan 1",
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var tenantContext = new TestTenantContext();
        var mockPnl = new Mock<IPnLReportService>();
        var summary = new PnLSummaryResponse(
            Period: new PnLPeriod(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1)),
            BranchId: branch.Id,
            BranchName: branch.Name,
            GrossRevenue: 10000000m,
            Discounts: 0m,
            SalesReturns: 0m,
            NetRevenue: 10000000m,
            Cogs: 6000000m,
            GrossProfit: 4000000m,
            GrossMarginPercentage: 40m,
            OperatingExpenses: new OperatingExpenseSummary(0m, new List<CategoryExpenseBreakdown>()),
            BadDebtExpense: 0m,
            NetProfit: 4000000m,
            NetMarginPercentage: 40m,
            TotalOrders: 10,
            AverageOrderValue: 1000000m
        );
        mockPnl.Setup(p => p.GetPnLSummaryAsync(It.IsAny<PnLFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);
        mockPnl.Setup(p => p.GetTrendAsync(It.IsAny<PnLFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PnLTrendPointResponse>());

        var mockInv = new Mock<IInventoryService>();
        var stockSummary = new InventoryPagedResultDto(
            Items: new List<InventoryStockItemDto>(),
            TotalCount: 0,
            PageIndex: 1,
            PageSize: 5,
            KpiSummary: new InventoryKpiSummaryDto(0, 0, 0, 0, 0)
        );
        mockInv.Setup(i => i.GetPagedStockSummaryAsync(It.IsAny<InventoryFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockSummary);

        var service = new StoreOwnerDashboardService(db, tenantContext, mockPnl.Object, mockInv.Object);

        // 1. Period "today"
        var resToday = await service.GetAsync("today", null);
        Assert.NotNull(resToday);

        // 2. Period "yesterday"
        var resYesterday = await service.GetAsync("yesterday", branch.Id);
        Assert.NotNull(resYesterday);

        // 3. Period "last7days"
        var res7Days = await service.GetAsync("last7days", null);
        Assert.NotNull(res7Days);

        // 4. Period "thisMonth"
        var resMonth = await service.GetAsync("thisMonth", null);
        Assert.NotNull(resMonth);

        // 5. Unknown branch -> null
        var resUnknownBranch = await service.GetAsync("today", Guid.NewGuid());
        Assert.Null(resUnknownBranch);
    }

    [Fact]
    public async Task SupplierDispatchPackageService_AllMethods_ShouldWork()
    {
        using var db = CreateDbContext();
        var pkg = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SupplierId = Guid.NewGuid(),
            SupplierCodeSnapshot = "SUP-01",
            SupplierNameSnapshot = "Sapa NCC",
            GenerationStatus = PackageGenerationStatuses.Generated,
            FileStorageKey = "packages/test.xlsx",
            FileName = "PO_SUP-01.xlsx",
            EmailStatus = PackageEmailStatuses.Scheduled,
            EmailSendMode = EmailSendModes.ScheduledSend,
            CreatedAt = DateTime.UtcNow
        };
        db.SupplierDispatchPackages.Add(pkg);
        await db.SaveChangesAsync();

        var tenantContext = new TestTenantContext();
        var mockStorage = new Mock<IFileStorage>();
        mockStorage.Setup(s => s.OpenReadAsync("packages/test.xlsx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("ExcelData")));

        var mockGenerator = new Mock<ISupplierOrderExcelGenerator>();
        var mockEmail = new Mock<ISupplierOrderEmailSender>();

        var service = new SupplierDispatchPackageService(
            db, tenantContext, mockStorage.Object, mockGenerator.Object, mockEmail.Object,
            NullLogger<SupplierDispatchPackageService>.Instance);

        // 1. GetByIdAsync
        var found = await service.GetByIdAsync(pkg.Id);
        Assert.NotNull(found);

        // 2. ListAsync
        var list = await service.ListAsync(pkg.SupplierId);
        Assert.Single(list);

        // 3. DownloadAsync
        var (content, fileName, _) = await service.DownloadAsync(pkg.Id);
        Assert.True(content.Length > 0);
        Assert.Equal("PO_SUP-01.xlsx", fileName);

        // 4. CancelScheduledAsync
        await service.CancelScheduledAsync(pkg.Id);
        var updated = await db.SupplierDispatchPackages.FindAsync(pkg.Id);
        Assert.Equal(PackageEmailStatuses.Cancelled, updated!.EmailStatus);

        // 5. RetryGenerationAsync
        await service.RetryGenerationAsync(pkg.Id);
        updated = await db.SupplierDispatchPackages.FindAsync(pkg.Id);
        Assert.Equal(PackageGenerationStatuses.Pending, updated!.GenerationStatus);
    }

    [Fact]
    public async Task CloudinaryService_Unconfigured_ShouldThrowInvalidOperationException()
    {
        var options = Options.Create(new CloudinarySettings
        {
            CloudName = "",
            ApiKey = "",
            ApiSecret = ""
        });
        var service = new CloudinaryService(options, NullLogger<CloudinaryService>.Instance);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadImageAsync(ms, "test.png", "folder"));
    }
}
