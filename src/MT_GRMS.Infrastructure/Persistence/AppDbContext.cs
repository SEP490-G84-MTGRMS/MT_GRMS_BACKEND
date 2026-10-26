using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Entities.Finance;


namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// DbContext quản lý kết nối cơ sở dữ liệu MT-GRMS:
/// Hỗ trợ Multi-tenancy Isolation và Soft Delete tự động qua Global Query Filter.
/// </summary>
public partial class AppDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Identity
    public virtual DbSet<User> Users { get; set; } = null!;

    // Catalog Module
    public virtual DbSet<Product> Products { get; set; } = null!;
    public virtual DbSet<Domain.Entities.Order> Orders { get; set; } = null!;
    public virtual DbSet<Category> Categories { get; set; } = null!;

    // Supplier Module
    public virtual DbSet<Domain.Entities.Supplier.Supplier> Suppliers { get; set; } = null!;
    public virtual DbSet<SupplierCategory> SupplierCategories { get; set; } = null!;
    public virtual DbSet<SupplierAPLedger> SupplierAPLedgers { get; set; } = null!;
    public virtual DbSet<SupplierDeliveryConfig> SupplierDeliveryConfigs { get; set; } = null!;
    public virtual DbSet<SupplierBlackoutDate> SupplierBlackoutDates { get; set; } = null!;
    public virtual DbSet<SupplierProduct> SupplierProducts { get; set; } = null!;

    // Finance Module (FE-07, FE-17)
    public virtual DbSet<CashbookEntry> CashbookEntries { get; set; } = null!;
    public virtual DbSet<BranchCashTill> BranchCashTills { get; set; } = null!;
    public virtual DbSet<BranchSafe> BranchSafes { get; set; } = null!;
    public virtual DbSet<CashInTransit> CashInTransits { get; set; } = null!;
    public virtual DbSet<ShiftZReport> ShiftZReports { get; set; } = null!;
    public virtual DbSet<ExpenseVoucher> ExpenseVouchers { get; set; } = null!;
    public virtual DbSet<TenantBankAccount> TenantBankAccounts { get; set; } = null!;
    public virtual DbSet<IdempotencyRecord> IdempotencyRecords { get; set; } = null!;

    // Customer Debt Module (FE-08)
    public virtual DbSet<CustomerDebtProfile> CustomerDebtProfiles { get; set; } = null!;
    public virtual DbSet<CustomerDebtInvoice> CustomerDebtInvoices { get; set; } = null!;
    public virtual DbSet<CustomerDebtEntry> CustomerDebtEntries { get; set; } = null!;
    public virtual DbSet<CustomerDebtAllocation> CustomerDebtAllocations { get; set; } = null!;

    // Household Tax Module (FE-19 TT 152/2025/TT-BTC)
    public virtual DbSet<HouseholdTaxSnapshot> HouseholdTaxSnapshots { get; set; } = null!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tự động nạp toàn bộ Configurations trong assembly Infrastructure
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

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
            (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
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
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = "default-tenant";
        }

        foreach (var entry in ChangeTracker.Entries<BaseTenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (string.IsNullOrWhiteSpace(entry.Entity.TenantId))
                {
                    entry.Entity.TenantId = tenantId;
                }
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



