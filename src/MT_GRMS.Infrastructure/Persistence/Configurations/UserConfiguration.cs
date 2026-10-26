using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Identity;

namespace MT_GRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình Code-First Fluent API cho bảng users:
/// - Khóa chính Id kiểu string Uid
/// - Username có Unique Index
/// - Password lưu mật khẩu hash
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // 1. Tên bảng Database
        builder.ToTable("users");

        // 2. Khóa chính Id (string Uid)
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasMaxLength(64)
            .IsRequired();

        // 3. Cột Username & Password
        builder.Property(x => x.Username)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Password)
            .HasMaxLength(255)
            .IsRequired();

        // 4. Chỉ mục Unique trên Username chống trùng lặp
        builder.HasIndex(x => x.Username)
            .IsUnique();
    }
}
