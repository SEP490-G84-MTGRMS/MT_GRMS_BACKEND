using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class LoyaltySettingConfiguration : IEntityTypeConfiguration<LoyaltySetting>
{
    public void Configure(EntityTypeBuilder<LoyaltySetting> builder)
    {
        builder.ToTable("LoyaltySettings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("LoyaltySettingID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsEnabled).HasDefaultValue(true);
        builder.Property(x => x.EarningRateAmount).HasColumnType("decimal(18,2)").HasDefaultValue(10000.00m);
        builder.Property(x => x.EarningPoints).HasDefaultValue(1);
        builder.Property(x => x.RedemptionRatePoints).HasDefaultValue(1);
        builder.Property(x => x.RedemptionRateAmount).HasColumnType("decimal(18,2)").HasDefaultValue(1000.00m);
        builder.Property(x => x.MinRedemptionPoints).HasDefaultValue(20);
        builder.Property(x => x.AllowPointsWithPromotions).HasDefaultValue(true);

        builder.HasIndex(x => x.TenantId).IsUnique();
    }
}

