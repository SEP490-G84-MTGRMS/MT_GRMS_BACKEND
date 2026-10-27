using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Identity;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Identity;

public class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.ToTable("TenantUsers");

        // Composite Key: TenantID, UserID
        builder.HasKey(x => new { x.TenantId, x.UserId });

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.UserId).HasColumnName("UserID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(50).HasDefaultValue("STAFF");
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.Tenant)
            .WithMany(t => t.TenantUsers)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(u => u.TenantUsers)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
