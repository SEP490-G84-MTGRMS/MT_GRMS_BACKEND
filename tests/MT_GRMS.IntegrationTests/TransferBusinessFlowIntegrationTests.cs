using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp (Integration Tests) cho phân hệ Luân chuyển kho liên chi nhánh (FE-15).
/// Kiểm chứng luồng nghiệp vụ thực tế trên DbContext:
/// - Chu kỳ điều chuyển toàn vẹn: Draft -> InTransit -> Received.
/// - Ghi nhận hàng hỏng và cơ chế kho cách ly (Damaged & Quarantine).
/// - Hủy phiếu đang vận chuyển và cơ chế tự động hoàn tồn (Stock Rollback).
/// - Cô lập dữ liệu đa người thuê (Multi-Tenancy Isolation).
/// - Phân quyền thao tác theo chi nhánh (Branch Access Control / RBAC).
/// - Bảo toàn quản lý tồn kho theo lô/hạn dùng (Batch & Expiry Consistency).
/// </summary>
public class TransferBusinessFlowIntegrationTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-retail-int-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string CurrentRawUserId => CurrentUserId.ToString();
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private AppDbContext CreateDbContext(string dbName, FakeTenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, tenantContext);
    }

    private (ITransferService service, AppDbContext context) CreateServiceScope(string dbName, FakeTenantContext tenantContext)
    {
        var context = CreateDbContext(dbName, tenantContext);
        var transfersRepo = new GenericRepository<Transfer>(context);
        var detailsRepo = new GenericRepository<TransferDetail>(context);
        var stocksRepo = new GenericRepository<BranchStock>(context);
        var productsRepo = new GenericRepository<Product>(context);
        var branchesRepo = new GenericRepository<Branch>(context);
        var historyRepo = new GenericRepository<TransferStatusHistory>(context);
        var uow = new UnitOfWork(context);

        var service = new TransferService(
            transfersRepo,
            detailsRepo,
            stocksRepo,
            productsRepo,
            branchesRepo,
            historyRepo,
            tenantContext,
            uow,
            new NotificationOutbox(new GenericRepository<OutboxMessage>(context)),
            packages: new GenericRepository<Package>(context),
            productUnits: new GenericRepository<ProductUnit>(context),
            transferRequests: new GenericRepository<TransferRequest>(context),
            transferRequestDetails: new GenericRepository<TransferRequestDetail>(context),
            transferRepository: new TransferRepository(context),
            branchProducts: new GenericRepository<BranchProduct>(context),
            userRepository: new UserRepository(context));

        return (service, context);
    }

    private async Task<(Guid branchAId, Guid branchBId, Guid prodAId, Guid prodBId)> SeedBaseDataAsync(AppDbContext context, string tenantId)
    {
        var branchAId = Guid.NewGuid();
        var branchBId = Guid.NewGuid();
        var prodAId = Guid.NewGuid();
        var prodBId = Guid.NewGuid();

        context.Branches.Add(new Branch
        {
            Id = branchAId,
            TenantId = tenantId,
            BranchCode = "CN-KHO-TONG",
            Name = "Kho Tổng Trung Tâm",
            Address = "Số 1 Đường Kho Vận",
            IsActive = true
        });

        context.Branches.Add(new Branch
        {
            Id = branchBId,
            TenantId = tenantId,
            BranchCode = "CN-BAN-LE-01",
            Name = "Chi nhánh Bán lẻ 1",
            Address = "Số 20 Đường Bán Lẻ",
            IsActive = true
        });

        context.Products.Add(new Product
        {
            Id = prodAId,
            TenantId = tenantId,
            ProductCode = "SP-BIA-HEINEKEN",
            Name = "Bia Heineken Sleek 330ml",
            BaseUnit = "Lon",
            CostPrice = 18_000m,
            BasePrice = 22_000m,
            HasBatches = true,
            Status = ProductStatus.Active
        });

        context.Products.Add(new Product
        {
            Id = prodBId,
            TenantId = tenantId,
            ProductCode = "SP-MI-OMACHI",
            Name = "Mì Omachi Sườn Hầm",
            BaseUnit = "Gói",
            CostPrice = 8_000m,
            BasePrice = 10_000m,
            HasBatches = false,
            Status = ProductStatus.Active
        });

        await context.SaveChangesAsync();
        return (branchAId, branchBId, prodAId, prodBId);
    }

    [Fact]
    public async Task CompleteTransferLifecycle_Draft_Dispatch_Receive_PersistsAndUpdatesStockInDatabase()
    {
        // ARRANGE
        var dbName = $"TransferDb_CompleteLifecycle_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);

        var (branchA, branchB, prodA, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        // Nạp tồn kho ban đầu tại Chi nhánh A (Kho Tổng): 100 lon bia, Lô L01
        var batchExpiry = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchA,
            ProductId = prodA,
            BatchCode = "L01",
            ExpiryDate = batchExpiry,
            QuantityOnHand = 100m,
            QuantityReserved = 0m,
            QuantityQuarantined = 0m,
            LandedCostPerUnit = 18_000m
        });
        await context.SaveChangesAsync();

        // ACT 1: Tạo phiếu Draft điều chuyển 30 lon bia từ CN A sang CN B
        var createRequest = new CreateTransferRequest(
            FromBranchId: branchA,
            ToBranchId: branchB,
            DispatchNow: false,
            ExpectedDeliveryDate: DateTime.UtcNow.AddDays(2),
            Note: "Điều chuyển bổ sung hàng cuối tuần",
            Items: new List<TransferLineRequest>
            {
                new(prodA, 30m, "Lon", "L01", batchExpiry, 18_000m)
            });

        var created = await service.CreateTransferAsync(createRequest, CancellationToken.None);

        // ASSERT 1: Kiểm tra trạng thái phiếu và kho trong DB
        Assert.NotNull(created);
        Assert.Equal("Draft", created.Status);
        Assert.Empty(await context.OutboxMessages.ToListAsync());
        Assert.Equal(540_000m, created.TotalAmount); // 30 * 18,000

        var stockInDbA = await context.BranchStocks.FirstOrDefaultAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.NotNull(stockInDbA);
        Assert.Equal(100m, stockInDbA.QuantityOnHand); // Tồn kho gửi chưa bị trừ

        // ACT 2: Xuất chuyển hàng (Dispatch)
        await service.SubmitTransferAsync(created.Id, CancellationToken.None);
        var dispatched = await service.DispatchTransferAsync(created.Id, CancellationToken.None);

        // ASSERT 2: Phiếu chuyển InTransit và tồn kho CN A bị trừ ngay trong DB
        Assert.Equal("InTransit", dispatched.Status);
        Assert.NotNull(dispatched.DispatchedAt);

        var stockAfterDispatchA = await context.BranchStocks.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.NotNull(stockAfterDispatchA);
        Assert.Equal(70m, stockAfterDispatchA.QuantityOnHand); // 100 - 30 = 70

        var stockAtBBeforeReceive = await context.BranchStocks.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branchB && s.ProductId == prodA);
        Assert.Null(stockAtBBeforeReceive); // Chi nhánh B chưa nhận nên chưa có tồn kho

        // ACT 3: Chi nhánh B kiểm đếm và xác nhận nhận đủ hàng (Receive)
        var lineItem = dispatched.Items.First();
        var receiveRequest = new ReceiveTransferRequest(
            Note: "Đã nhận đủ 30 lon bia nguyên vẹn",
            Items: new List<ReceiveTransferLineRequest>
            {
                new(lineItem.Id, ReceivedQuantity: 30m, DamagedQuantity: 0m, DiscrepancyReason: null)
            });

        var received = await service.ReceiveTransferAsync(created.Id, receiveRequest, CancellationToken.None);

        // ASSERT 3: Phiếu chuyển Received, tồn kho CN B được tạo mới và tăng đúng 30 lon
        Assert.Equal("Completed", received.Status);
        Assert.NotNull(received.ReceivedAt);

        var stockAtB = await context.BranchStocks.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branchB && s.ProductId == prodA && s.BatchCode == "L01");
        Assert.NotNull(stockAtB);
        Assert.Equal(30m, stockAtB.QuantityOnHand);
        Assert.Equal(0m, stockAtB.QuantityQuarantined);
        Assert.Equal(18_000m, stockAtB.LandedCostPerUnit);

        // Tổng tồn kho toàn hệ thống được bảo toàn: CN A (70) + CN B (30) = 100
        var totalStock = await context.BranchStocks.Where(s => s.ProductId == prodA).SumAsync(s => s.QuantityOnHand);
        Assert.Equal(100m, totalStock);
        var notifications = await context.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, message => Assert.Equal(NotificationOutbox.MessageType, message.Type));
        var pending = notifications.Select(message => JsonSerializer.Deserialize<BusinessNotification>(message.PayloadJson)!).ToList();
        Assert.Equal(branchA, Assert.Single(pending, item => item.Type == BusinessNotificationTypes.TransferDispatchRequired).StaffBranchId);
        Assert.Equal(branchB, Assert.Single(pending, item => item.Type == BusinessNotificationTypes.TransferReceiveRequired).StaffBranchId);
    }

    [Fact]
    public async Task ReceiveTransfer_WithDamagedGoods_PlacesDamagedQuantityIntoQuarantine()
    {
        // ARRANGE: Tạo và xuất chuyển ngay 40 gói mì Omachi từ CN A sang CN B
        var dbName = $"TransferDb_DamageQuarantine_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);

        var (branchA, branchB, _, prodB) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchA,
            ProductId = prodB,
            BatchCode = null,
            ExpiryDate = null,
            QuantityOnHand = 60m,
            QuantityReserved = 0m,
            QuantityQuarantined = 0m,
            LandedCostPerUnit = 8_000m
        });
        await context.SaveChangesAsync();

        var createRequest = new CreateTransferRequest(
            FromBranchId: branchA,
            ToBranchId: branchB,
            DispatchNow: true,
            ExpectedDeliveryDate: DateTime.UtcNow.AddDays(1),
            Note: "Xuất chuyển mì gói",
            Items: new List<TransferLineRequest>
            {
                new(prodB, 40m, "Gói", null, null, 8_000m)
            });

        var transfer = await service.CreateTransferAsync(createRequest, CancellationToken.None);
        await service.SubmitTransferAsync(transfer.Id, CancellationToken.None);
        transfer = await service.DispatchTransferAsync(transfer.Id, CancellationToken.None);
        Assert.Equal("InTransit", transfer.Status);

        // ACT: Chi nhánh B nhận hàng: 32 gói tốt, 8 gói bị rách nát do ngấm nước
        var lineItem = transfer.Items.First();
        var receiveRequest = new ReceiveTransferRequest(
            Note: "Kiểm đếm phát hiện 8 gói mì bị ngấm nước",
            Items: new List<ReceiveTransferLineRequest>
            {
                new(lineItem.Id, ReceivedQuantity: 32m, DamagedQuantity: 8m, DiscrepancyReason: "Thùng hàng bị ướt rách khi vận chuyển trời mưa")
            });

        var received = await service.ReceiveTransferAsync(transfer.Id, receiveRequest, CancellationToken.None);

        // ASSERT:
        Assert.Equal("Completed", received.Status);
        var receivedLine = received.Items.First();
        Assert.Equal(32m, receivedLine.ReceivedQuantity);
        Assert.Equal(8m, receivedLine.DamagedQuantity);

        // Kiểm tra tồn kho CN B trong Database:
        // Tổng QuantityOnHand = 40 (32 tốt + 8 hỏng), QuantityQuarantined = 8
        var stockB = await context.BranchStocks.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branchB && s.ProductId == prodB);
        Assert.NotNull(stockB);
        Assert.Equal(40m, stockB.QuantityOnHand);
        Assert.Equal(8m, stockB.QuantityQuarantined);

        // Tồn khả dụng thực tế bán lẻ của CN B chỉ là 32 (40 - 8)
        var availableAtB = stockB.QuantityOnHand - stockB.QuantityReserved - stockB.QuantityQuarantined;
        Assert.Equal(32m, availableAtB);
    }

    [Fact]
    public async Task CancelInTransitTransfer_ByStoreOwner_IsRejectedWithoutCreatingPhantomStock()
    {
        // ARRANGE: Xuất chuyển 50 lon bia
        var dbName = $"TransferDb_CancelRollback_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);

        var (branchA, branchB, prodA, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchA,
            ProductId = prodA,
            BatchCode = "L-ROLLBACK",
            ExpiryDate = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            QuantityOnHand = 80m,
            QuantityReserved = 0m,
            QuantityQuarantined = 0m,
            LandedCostPerUnit = 18_000m
        });
        await context.SaveChangesAsync();

        var createRequest = new CreateTransferRequest(
            FromBranchId: branchA,
            ToBranchId: branchB,
            DispatchNow: true,
            ExpectedDeliveryDate: null,
            Note: "Chuyển tạm thời",
            Items: new List<TransferLineRequest>
            {
                new(prodA, 50m, "Lon", "L-ROLLBACK", new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), 18_000m)
            });

        var transfer = await service.CreateTransferAsync(createRequest, CancellationToken.None);
        await service.SubmitTransferAsync(transfer.Id, CancellationToken.None);
        transfer = await service.DispatchTransferAsync(transfer.Id, CancellationToken.None);

        // Tồn kho nguồn sau khi xuất: 80 - 50 = 30
        var stockAfterDispatch = await context.BranchStocks.AsNoTracking().FirstAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.Equal(30m, stockAfterDispatch.QuantityOnHand);

        // ACT: Chủ shop hủy phiếu chuyển đang vận chuyển
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelTransferAsync(transfer.Id, new CancelTransferRequest("Hủy do chi nhánh nhận không đủ diện tích chứa hàng"), CancellationToken.None));

        // ASSERT: Phiếu bị Cancelled và Tồn kho nguồn được hoàn trả 100% về 80 trong DB
        var stockAfterCancel = await context.BranchStocks.AsNoTracking().FirstAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.Equal(30m, stockAfterCancel.QuantityOnHand);
    }

    [Fact]
    public async Task CancelInTransitTransfer_ByRegularStaff_ThrowsUnauthorizedAccessException()
    {
        // ARRANGE
        var dbName = $"TransferDb_CancelAuth_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);

        var (branchA, branchB, prodA, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchA,
            ProductId = prodA,
            BatchCode = null,
            ExpiryDate = null,
            QuantityOnHand = 100m,
            LandedCostPerUnit = 18_000m
        });
        await context.SaveChangesAsync();

        var transfer = await service.CreateTransferAsync(new CreateTransferRequest(branchA, branchB, true, null, null, new List<TransferLineRequest> { new(prodA, 10m, "Lon", null, null, 18_000m) }), CancellationToken.None);
        await service.SubmitTransferAsync(transfer.Id, CancellationToken.None);
        transfer = await service.DispatchTransferAsync(transfer.Id, CancellationToken.None);

        // Đổi quyền sang Nhân viên thường của chi nhánh gửi
        tenant.SetRole("STAFF");
        tenant.CurrentBranchId = branchA;

        // ACT & ASSERT: Nhân viên thường không được phép hủy phiếu đang vận chuyển
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelTransferAsync(transfer.Id, new CancelTransferRequest("Nhân viên tự hủy"), CancellationToken.None));
        Assert.Contains("trước khi xuất hàng", ex.Message);
    }

    [Fact]
    public async Task MultiTenantIsolation_CrossTenantAccess_StrictlyRejectedInDatabase()
    {
        // ARRANGE: Tạo dữ liệu trên 2 Tenant hoàn toàn tách biệt
        var dbName = $"TransferDb_MultiTenant_{Guid.NewGuid()}";
        var tenantA = new FakeTenantContext { CurrentTenantId = "tenant-ALPHA" };
        var (serviceA, context) = CreateServiceScope(dbName, tenantA);

        // Seed dữ liệu Tenant ALPHA
        var (branchA1, branchA2, prodA, _) = await SeedBaseDataAsync(context, "tenant-ALPHA");

        // Đổi sang Tenant BETA để seed đúng TenantId bảo mật
        tenantA.CurrentTenantId = "tenant-BETA";
        var (branchB1, branchB2, prodB, _) = await SeedBaseDataAsync(context, "tenant-BETA");

        // Đổi lại Tenant ALPHA để thực thi nghiệp vụ
        tenantA.CurrentTenantId = "tenant-ALPHA";

        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-ALPHA",
            BranchId = branchA1,
            ProductId = prodA,
            QuantityOnHand = 50m
        });
        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-BETA",
            BranchId = branchB1,
            ProductId = prodB,
            QuantityOnHand = 50m
        });
        await context.SaveChangesAsync();

        // ACT & ASSERT 1: Tenant ALPHA cố chuyển hàng tới chi nhánh của Tenant BETA (Global Query Filter ngăn chặn triệt để)
        var exBranch = await Assert.ThrowsAnyAsync<Exception>(() =>
            serviceA.CreateTransferAsync(new CreateTransferRequest(branchA1, branchB2, false, null, null, new List<TransferLineRequest> { new(prodA, 10m, "Lon", null, null, 18_000m) }), CancellationToken.None));
        Assert.True(exBranch is KeyNotFoundException or InvalidOperationException);

        // ACT & ASSERT 2: Tenant ALPHA cố chuyển sản phẩm thuộc sở hữu của Tenant BETA
        var exProd = await Assert.ThrowsAnyAsync<Exception>(() =>
            serviceA.CreateTransferAsync(new CreateTransferRequest(branchA1, branchA2, false, null, null, new List<TransferLineRequest> { new(prodB, 10m, "Gói", null, null, 10_000m) }), CancellationToken.None));
        Assert.True(exProd is KeyNotFoundException or InvalidOperationException);
    }

    [Fact]
    public async Task BranchAccessControl_StaffCannotReceiveGoodsDestinedForAnotherBranch()
    {
        // ARRANGE
        var dbName = $"TransferDb_BranchRBAC_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);

        var (branchA, branchB, prodA, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchA,
            ProductId = prodA,
            QuantityOnHand = 50m
        });
        await context.SaveChangesAsync();

        var transfer = await service.CreateTransferAsync(new CreateTransferRequest(branchA, branchB, true, null, null, new List<TransferLineRequest> { new(prodA, 10m, "Lon", null, null, 18_000m) }), CancellationToken.None);
        await service.SubmitTransferAsync(transfer.Id, CancellationToken.None);
        transfer = await service.DispatchTransferAsync(transfer.Id, CancellationToken.None);

        // Đổi quyền sang Nhân viên chỉ trực thuộc Chi nhánh A (kho gửi), không có thẩm quyền tại Chi nhánh B (kho nhận)
        tenant.SetRole("STAFF");
        tenant.CurrentBranchId = branchA;

        var line = transfer.Items.First();
        var receiveRequest = new ReceiveTransferRequest("Nhận thử trái phép", new List<ReceiveTransferLineRequest> { new(line.Id, 10m, 0m, null) });

        // ACT & ASSERT: Nhân viên CN A cố bấm nhận hàng thay cho CN B -> Chặn ngay lập tức
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReceiveTransferAsync(transfer.Id, receiveRequest, CancellationToken.None));

        Assert.Contains("Không có quyền thao tác tại chi nhánh này", ex.Message);
    }

    [Fact]
    public async Task UrgentRequest_AllocatesAcrossTwoSourceBranches_AndCreatesOnePendingTransferPerSource()
    {
        var dbName = $"TransferDb_MultiSource_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (transferService, context) = CreateServiceScope(dbName, tenant);
        var (sourceA, destination, productId, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);
        var sourceB = Guid.NewGuid();
        context.Branches.Add(new Branch { Id = sourceB, TenantId = tenant.CurrentTenantId, BranchCode = "CN-NGUON-B", Name = "Chi nhánh nguồn B", Address = "B", IsActive = true });
        context.BranchStocks.AddRange(
            new BranchStock { Id = Guid.NewGuid(), TenantId = tenant.CurrentTenantId, BranchId = sourceA, ProductId = productId, QuantityOnHand = 12m, LandedCostPerUnit = 18_000m },
            new BranchStock { Id = Guid.NewGuid(), TenantId = tenant.CurrentTenantId, BranchId = sourceB, ProductId = productId, QuantityOnHand = 8m, LandedCostPerUnit = 18_500m });
        await context.SaveChangesAsync();

        var requestService = new TransferRequestService(
            new GenericRepository<TransferRequest>(context),
            new GenericRepository<TransferRequestDetail>(context),
            new GenericRepository<Transfer>(context),
            new GenericRepository<TransferDetail>(context),
            new GenericRepository<TransferStatusHistory>(context),
            new GenericRepository<BranchStock>(context),
            new GenericRepository<Product>(context),
            new GenericRepository<Branch>(context),
            transferService,
            tenant,
            new UnitOfWork(context));

        var request = new CreateTransferRequestGroupRequest(destination, DateTime.UtcNow.AddHours(2), null,
            "Khách đang chờ", [new TransferDemandLineRequest(productId, 20m, "Lon")]);
        var preview = await requestService.PreviewAllocationAsync(request, CancellationToken.None);
        Assert.True(preview.IsFullyAllocated);
        Assert.Equal(2, preview.Sources.Count);
        Assert.Equal(20m, preview.Sources.Sum(x => x.Items.Sum(line => line.AllocatedQuantity)));

        var created = await requestService.CreateAndAllocateAsync(request, CancellationToken.None);
        Assert.Equal("Open", created.Status);
        Assert.Equal(2, created.Transfers.Count);
        Assert.All(created.Transfers, transfer => Assert.Equal("Pending", transfer.Status));
        Assert.Equal(20m, created.Transfers.SelectMany(x => x.Items).Sum(x => x.SentQuantity));

        var reservedStocks = context.BranchStocks.Where(x => x.ProductId == productId).ToList();
        Assert.Equal(20m, reservedStocks.Sum(x => x.QuantityReserved));

        var secondPreview = await requestService.PreviewAllocationAsync(request, CancellationToken.None);
        Assert.False(secondPreview.IsFullyAllocated);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            requestService.CreateAndAllocateAsync(request, CancellationToken.None));

        var firstTransfer = created.Transfers.First();
        await transferService.DispatchTransferAsync(firstTransfer.Id, CancellationToken.None);
        var dispatchedSourceStock = context.BranchStocks.Single(x => x.BranchId == firstTransfer.FromBranchId && x.ProductId == productId);
        Assert.Equal(0m, dispatchedSourceStock.QuantityReserved);
        Assert.Equal(0m, dispatchedSourceStock.QuantityOnHand);
    }

    [Fact]
    public async Task ReceiveTransfer_CreatesAndActivatesBranchProduct_AndPreventsDuplicateBranchStock_AndRequiresShortageReason()
    {
        var dbName = $"TransferDb_Bug041_061_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);
        var (branchA, branchB, prodA, prodB) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        // prodB already has an Inactive BranchProduct at branchB
        context.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = branchB,
            ProductId = prodB,
            Status = "Inactive"
        });
        context.BranchStocks.AddRange(
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.CurrentTenantId,
                BranchId = branchA,
                ProductId = prodA,
                BatchCode = "L-SAME",
                ExpiryDate = DateTime.UtcNow.AddMonths(6),
                QuantityOnHand = 50m,
                LandedCostPerUnit = 18_000m
            },
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.CurrentTenantId,
                BranchId = branchA,
                ProductId = prodB,
                BatchCode = "L-B",
                ExpiryDate = DateTime.UtcNow.AddMonths(6),
                QuantityOnHand = 20m,
                LandedCostPerUnit = 8_000m
            });
        await context.SaveChangesAsync();

        // Create atomic Pending transfer with 2 lines of the SAME product/batch + 1 line of prodB (BUG-057 & BUG-061)
        var created = await service.CreateTransferAsync(new CreateTransferRequest(
            FromBranchId: branchA,
            ToBranchId: branchB,
            DispatchNow: false,
            ExpectedDeliveryDate: null,
            Note: "Gửi ngay",
            Items:
            [
                new(prodA, 10m, "Lon", "L-SAME", null, 18_000m),
                new(prodA, 5m, "Lon", "L-SAME", null, 18_000m),
                new(prodB, 10m, "Gói", "L-B", null, 8_000m)
            ],
            SubmitImmediately: true), CancellationToken.None);

        Assert.Equal("Pending", created.Status);
        Assert.NotNull(created.SubmittedAt);
        Assert.Contains(created.History, h => h.Action == "Created");
        Assert.Contains(created.History, h => h.Action == "Submitted");

        var dispatched = await service.DispatchTransferAsync(created.Id, CancellationToken.None);

        // Shortage without damage and without reason must throw (BUG-061)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReceiveTransferAsync(dispatched.Id, new ReceiveTransferRequest(
                Note: null,
                Items:
                [
                    new(dispatched.Items[0].Id, 8m, 0m, null),
                    new(dispatched.Items[1].Id, 5m, 0m, null),
                    new(dispatched.Items[2].Id, 10m, 0m, null)
                ]), CancellationToken.None));

        // Receive with shortage reason succeeds
        var received = await service.ReceiveTransferAsync(dispatched.Id, new ReceiveTransferRequest(
            Note: "Kiểm nhận thực tế",
            Items:
            [
                new(dispatched.Items[0].Id, 8m, 0m, "Thiếu 2 lon khi giao"),
                new(dispatched.Items[1].Id, 5m, 0m, null),
                new(dispatched.Items[2].Id, 10m, 0m, null)
            ]), CancellationToken.None);

        Assert.Equal("Completed", received.Status);
        Assert.Equal("ReceivedWithDiscrepancy", received.CompletionResult);

        // Only 1 BranchStock row created for (branchB, prodA, L-SAME) with merged qty 8 + 5 = 13 (BUG-061)
        var destStocksA = await context.BranchStocks
            .Where(s => s.BranchId == branchB && s.ProductId == prodA && s.BatchCode == "L-SAME")
            .ToListAsync();
        Assert.Single(destStocksA);
        Assert.Equal(13m, destStocksA[0].QuantityOnHand);

        // BUG-041: BranchProduct auto-created Active for prodA and reactivated to Active for prodB
        var bpA = await context.BranchProducts.SingleOrDefaultAsync(bp => bp.BranchId == branchB && bp.ProductId == prodA);
        var bpB = await context.BranchProducts.SingleOrDefaultAsync(bp => bp.BranchId == branchB && bp.ProductId == prodB);
        Assert.NotNull(bpA);
        Assert.Equal("Active", bpA.Status);
        Assert.NotNull(bpB);
        Assert.Equal("Active", bpB.Status);
    }

    [Fact]
    public async Task ExpiredBatch_CreatorName_ReturnTransferNullReceivedAt_AndCompletionResultFilter()
    {
        var dbName = $"TransferDb_Bug046_056_060_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (service, context) = CreateServiceScope(dbName, tenant);
        var (branchA, branchB, prodA, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        context.Users.Add(new User
        {
            Id = tenant.CurrentRawUserId,
            FullName = "Nguyễn Văn Chủ Shop",
            UserName = "chushop",
            Email = "owner@test.vn"
        });
        context.BranchStocks.AddRange(
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.CurrentTenantId,
                BranchId = branchA,
                ProductId = prodA,
                BatchCode = "L-EXPIRED",
                ExpiryDate = DateTime.UtcNow.AddDays(-5),
                QuantityOnHand = 20m,
                LandedCostPerUnit = 18_000m
            },
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.CurrentTenantId,
                BranchId = branchA,
                ProductId = prodA,
                BatchCode = "L-VALID",
                ExpiryDate = DateTime.UtcNow.AddMonths(3),
                QuantityOnHand = 20m,
                LandedCostPerUnit = 18_000m
            });
        await context.SaveChangesAsync();

        // BUG-056: Expired batch throws explicit message
        var expiredEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateTransferAsync(new CreateTransferRequest(
                branchA, branchB, false, null, null,
                [new(prodA, 5m, "Lon", "L-EXPIRED", null, 18_000m)]), CancellationToken.None));
        Assert.Equal("Không thể điều chuyển lô hàng đã hết hạn sử dụng.", expiredEx.Message);

        // Valid transfer -> creator name resolved (BUG-060)
        var created = await service.CreateTransferAsync(new CreateTransferRequest(
            branchA, branchB, false, null, "Test hoàn trả",
            [new(prodA, 5m, "Lon", "L-VALID", null, 18_000m)],
            SubmitImmediately: true), CancellationToken.None);
        Assert.Equal("Nguyễn Văn Chủ Shop", created.CreatedBy);

        await service.DispatchTransferAsync(created.Id, CancellationToken.None);

        // BUG-046: ReturnTransfer leaves ReceivedAt null and filter CompletionResult=Returned finds it
        var returned = await service.ReturnTransferAsync(created.Id, new ReturnTransferRequest("Xe hỏng quay về"), CancellationToken.None);
        Assert.Equal("Completed", returned.Status);
        Assert.Equal("Returned", returned.CompletionResult);
        Assert.Null(returned.ReceivedAt);

        var filtered = await service.GetTransfersAsync(
            new TransferFilterParams(null, null, null, null, null, null, 1, 20, null, null, null, TransferCompletionResult.Returned),
            CancellationToken.None);
        Assert.Single(filtered.Items);
        Assert.Equal(created.Id, filtered.Items[0].Id);
        Assert.Equal("Nguyễn Văn Chủ Shop", filtered.Items[0].CreatedBy);
    }

    [Fact]
    public async Task UrgentRequest_AvailableProducts_OrderCode_UnitConversion_AndLifecycleSync()
    {
        var dbName = $"TransferDb_Bug057_062_063_{Guid.NewGuid()}";
        var tenant = new FakeTenantContext();
        var (transferService, context) = CreateServiceScope(dbName, tenant);
        var (sourceA, destination, prodA, _) = await SeedBaseDataAsync(context, tenant.CurrentTenantId);

        // Add ProductUnit: 1 Thùng = 24 Lon
        context.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            ProductId = prodA,
            UnitName = "Thùng",
            ConversionRate = 24m
        });
        var orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenant.CurrentTenantId,
            BranchId = destination,
            OrderCode = "HD261125001"
        });
        context.BranchStocks.AddRange(
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.CurrentTenantId,
                BranchId = sourceA,
                ProductId = prodA,
                BatchCode = "L-OK",
                ExpiryDate = DateTime.UtcNow.AddMonths(4),
                QuantityOnHand = 50m,
                QuantityReserved = 2m,
                LandedCostPerUnit = 18_000m
            },
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.CurrentTenantId,
                BranchId = sourceA,
                ProductId = prodA,
                BatchCode = "L-EXP",
                ExpiryDate = DateTime.UtcNow.AddDays(-1),
                QuantityOnHand = 100m,
                LandedCostPerUnit = 18_000m
            });
        await context.SaveChangesAsync();

        var requestService = new TransferRequestService(
            new GenericRepository<TransferRequest>(context),
            new GenericRepository<TransferRequestDetail>(context),
            new GenericRepository<Transfer>(context),
            new GenericRepository<TransferDetail>(context),
            new GenericRepository<TransferStatusHistory>(context),
            new GenericRepository<BranchStock>(context),
            new GenericRepository<Product>(context),
            new GenericRepository<Branch>(context),
            transferService,
            tenant,
            new UnitOfWork(context),
            packages: new GenericRepository<Package>(context),
            productUnits: new GenericRepository<ProductUnit>(context),
            orders: new GenericRepository<Order>(context),
            transferRepository: new TransferRepository(context));

        // BUG-062: Available products excludes expired stock and returns 50 - 2 = 48
        var available = await requestService.GetAvailableProductsAsync(destination, CancellationToken.None);
        var prodAvail = Assert.Single(available);
        Assert.Equal(prodA, prodAvail.ProductId);
        Assert.Equal(48m, prodAvail.TotalAvailableQuantity);

        // BUG-062: Unknown order code throws 422 (InvalidOperationException)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            requestService.CreateAndAllocateAsync(new CreateTransferRequestGroupRequest(
                destination, null, null, "Khẩn",
                [new TransferDemandLineRequest(prodA, 1m, "Thùng")],
                RelatedOrderCode: "HD-KHONG-TON-TAI"), CancellationToken.None));

        // BUG-057 & BUG-062: 1 Thùng -> 24 Lon reserved & OrderCode resolved
        var group = await requestService.CreateAndAllocateAsync(new CreateTransferRequestGroupRequest(
            destination, null, null, "Khẩn",
            [new TransferDemandLineRequest(prodA, 1m, "Thùng")],
            RelatedOrderCode: "HD261125001"), CancellationToken.None);

        Assert.Equal(orderId, group.RelatedOrderId);
        Assert.Equal(24m, group.Items[0].RequestedQuantity);
        Assert.Equal("Lon", group.Items[0].Unit);
        var childTransfer = Assert.Single(group.Transfers);
        Assert.Equal(24m, childTransfer.Items[0].SentQuantity);

        // BUG-063: Cancel child transfer syncs TransferRequest -> Cancelled
        await transferService.CancelTransferAsync(childTransfer.Id, new CancelTransferRequest("Hết nhu cầu"), CancellationToken.None);
        var updatedGroup = await requestService.GetAsync(group.Id, CancellationToken.None);
        Assert.Equal("Cancelled", updatedGroup.Status);

        // BUG-063: Return child transfer syncs TransferRequest -> Cancelled
        var groupForReturn = await requestService.CreateAndAllocateAsync(new CreateTransferRequestGroupRequest(
            destination, null, null, "Khẩn hoàn trả",
            [new TransferDemandLineRequest(prodA, 10m, "Lon")]), CancellationToken.None);
        var returnTransfer = Assert.Single(groupForReturn.Transfers);
        await transferService.DispatchTransferAsync(returnTransfer.Id, CancellationToken.None);
        await transferService.ReturnTransferAsync(returnTransfer.Id, new ReturnTransferRequest("Quay đầu kho nguồn"), CancellationToken.None);
        var afterReturn = await requestService.GetAsync(groupForReturn.Id, CancellationToken.None);
        Assert.Equal("Cancelled", afterReturn.Status);

        // BUG-063: Receive with shortage syncs TransferRequest -> Completed (no longer stuck Open)
        var groupForShortage = await requestService.CreateAndAllocateAsync(new CreateTransferRequestGroupRequest(
            destination, null, null, "Khẩn thiếu hàng",
            [new TransferDemandLineRequest(prodA, 10m, "Lon")]), CancellationToken.None);
        var shortageTransfer = Assert.Single(groupForShortage.Transfers);
        await transferService.DispatchTransferAsync(shortageTransfer.Id, CancellationToken.None);
        await transferService.ReceiveTransferAsync(shortageTransfer.Id, new ReceiveTransferRequest(
            Note: "Thiếu 2 lon",
            Items: [new ReceiveTransferLineRequest(shortageTransfer.Items[0].Id, 8m, 0m, "Thiếu 2 lon khi giao")]), CancellationToken.None);
        var afterShortage = await requestService.GetAsync(groupForShortage.Id, CancellationToken.None);
        Assert.Equal("Completed", afterShortage.Status);
    }
}
