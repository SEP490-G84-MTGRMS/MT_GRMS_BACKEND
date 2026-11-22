using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public sealed class TaxFilingConfirmationConfiguration : IEntityTypeConfiguration<TaxFilingConfirmation>
{
    public void Configure(EntityTypeBuilder<TaxFilingConfirmation> builder)
    {
        builder.ToTable("TaxFilingConfirmations");
        builder.HasKey(row => row.SnapshotId);
        builder.Property(row => row.SnapshotId).ValueGeneratedNever();
        builder.Property(row => row.TenantId).HasMaxLength(50);
        builder.Property(row => row.ConfirmedBy).HasMaxLength(64);
        builder.HasOne<HouseholdTaxSnapshot>().WithOne().HasForeignKey<TaxFilingConfirmation>(row => row.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
