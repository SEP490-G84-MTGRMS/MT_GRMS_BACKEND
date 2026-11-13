using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// AppDbContext trung tâm quản lý toàn bộ 34+ bảng thực thể của MT_GRMS
/// </summary>
public partial class AppDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // 1. Phân hệ SaaS & Multi-Tenancy (6 bảng)
    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; } = null!;
    public virtual DbSet<Tenant> Tenants { get; set; } = null!;
    public virtual DbSet<User> Users { get; set; } = null!;
    public virtual DbSet<TenantUser> TenantUsers { get; set; } = null!;
    public virtual DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public virtual DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;
    public virtual DbSet<WalletTopupOrder> WalletTopupOrders { get; set; } = null!;

    // 2. Phân hệ Catalog & Hàng hóa
    public virtual DbSet<Category> Categories { get; set; } = null!;
    public virtual DbSet<Product> Products { get; set; } = null!;
    public virtual DbSet<ProductUnit> ProductUnits { get; set; } = null!;
    public virtual DbSet<BranchProduct> BranchProducts { get; set; } = null!;
    public virtual DbSet<BranchProductUnitPrice> BranchProductUnitPrices { get; set; } = null!;
    public virtual DbSet<ProductPriceHistory> ProductPriceHistories { get; set; } = null!;

    // 3. Phân hệ Chi nhánh, Kho & Điều chuyển (tồn theo lô)
    public virtual DbSet<Branch> Branches { get; set; } = null!;
    public virtual DbSet<BranchStock> BranchStocks { get; set; } = null!;
    public virtual DbSet<Transfer> Transfers { get; set; } = null!;
    public virtual DbSet<TransferDetail> TransferDetails { get; set; } = null!;
    public virtual DbSet<Batch> Batches { get; set; } = null!;
    public virtual DbSet<StockOnHand> StockOnHands { get; set; } = null!;
    public virtual DbSet<Stocktake> Stocktakes { get; set; } = null!;
    public virtual DbSet<StocktakeLine> StocktakeLines { get; set; } = null!;
    public virtual DbSet<RepackOrder> RepackOrders { get; set; } = null!;
    public virtual DbSet<Package> Packages { get; set; } = null!;

    // 4. Phân hệ Nhà cung cấp, Mua hàng PO & Nhập kho GRN
    public virtual DbSet<Domain.Entities.Supplier.Supplier> Suppliers { get; set; } = null!;
    public virtual DbSet<SupplierBlackoutDate> SupplierBlackoutDates { get; set; } = null!;
    public virtual DbSet<SupplierAPLedger> SupplierAPLedgers { get; set; } = null!;
    public virtual DbSet<SupplierProduct> SupplierProducts { get; set; } = null!;
    public virtual DbSet<SupplierBranchAssignment> SupplierBranchAssignments { get; set; } = null!;
    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public virtual DbSet<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = null!;
    public virtual DbSet<PurchaseOrderRevision> PurchaseOrderRevisions { get; set; } = null!;
    public virtual DbSet<SupplierDispatchPackage> SupplierDispatchPackages { get; set; } = null!;
    public virtual DbSet<SupplierDispatchPackageItem> SupplierDispatchPackageItems { get; set; } = null!;
    public virtual DbSet<GoodsReceipt> GoodsReceipts { get; set; } = null!;
    public virtual DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; } = null!;
    public virtual DbSet<SupplierReturn> SupplierReturns { get; set; } = null!;
    public virtual DbSet<SupplierReturnLine> SupplierReturnLines { get; set; } = null!;
    public virtual DbSet<SupplierReturnPolicy> SupplierReturnPolicies { get; set; } = null!;
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    // 5. Phân hệ Khách hàng, Khuyến mãi & Bán lẻ POS (4 bảng)
    public virtual DbSet<Customer> Customers { get; set; } = null!;
    public virtual DbSet<Promotion> Promotions { get; set; } = null!;
    public virtual DbSet<PromotionCondition> PromotionConditions { get; set; } = null!;
    public virtual DbSet<VoucherCode> VoucherCodes { get; set; } = null!;
    public virtual DbSet<Domain.Entities.Sales.Order> Orders { get; set; } = null!;
    public virtual DbSet<OrderDetail> OrderDetails { get; set; } = null!;
    public virtual DbSet<LoyaltySetting> LoyaltySettings { get; set; } = null!;

    // 6. Phân hệ Tài chính, Sổ quỹ & Chi phí (FE-07, FE-17) - Đã hợp nhất FinanceAccounts
    public virtual DbSet<CashbookTransaction> CashbookTransactions { get; set; } = null!;
    public virtual DbSet<CashbookEntry> CashbookEntries { get; set; } = null!;
    public virtual DbSet<FinanceAccount> FinanceAccounts { get; set; } = null!;
    public virtual DbSet<CashInTransit> CashInTransits { get; set; } = null!;
    public virtual DbSet<ShiftZReport> ShiftZReports { get; set; } = null!;
    public virtual DbSet<ShiftCashMovement> ShiftCashMovements { get; set; } = null!;
    public virtual DbSet<ShiftParticipant> ShiftParticipants { get; set; } = null!;
    public virtual DbSet<ShiftCloseConfirmation> ShiftCloseConfirmations { get; set; } = null!;
    public virtual DbSet<ExpenseVoucher> ExpenseVouchers { get; set; } = null!;
    public virtual DbSet<StorePaymentConfig> StorePaymentConfigs { get; set; } = null!;
    public virtual DbSet<PaymentTransaction> PaymentTransactions { get; set; } = null!;
    public virtual DbSet<IdempotencyRecord> IdempotencyRecords { get; set; } = null!;

    // 7. Customer Debt Module (FE-08) - Đã hợp nhất công nợ vào Customers
    public virtual DbSet<CustomerDebtInvoice> CustomerDebtInvoices { get; set; } = null!;
    public virtual DbSet<CustomerDebtEntry> CustomerDebtEntries { get; set; } = null!;
    public virtual DbSet<CustomerDebtAllocation> CustomerDebtAllocations { get; set; } = null!;
    // 8. Household Tax Module (FE-19 TT 152/2025/TT-BTC)
    public virtual DbSet<HouseholdTaxSnapshot> HouseholdTaxSnapshots { get; set; } = null!;
    public virtual DbSet<HouseholdTaxYearSetting> HouseholdTaxYearSettings { get; set; } = null!;

    // 9. Phân hệ Nhân sự, Ca kíp & Tiền lương (4 bảng)
    public virtual DbSet<Employee> Employees { get; set; } = null!;
    public virtual DbSet<Payslip> Payslips { get; set; } = null!;
    public virtual DbSet<EmployeeSchedule> EmployeeSchedules { get; set; } = null!;
    public virtual DbSet<ScheduleApprovalRequest> ScheduleApprovalRequests { get; set; } = null!;
    public virtual DbSet<EmployeeAllowance> EmployeeAllowances { get; set; } = null!;
    public virtual DbSet<ShiftConfig> ShiftConfigs { get; set; } = null!;
    public virtual DbSet<BranchAttendancePolicy> BranchAttendancePolicies { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tự động nạp toàn bộ Configurations trong assembly Infrastructure
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Tắt toàn bộ Cascade Delete ở mức Database để tránh lỗi Multiple Cascade Paths (Error 1785) của SQL Server
        // Dự án áp dụng Soft Delete (IsDeleted) toàn diện nên an toàn 100%
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            if (foreignKey.DeclaringEntityType.ClrType == typeof(EmployeeAllowance))
            {
                continue;
            }
            if (foreignKey.DeleteBehavior == DeleteBehavior.Cascade)
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        // Áp dụng Global Query Filter cho toàn bộ thực thể kế thừa BaseTenantEntity
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseTenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)?
                    .MakeGenericMethod(entityType.ClrType);
                method?.Invoke(this, [modelBuilder]);
            }
        }

        OnModelCreatingPartial(modelBuilder);
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : BaseTenantEntity
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            !e.IsDeleted &&
            e.TenantId == (_tenantContext != null && !string.IsNullOrEmpty(_tenantContext.CurrentTenantId)
                ? _tenantContext.CurrentTenantId
                : "__FAIL_CLOSED_NO_ACTIVE_TENANT__"));
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantInfo();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantInfo();
        return base.SaveChanges();
    }

    private void ApplyAuditAndTenantInfo()
    {
        var tenantId = _tenantContext?.CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries<BaseTenantEntity>())
        {
            if (entry.Entity is CashbookEntry || entry.Entity is CustomerDebtEntry || entry.Entity is HouseholdTaxSnapshot)
            {
                if (entry.State == EntityState.Deleted)
                {
                    throw new InvalidOperationException($"Cấm xóa thực thể sổ cái tài chính bất biến: {entry.Entity.GetType().Name}. Vui lòng sử dụng bút toán đảo.");
                }
                if (entry.Entity.IsDeleted)
                {
                    throw new InvalidOperationException($"Cấm soft-delete thực thể sổ cái tài chính bất biến: {entry.Entity.GetType().Name}. Vui lòng sử dụng bút toán đảo.");
                }
                if (entry.State == EntityState.Modified)
                {
                    throw new InvalidOperationException($"Cấm chỉnh sửa (Modified) thực thể sổ cái tài chính bất biến: {entry.Entity.GetType().Name}. Sổ cái chỉ hỗ trợ cơ chế append-only và điều chỉnh qua bút toán đảo.");
                }
            }

            if (entry.State == EntityState.Added)
            {
                // INV-1: Gán TenantId từ ngữ cảnh xác thực, ngăn chặn client ghi đè TenantId (TEN-02)
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    entry.Entity.TenantId = tenantId;
                }
                else if (string.IsNullOrWhiteSpace(entry.Entity.TenantId))
                {
                    entry.Entity.TenantId = "default-tenant";
                }
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
                if (entry.Entity is BranchStock branchStock)
                {
                    // CONC-03: Optimistic Concurrency token increment for BranchStock
                    branchStock.RowVersion++;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
