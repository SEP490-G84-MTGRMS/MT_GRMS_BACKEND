using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.System;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RecipientUserId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ActionUrl).HasMaxLength(300);
        builder.Property(x => x.IsRead).HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.IsRead, x.CreatedAt });
    }
}
