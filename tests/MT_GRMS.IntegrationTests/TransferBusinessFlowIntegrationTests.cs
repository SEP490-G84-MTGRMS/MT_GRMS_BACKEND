using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
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
        var uow = new UnitOfWork(context);

        var service = new TransferService(
            transfersRepo,
            detailsRepo,
            stocksRepo,
            productsRepo,
            branchesRepo,
            tenantContext,
            uow);

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
        Assert.Equal(540_000m, created.TotalAmount); // 30 * 18,000

        var stockInDbA = await context.BranchStocks.FirstOrDefaultAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.NotNull(stockInDbA);
        Assert.Equal(100m, stockInDbA.QuantityOnHand); // Tồn kho gửi chưa bị trừ

        // ACT 2: Xuất chuyển hàng (Dispatch)
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
        Assert.Equal("Received", received.Status);
        Assert.NotNull(received.ReceivedAt);

        var stockAtB = await context.BranchStocks.AsNoTracking().FirstOrDefaultAsync(s => s.BranchId == branchB && s.ProductId == prodA && s.BatchCode == "L01");
        Assert.NotNull(stockAtB);
        Assert.Equal(30m, stockAtB.QuantityOnHand);
        Assert.Equal(0m, stockAtB.QuantityQuarantined);
        Assert.Equal(18_000m, stockAtB.LandedCostPerUnit);

        // Tổng tồn kho toàn hệ thống được bảo toàn: CN A (70) + CN B (30) = 100
        var totalStock = await context.BranchStocks.Where(s => s.ProductId == prodA).SumAsync(s => s.QuantityOnHand);
        Assert.Equal(100m, totalStock);
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
        Assert.Equal("Received", received.Status);
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
    public async Task CancelInTransitTransfer_ByStoreOwner_RollsBackStockToSourceBranchImmediately()
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

        // Tồn kho nguồn sau khi xuất: 80 - 50 = 30
        var stockAfterDispatch = await context.BranchStocks.AsNoTracking().FirstAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.Equal(30m, stockAfterDispatch.QuantityOnHand);

        // ACT: Chủ shop hủy phiếu chuyển đang vận chuyển
        var cancelResult = await service.CancelTransferAsync(transfer.Id, new CancelTransferRequest("Hủy do chi nhánh nhận không đủ diện tích chứa hàng"), CancellationToken.None);

        // ASSERT: Phiếu bị Cancelled và Tồn kho nguồn được hoàn trả 100% về 80 trong DB
        Assert.Equal("Cancelled", cancelResult.Status);
        Assert.Equal("Hủy do chi nhánh nhận không đủ diện tích chứa hàng", cancelResult.CancelReason);

        var stockAfterCancel = await context.BranchStocks.AsNoTracking().FirstAsync(s => s.BranchId == branchA && s.ProductId == prodA);
        Assert.Equal(80m, stockAfterCancel.QuantityOnHand); // Tự động phục hồi tồn ban đầu
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

        // Đổi quyền sang Nhân viên thường của chi nhánh gửi
        tenant.SetRole("STAFF");
        tenant.CurrentBranchId = branchA;

        // ACT & ASSERT: Nhân viên thường không được phép hủy phiếu đang vận chuyển
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CancelTransferAsync(transfer.Id, new CancelTransferRequest("Nhân viên tự hủy"), CancellationToken.None));

        Assert.Contains("Chỉ chủ cửa hàng hoặc quản trị viên", ex.Message);
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
}
