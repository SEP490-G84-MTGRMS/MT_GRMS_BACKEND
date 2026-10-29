using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.System;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("LogID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50);
        builder.Property(x => x.ActorId).HasColumnName("ActorID").HasMaxLength(50);
        builder.Property(x => x.ActorName).HasMaxLength(100);
        builder.Property(x => x.ActorRole).HasMaxLength(50);
        builder.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TargetEntity).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("TargetID").HasMaxLength(50);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.UserAgent).HasMaxLength(255);

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Actor)
            .WithMany()
            .HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
