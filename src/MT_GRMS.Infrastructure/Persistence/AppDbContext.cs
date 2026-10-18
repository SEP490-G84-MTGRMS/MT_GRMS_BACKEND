using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Supplier;

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

    // Supplier Module
    public virtual DbSet<Domain.Entities.Supplier.Supplier> Suppliers { get; set; } = null!;
    public virtual DbSet<SupplierCategory> SupplierCategories { get; set; } = null!;
    public virtual DbSet<SupplierAPLedger> SupplierAPLedgers { get; set; } = null!;
    public virtual DbSet<SupplierDeliveryConfig> SupplierDeliveryConfigs { get; set; } = null!;
    public virtual DbSet<SupplierBlackoutDate> SupplierBlackoutDates { get; set; } = null!;

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

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
