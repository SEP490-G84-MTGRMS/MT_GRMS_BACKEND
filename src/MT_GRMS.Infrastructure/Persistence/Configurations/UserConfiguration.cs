using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Identity;

namespace MT_GRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình Code-First Fluent API cho bảng Users và các cột Security PIN PBKDF2
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("UserID").HasMaxLength(50);

        builder.Property(x => x.UserName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.AvatarUrl).HasMaxLength(255);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsEmailVerified).HasDefaultValue(false);
        builder.Property(x => x.MustChangePassword).HasDefaultValue(false);
        builder.Property(x => x.LastLoginIp).HasMaxLength(45);
        builder.Property(x => x.SecurityStamp).HasMaxLength(100);
        builder.Property(x => x.PasswordResetOtp).HasMaxLength(20);
        builder.Property(x => x.RefreshToken).HasMaxLength(255);
        builder.Property(x => x.AccessFailedCount).HasDefaultValue(0);

        builder.HasIndex(x => x.UserName).IsUnique();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.Phone).IsUnique();

        // Cấu hình các cột Security PIN phục vụ ký số và bảo mật chốt kỳ
        builder.Property(x => x.SecurityPinHash)
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.SecurityPinSalt)
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.FailedPinAttempts)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.PinLockedUntilUtc)
            .IsRequired(false);
    }
}
