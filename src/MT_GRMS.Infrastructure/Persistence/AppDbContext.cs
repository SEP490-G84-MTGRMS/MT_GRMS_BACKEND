using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// DbContext chuẩn cấu trúc Database-First (hỗ trợ Scaffold-DbContext và DI)
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

    // Các DbSet tương ứng với các bảng trong Database
    public virtual DbSet<Order> Orders { get; set; } = null!;
    public virtual DbSet<OrderItem> OrderItems { get; set; } = null!;
    public virtual DbSet<User> Users { get; set; } = null!;
    public virtual DbSet<Role> Roles { get; set; } = null!;
    public virtual DbSet<Product> Products { get; set; } = null!;
    public virtual DbSet<Category> Categories { get; set; } = null!;
    public virtual DbSet<Batch> Batches { get; set; } = null!;
    public virtual DbSet<StockOnHand> StockOnHands { get; set; } = null!;
    public virtual DbSet<CashbookTransaction> CashbookTransactions { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Cho phép Scaffold-DbContext hoặc chạy console mà không cần qua Web Host DI
        if (!optionsBuilder.IsConfigured)
        {
            // Fallback connection string (khi chạy công cụ Scaffold của EF Core)
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Tự động quét và nạp toàn bộ cấu hình Fluent API Code-First trong assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // 2. Áp dụng Global Query Filter cho đa người thuê (Multi-Tenancy) & Soft-Delete
        ConfigureTenantFilters(modelBuilder);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    private void ConfigureTenantFilters(ModelBuilder modelBuilder)
    {
        // Đánh giá động ở runtime: EF Core tự động chèn TenantId vào parameter câu truy vấn SQL
        modelBuilder.Entity<Order>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<OrderItem>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<User>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<Role>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<Product>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<Category>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<Batch>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<StockOnHand>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
        modelBuilder.Entity<CashbookTransaction>().HasQueryFilter(e => !e.IsDeleted && (_tenantContext == null || string.IsNullOrEmpty(_tenantContext.CurrentTenantId) || e.TenantId == _tenantContext.CurrentTenantId));
    }
}
