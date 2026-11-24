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
            .HasColumnName("AuditSignature")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(x => x.Notes)
            .HasMaxLength(500);

        builder.Property(x => x.BookDataJson)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.TaxPolicyJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .HasDefaultValue(MT_GRMS.Domain.Enums.TaxSnapshotStatus.Finalized)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.RevisionNumber)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.SupersededSnapshotId)
            .IsRequired(false);

        builder.Property(x => x.RectificationReason)
            .HasMaxLength(1000)
            .IsRequired(false);

        // Unique index cho từng revision (tên gọn <= 63 ký tự tương thích PostgreSQL)
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.PeriodType, x.Year, x.PeriodNumber, x.RevisionNumber })
            .HasDatabaseName("IX_HouseholdTaxSnapshots_Tenant_Period_Revision")
            .IsUnique();

        // Unique index cho active snapshot duy nhất đang khóa kỳ (Status = Finalized = 1)
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.PeriodType, x.Year, x.PeriodNumber })
            .HasDatabaseName("IX_HouseholdTaxSnapshots_Tenant_Period_Active")
            .IsUnique()
            .HasFilter("\"Status\" = 1");
    }
}

