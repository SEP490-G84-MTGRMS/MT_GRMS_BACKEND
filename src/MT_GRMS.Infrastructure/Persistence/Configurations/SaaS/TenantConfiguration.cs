using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.SaaS;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("TenantID").HasMaxLength(50);

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Subdomain).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.TaxCode).HasMaxLength(50);
        builder.Property(x => x.Hotline).HasMaxLength(20);
        builder.Property(x => x.Address).HasMaxLength(255);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("ACTIVE");
        builder.Property(x => x.CurrentPlan).HasMaxLength(50);
        builder.Property(x => x.LockType).HasMaxLength(50);
        builder.Property(x => x.LockReason).HasMaxLength(500);
        builder.Property(x => x.LockedBy).HasMaxLength(100);

        builder.HasIndex(x => x.Subdomain).IsUnique();
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasOne(x => x.SubscriptionPlan)
            .WithMany(p => p.Tenants)
            .HasPrincipalKey(p => p.PlanCode)
            .HasForeignKey(x => x.CurrentPlan)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
