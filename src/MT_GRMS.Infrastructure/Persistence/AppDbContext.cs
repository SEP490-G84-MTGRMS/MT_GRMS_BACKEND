using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Infrastructure.Persistence.Configurations;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// DbContext quản lý kết nối cơ sở dữ liệu MT-GRMS:
/// Khởi tạo trước duy nhất bảng users cho tính năng xác thực đăng nhập.
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

    // Bảng User duy nhất phục vụ chức năng đăng nhập
    public virtual DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Áp dụng cấu hình bảng users (Id string Uid, Username unique, Password)
        modelBuilder.ApplyConfiguration(new UserConfiguration());

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
