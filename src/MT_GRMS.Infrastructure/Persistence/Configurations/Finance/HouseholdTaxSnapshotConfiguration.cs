using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class HouseholdTaxSnapshotConfiguration : IEntityTypeConfiguration<HouseholdTaxSnapshot>
{
    public void Configure(EntityTypeBuilder<HouseholdTaxSnapshot> builder)
    {
        builder.ToTable("HouseholdTaxSnapshots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AnnualAccumulatedRevenue)
            .HasPrecision(18, 2);

        builder.Property(x => x.TaxableRevenueInPeriod)
            .HasPrecision(18, 2);

        builder.Property(x => x.VatRate)
            .HasPrecision(5, 2);

        builder.Property(x => x.VatAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.PitRate)
            .HasPrecision(5, 2);

        builder.Property(x => x.PitAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.TotalTaxAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.FinalizedBy)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.AuditSignatureHash)
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Notes)
            .HasMaxLength(500);

        builder.Property(x => x.BookDataJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.PeriodType, x.Year, x.PeriodNumber })
            .IsUnique();
    }
}

