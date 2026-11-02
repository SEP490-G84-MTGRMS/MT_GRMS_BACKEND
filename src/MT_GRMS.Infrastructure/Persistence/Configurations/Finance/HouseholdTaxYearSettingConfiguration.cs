using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class HouseholdTaxYearSettingConfiguration : IEntityTypeConfiguration<HouseholdTaxYearSetting>
{
    public void Configure(EntityTypeBuilder<HouseholdTaxYearSetting> builder)
    {
        builder.ToTable("HouseholdTaxYearSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Year)
            .IsRequired();

        builder.Property(x => x.PitMethod)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.LockedUntilYear)
            .IsRequired();

        builder.Property(x => x.LockReason)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LockedBySystem)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LockedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Year })
            .IsUnique();
    }
}

