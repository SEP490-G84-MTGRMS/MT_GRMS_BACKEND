using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using MT_GRMS.Infrastructure.Workers;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SupplierDispatchPackageServiceTests
{
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly Mock<IFileStorage> _mockFileStorage;
    private readonly Mock<ISupplierOrderExcelGenerator> _mockExcelGenerator;
    private readonly Mock<ISupplierOrderEmailSender> _mockEmailSender;
    private readonly Mock<ILogger<SupplierDispatchPackageService>> _mockLogger;
    private const string TenantId = "tenant-dispatch-01";

    public SupplierDispatchPackageServiceTests()
    {
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);

        _mockFileStorage = new Mock<IFileStorage>();
        _mockExcelGenerator = new Mock<ISupplierOrderExcelGenerator>();
        _mockEmailSender = new Mock<ISupplierOrderEmailSender>();
        _mockLogger = new Mock<ILogger<SupplierDispatchPackageService>>();
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"DispatchDb_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, _mockTenant.Object);
    }

    private SupplierDispatchPackageService CreateService(AppDbContext db)
    {
        return new SupplierDispatchPackageService(
            db,
            _mockTenant.Object,
            _mockFileStorage.Object,
            _mockExcelGenerator.Object,
            _mockEmailSender.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task CreatePackageForDispatchAsync_ValidationAndEdgeCases()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        // 1. EmailSendMode is null or whitespace -> returns null
        var cmd1 = new SplitMatrixPoCommand { EmailSendMode = null };
        var res1 = await service.CreatePackageForDispatchAsync(cmd1, new SplitMatrixBatchResultDto());
        Assert.Null(res1);

        // 2. BatchResult has 0 count and empty POs -> returns null
        var cmd2 = new SplitMatrixPoCommand { EmailSendMode = "SEND_NOW" };
        var res2 = await service.CreatePackageForDispatchAsync(cmd2, new SplitMatrixBatchResultDto { SuccessCount = 0 });
        Assert.Null(res2);

        // 3. Invalid mode -> BusinessException EMAIL_MODE_INVALID
        var cmd3 = new SplitMatrixPoCommand { EmailSendMode = "UNKNOWN_MODE" };
        var batch3 = new SplitMatrixBatchResultDto
        {
            SuccessCount = 1,
            CreatedPurchaseOrders = new List<PurchaseOrderDto> { new() { Id = Guid.NewGuid(), SupplierId = Guid.NewGuid() } }
        };
        var ex3 = await Assert.ThrowsAsync<BusinessException>(() => service.CreatePackageForDispatchAsync(cmd3, batch3));
        Assert.Equal("EMAIL_MODE_INVALID", ex3.ErrorCode);

        // 4. Missing SupplierId -> BusinessException SUPPLIER_REQUIRED
        var cmd4 = new SplitMatrixPoCommand { EmailSendMode = "SEND_NOW" };
        var batch4 = new SplitMatrixBatchResultDto
        {
            SuccessCount = 1,
            CreatedPurchaseOrders = new List<PurchaseOrderDto> { new() { Id = Guid.NewGuid(), SupplierId = Guid.Empty } }
        };
        var ex4 = await Assert.ThrowsAsync<BusinessException>(() => service.CreatePackageForDispatchAsync(cmd4, batch4));
        Assert.Equal("SUPPLIER_REQUIRED", ex4.ErrorCode);

        // 5. Supplier not found -> NotFoundException
        var supplierId = Guid.NewGuid();
        var cmd5 = new SplitMatrixPoCommand { EmailSendMode = "SEND_NOW", SupplierId = supplierId };
        var batch5 = new SplitMatrixBatchResultDto
        {
            SuccessCount = 1,
            CreatedPurchaseOrders = new List<PurchaseOrderDto> { new() { Id = Guid.NewGuid(), SupplierId = supplierId } }
        };
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreatePackageForDispatchAsync(cmd5, batch5));

        // 6. Supplier has invalid / missing email -> BusinessException SUPPLIER_EMAIL_INVALID
        var supplier = new Supplier
        {
            Id = supplierId,
            TenantId = TenantId,
            Code = "SUP01",
            Name = "Supplier One",
            Email = "invalid-email-string"
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var ex6 = await Assert.ThrowsAsync<BusinessException>(() => service.CreatePackageForDispatchAsync(cmd5, batch5));
        Assert.Equal("SUPPLIER_EMAIL_INVALID", ex6.ErrorCode);

        // 7. ScheduledSend without ScheduledSendAt -> BusinessException SCHEDULE_REQUIRED
        supplier.Email = "supplier@test.com";
        await db.SaveChangesAsync();

        var cmd7 = new SplitMatrixPoCommand
        {
            EmailSendMode = "SCHEDULED_SEND",
            SupplierId = supplierId,
            ScheduledSendAt = null
        };
        var ex7 = await Assert.ThrowsAsync<BusinessException>(() => service.CreatePackageForDispatchAsync(cmd7, batch5));
        Assert.Equal("SCHEDULE_REQUIRED", ex7.ErrorCode);

        // 8. ScheduledSend with past time -> BusinessException SCHEDULE_PAST
        var cmd8 = new SplitMatrixPoCommand
        {
            EmailSendMode = "SCHEDULED_SEND",
            SupplierId = supplierId,
            ScheduledSendAt = DateTime.UtcNow.AddHours(-2)
        };
        var ex8 = await Assert.ThrowsAsync<BusinessException>(() => service.CreatePackageForDispatchAsync(cmd8, batch5));
        Assert.Equal("SCHEDULE_PAST", ex8.ErrorCode);
    }

    [Fact]
    public async Task CreatePackageForDispatchAsync_SuccessFlow_SendNowAndScheduled()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var supplierId = Guid.NewGuid();
        var supplier = new Supplier
        {
            Id = supplierId,
            TenantId = TenantId,
            Code = "SUP01",
            Name = "Supplier Test",
            Email = "ncc@test.com"
        };
        var po1 = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "PO-001",
            SupplierId = supplierId
        };
        var po2 = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "PO-002",
            SupplierId = supplierId
        };
        db.Suppliers.Add(supplier);
        db.PurchaseOrders.AddRange(po1, po2);
        await db.SaveChangesAsync();

        var batchResult = new SplitMatrixBatchResultDto
        {
            SuccessCount = 2,
            CreatedPurchaseOrders = new List<PurchaseOrderDto>
            {
                new() { Id = po1.Id, Code = po1.Code, SupplierId = supplierId },
                new() { Id = po2.Id, Code = po2.Code, SupplierId = supplierId }
            }
        };

        // 1. Success SEND_NOW
        var cmdNow = new SplitMatrixPoCommand
        {
            EmailSendMode = "SEND_NOW",
            SupplierId = supplierId,
            CcEmails = new List<string> { "cc1@test.com" },
            Note = "Giao buoi sang"
        };
        var resultNow = await service.CreatePackageForDispatchAsync(cmdNow, batchResult);
        Assert.NotNull(resultNow);
        Assert.Equal("SEND_NOW", resultNow.EmailSendMode);
        Assert.Equal("PENDING", resultNow.EmailStatus);
        Assert.Equal(1, resultNow.PackageRevisionNumber);
        Assert.Equal(2, resultNow.PurchaseOrderCount);

        // Verify outbox message created
        var outbox = await db.OutboxMessages.FirstOrDefaultAsync();
        Assert.NotNull(outbox);
        Assert.Equal(OutboxMessageTypes.SupplierOrderPackageProcess, outbox.Type);

        // 2. Success SCHEDULED_SEND (Unspecified kind interpret as VN time)
        var futureDate = DateTime.Now.AddDays(2);
        var cmdSched = new SplitMatrixPoCommand
        {
            EmailSendMode = "SCHEDULED_SEND",
            SupplierId = supplierId,
            ScheduledSendAt = DateTime.SpecifyKind(futureDate, DateTimeKind.Unspecified),
            CcEmails = new List<string> { "cc2@test.com" },
            Note = "Hen gio giao"
        };
        var resultSched = await service.CreatePackageForDispatchAsync(cmdSched, batchResult);
        Assert.NotNull(resultSched);
        Assert.Equal("SCHEDULED_SEND", resultSched.EmailSendMode);
        Assert.Equal("SCHEDULED", resultSched.EmailStatus);
        Assert.Equal(2, resultSched.PackageRevisionNumber);

        // Check that old package was superseded
        var oldPkg = await db.SupplierDispatchPackages.FindAsync(resultNow.Id);
        Assert.NotNull(oldPkg);
        Assert.Equal("SUPERSEDED", oldPkg.EmailStatus);
    }

    [Fact]
    public async Task GetByIdAsync_And_ListAsync_ShouldReturnExpectedResults()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        // 1. Nonexistent package -> null
        var nullPkg = await service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(nullPkg);

        // 2. Create sample package with item
        var supplierId = Guid.NewGuid();
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "PO-LIST-01",
            SupplierId = supplierId
        };
        var pkg = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = supplierId,
            SupplierCodeSnapshot = "SUP01",
            SupplierNameSnapshot = "Supplier 01",
            EmailTo = "sup01@test.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "PENDING",
            GenerationStatus = "PENDING"
        };
        pkg.Items.Add(new SupplierDispatchPackageItem
        {
            TenantId = TenantId,
            PackageId = pkg.Id,
            PurchaseOrderId = po.Id,
            PurchaseOrder = po
        });
        db.PurchaseOrders.Add(po);
        db.SupplierDispatchPackages.Add(pkg);
        await db.SaveChangesAsync();

        var dto = await service.GetByIdAsync(pkg.Id);
        Assert.NotNull(dto);
        Assert.Equal(pkg.Id, dto.Id);
        Assert.Contains("PO-LIST-01", dto.PurchaseOrderCodes);

        // 3. ListAsync
        var listAll = await service.ListAsync(null, take: 10);
        Assert.Single(listAll);

        var listFilter = await service.ListAsync(supplierId, take: 10);
        Assert.Single(listFilter);

        var listOther = await service.ListAsync(Guid.NewGuid(), take: 10);
        Assert.Empty(listOther);
    }

    [Fact]
    public async Task RetryGeneration_RetryEmail_CancelScheduled_Download_Validation()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        // 1. NotFound tests
        var dummyId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => service.RetryGenerationAsync(dummyId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RetryEmailAsync(dummyId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelScheduledAsync(dummyId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DownloadAsync(dummyId));

        var pkg = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            SupplierCodeSnapshot = "SUP-LOCK",
            EmailTo = "test@sup.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "SENDING",
            GenerationStatus = "GENERATED",
            FileStorageKey = "storage/key/test.xlsx",
            FileName = "test.xlsx"
        };
        db.SupplierDispatchPackages.Add(pkg);
        await db.SaveChangesAsync();

        // 2. RetryGeneration: Locked when SENDING -> PACKAGE_LOCKED
        var exGenLock = await Assert.ThrowsAsync<BusinessException>(() => service.RetryGenerationAsync(pkg.Id));
        Assert.Equal("PACKAGE_LOCKED", exGenLock.ErrorCode);

        // 3. RetryEmail: Locked when SENDING -> EMAIL_SENDING
        var exEmailLock = await Assert.ThrowsAsync<BusinessException>(() => service.RetryEmailAsync(pkg.Id));
        Assert.Equal("EMAIL_SENDING", exEmailLock.ErrorCode);

        // 4. CancelScheduled: Forbidden when SENDING -> CANCEL_FORBIDDEN
        var exCancelLock = await Assert.ThrowsAsync<BusinessException>(() => service.CancelScheduledAsync(pkg.Id));
        Assert.Equal("CANCEL_FORBIDDEN", exCancelLock.ErrorCode);

        // 5. DownloadAsync: Success when GENERATED and key present
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };
        _mockFileStorage.Setup(s => s.OpenReadAsync(pkg.FileStorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(fileBytes));

        var (content, fileName, contentType) = await service.DownloadAsync(pkg.Id);
        Assert.Equal(fileBytes, content);
        Assert.Equal("test.xlsx", fileName);

        // DownloadAsync: Fails if not generated
        pkg.GenerationStatus = "PENDING";
        await db.SaveChangesAsync();
        var exDownNotReady = await Assert.ThrowsAsync<BusinessException>(() => service.DownloadAsync(pkg.Id));
        Assert.Equal("FILE_NOT_READY", exDownNotReady.ErrorCode);

        // 6. RetryGeneration: Success when unlocked
        pkg.EmailStatus = "FAILED";
        await db.SaveChangesAsync();
        await service.RetryGenerationAsync(pkg.Id);
        Assert.Equal("PENDING", pkg.GenerationStatus);

        // 7. RetryEmail: Fails if not generated
        var exEmailNoFile = await Assert.ThrowsAsync<BusinessException>(() => service.RetryEmailAsync(pkg.Id));
        Assert.Equal("FILE_REQUIRED", exEmailNoFile.ErrorCode);

        // RetryEmail: Fails if superseded
        pkg.GenerationStatus = "GENERATED";
        pkg.EmailStatus = "SUPERSEDED";
        await db.SaveChangesAsync();
        var exSuperseded = await Assert.ThrowsAsync<BusinessException>(() => service.RetryEmailAsync(pkg.Id));
        Assert.Equal("PACKAGE_SUPERSEDED", exSuperseded.ErrorCode);

        // RetryEmail: Success when FAILED -> becomes PENDING
        pkg.GenerationStatus = "GENERATED";
        pkg.FileStorageKey = "storage/key/test.xlsx";
        pkg.EmailStatus = "FAILED";
        await db.SaveChangesAsync();
        await service.RetryEmailAsync(pkg.Id);
        Assert.Equal("PENDING", pkg.EmailStatus);

        // RetryEmail: Success when CANCELLED with future schedule -> becomes SCHEDULED
        pkg.EmailStatus = "CANCELLED";
        pkg.EmailSendMode = "SCHEDULED_SEND";
        pkg.ScheduledSendAtUtc = DateTime.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        await service.RetryEmailAsync(pkg.Id);
        Assert.Equal("SCHEDULED", pkg.EmailStatus);

        // 8. CancelScheduled: Fails if not scheduled
        pkg.EmailStatus = "PENDING";
        pkg.EmailSendMode = "SEND_NOW";
        await db.SaveChangesAsync();
        var exNotSched = await Assert.ThrowsAsync<BusinessException>(() => service.CancelScheduledAsync(pkg.Id));
        Assert.Equal("NOT_SCHEDULED", exNotSched.ErrorCode);

        // CancelScheduled: Success when SCHEDULED
        pkg.EmailStatus = "SCHEDULED";
        pkg.EmailSendMode = "SCHEDULED_SEND";
        await db.SaveChangesAsync();
        await service.CancelScheduledAsync(pkg.Id);
        Assert.Equal("CANCELLED", pkg.EmailStatus);
    }

    [Fact]
    public async Task ProcessPackageAsync_FullExecution_GenerationAndEmail()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        // 1. Package not found -> returns silently
        await service.ProcessPackageAsync(Guid.NewGuid());

        // 2. Package superseded -> returns silently
        var pkgSuperseded = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            EmailTo = "test@sup.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "SUPERSEDED"
        };
        db.SupplierDispatchPackages.Add(pkgSuperseded);
        await db.SaveChangesAsync();
        await service.ProcessPackageAsync(pkgSuperseded.Id);

        // 3. Package active lease by another worker -> returns silently
        var pkgLeased = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            EmailTo = "test@sup.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "PENDING",
            ProcessingLeaseUntilUtc = DateTime.UtcNow.AddMinutes(3),
            ProcessingLockedBy = "OTHER_MACHINE"
        };
        db.SupplierDispatchPackages.Add(pkgLeased);
        await db.SaveChangesAsync();
        await service.ProcessPackageAsync(pkgLeased.Id);

        // 4. Package with NO POs -> Generation fails
        var pkgNoPos = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            EmailTo = "test@sup.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "PENDING",
            GenerationStatus = "PENDING"
        };
        db.SupplierDispatchPackages.Add(pkgNoPos);
        await db.SaveChangesAsync();
        await service.ProcessPackageAsync(pkgNoPos.Id);
        Assert.Equal("FAILED", pkgNoPos.GenerationStatus);
        Assert.Contains("Package không có PO", pkgNoPos.LastError);

        // 5. Package with POs missing snapshots -> Generation fails
        var poMissingSnapshot = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "PO-NOSNAP",
            SupplierCodeSnapshot = null // Missing snapshot
        };
        db.PurchaseOrders.Add(poMissingSnapshot);

        var pkgMissingSnapshot = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            EmailTo = "test@sup.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "PENDING",
            GenerationStatus = "PENDING"
        };
        pkgMissingSnapshot.Items.Add(new SupplierDispatchPackageItem
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PackageId = pkgMissingSnapshot.Id,
            PurchaseOrderId = poMissingSnapshot.Id
        });
        db.SupplierDispatchPackages.Add(pkgMissingSnapshot);
        await db.SaveChangesAsync();

        await service.ProcessPackageAsync(pkgMissingSnapshot.Id);
        Assert.Equal("FAILED", pkgMissingSnapshot.GenerationStatus);
        Assert.Contains("PO thiếu snapshot", pkgMissingSnapshot.LastError);

        // 6. Valid POs with snapshots -> Generation and Email send SUCCESS!
        var poValid = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "PO-SNAP-OK",
            SupplierCodeSnapshot = "SUP01",
            StoreCodeSnapshot = "STORE01",
            StoreNameSnapshot = "Store Central",
            DeliveryDate = DateTime.UtcNow.Date.AddDays(1)
        };
        poValid.Details.Add(new PurchaseOrderDetail
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = poValid.Id,
            ProductCodeSnapshot = "PROD01",
            ProductNameSnapshot = "Fresh Milk",
            OrderedQty = 10,
            PurchaseUnitNameSnapshot = "Box"
        });
        db.PurchaseOrders.Add(poValid);

        var validPkg = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            SupplierCodeSnapshot = "SUP01",
            SupplierNameSnapshot = "Dairy Supplier",
            EmailTo = "sup@dairy.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = "PENDING",
            GenerationStatus = "PENDING",
            PackageRevisionNumber = 1,
            Note = "Giao buoi sang"
        };
        validPkg.Items.Add(new SupplierDispatchPackageItem
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PackageId = validPkg.Id,
            PurchaseOrderId = poValid.Id
        });
        db.SupplierDispatchPackages.Add(validPkg);
        await db.SaveChangesAsync();

        var fakeExcelBytes = new byte[] { 10, 20, 30, 40 };
        _mockExcelGenerator.Setup(g => g.GenerateAsync(It.IsAny<SupplierDispatchPackage>(), It.IsAny<IReadOnlyList<PurchaseOrder>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeExcelBytes);

        _mockFileStorage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredFileResult("storage/test-po.xlsx", 40, "sha256hex"));

        _mockFileStorage.Setup(s => s.OpenReadAsync("storage/test-po.xlsx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(fakeExcelBytes));

        await service.ProcessPackageAsync(validPkg.Id);

        Assert.Equal("GENERATED", validPkg.GenerationStatus);
        Assert.Equal("SENT", validPkg.EmailStatus);
        Assert.NotNull(validPkg.SentAtUtc);
        _mockEmailSender.Verify(e => e.SendSupplierOrderAsync(It.IsAny<SupplierOrderEmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);

        // 7. Email Sender throws -> EmailStatus becomes FAILED
        validPkg.EmailStatus = "PENDING";
        await db.SaveChangesAsync();
        _mockEmailSender.Setup(e => e.SendSupplierOrderAsync(It.IsAny<SupplierOrderEmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP connection refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessPackageAsync(validPkg.Id));
        Assert.Equal("FAILED", validPkg.EmailStatus);
        Assert.Contains("SMTP connection refused", validPkg.LastError);
    }

    [Fact]
    public async Task SupplierOrderDispatchWorker_ProcessesOutbox_Scheduled_AndStuckPackages()
    {
        var services = new ServiceCollection();
        var db = CreateDbContext();
        var mockPackageService = new Mock<ISupplierDispatchPackageService>();
        var mockWorkerLogger = new Mock<ILogger<SupplierOrderDispatchWorker>>();

        services.AddSingleton(db);
        services.AddSingleton(mockPackageService.Object);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // 1. Add valid pending outbox message
        var pkgId1 = Guid.NewGuid();
        var outboxMsg1 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Type = OutboxMessageTypes.SupplierOrderPackageProcess,
            PayloadJson = JsonSerializer.Serialize(new { PackageId = pkgId1 }),
            Status = OutboxStatuses.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        // 2. Add invalid JSON outbox message (missing packageId)
        var outboxMsg2 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Type = OutboxMessageTypes.SupplierOrderPackageProcess,
            PayloadJson = "{ \"OtherField\": 123 }",
            Status = OutboxStatuses.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-4),
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        // 3. Add outbox message where ProcessPackageAsync will throw
        var pkgId3 = Guid.NewGuid();
        var outboxMsg3 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Type = OutboxMessageTypes.SupplierOrderPackageProcess,
            PayloadJson = JsonSerializer.Serialize(new { packageId = pkgId3 }),
            Status = OutboxStatuses.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        mockPackageService.Setup(s => s.ProcessPackageAsync(pkgId3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Outbox processing error"));

        // 4. Add scheduled package due for dispatch
        var duePkg = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            EmailTo = "due@test.com",
            EmailSendMode = "SCHEDULED_SEND",
            EmailStatus = PackageEmailStatuses.Scheduled,
            GenerationStatus = PackageGenerationStatuses.Generated,
            ScheduledSendAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        // 5. Add stuck package recovery
        var stuckPkg = new SupplierDispatchPackage
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DispatchBatchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            EmailTo = "stuck@test.com",
            EmailSendMode = "SEND_NOW",
            EmailStatus = PackageEmailStatuses.Pending,
            GenerationStatus = PackageGenerationStatuses.Generating,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        db.OutboxMessages.AddRange(outboxMsg1, outboxMsg2, outboxMsg3);
        db.SupplierDispatchPackages.AddRange(duePkg, stuckPkg);
        await db.SaveChangesAsync();

        var worker = new SupplierOrderDispatchWorker(scopeFactory, mockWorkerLogger.Object);

        // Run worker with CancellationToken that cancels after a brief run
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(200);
            await worker.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }

        // Verify outbox 1 processed
        var reloaded1 = await db.OutboxMessages.FindAsync(outboxMsg1.Id);
        Assert.Equal(OutboxStatuses.Processed, reloaded1!.Status);
        mockPackageService.Verify(s => s.ProcessPackageAsync(pkgId1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Verify outbox 2 dead
        var reloaded2 = await db.OutboxMessages.FindAsync(outboxMsg2.Id);
        Assert.Equal(OutboxStatuses.Dead, reloaded2!.Status);

        // Verify outbox 3 failed
        var reloaded3 = await db.OutboxMessages.FindAsync(outboxMsg3.Id);
        Assert.Equal(OutboxStatuses.Failed, reloaded3!.Status);

        // Verify due scheduled package processed
        mockPackageService.Verify(s => s.ProcessPackageAsync(duePkg.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Verify stuck package processed
        mockPackageService.Verify(s => s.ProcessPackageAsync(stuckPkg.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
