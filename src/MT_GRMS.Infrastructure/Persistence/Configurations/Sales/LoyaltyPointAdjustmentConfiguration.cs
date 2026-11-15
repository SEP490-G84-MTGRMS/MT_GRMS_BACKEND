using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class LoyaltyPointAdjustmentConfiguration : IEntityTypeConfiguration<LoyaltyPointAdjustment>
{
    public void Configure(EntityTypeBuilder<LoyaltyPointAdjustment> builder)
    {
        builder.ToTable("LoyaltyPointAdjustments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TransactionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PerformedBy).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TransactionCode }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CustomerId, x.CreatedAt });
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
