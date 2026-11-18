using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.SaaS;

public class PlatformSettingConfiguration : IEntityTypeConfiguration<PlatformSetting>
{
    public void Configure(EntityTypeBuilder<PlatformSetting> builder)
    {
        builder.ToTable("PlatformSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TrialDays).HasDefaultValue(3);
        builder.Property(x => x.GracePeriodDays).HasDefaultValue(7);
        builder.Property(x => x.BranchAddonPriceMonthly).HasColumnType("decimal(18,2)").HasDefaultValue(80000m);
        builder.Property(x => x.StorageAddonPricePerGbMonthly).HasColumnType("decimal(18,2)").HasDefaultValue(15000m);

        builder.Property(x => x.MasterBankCode).HasMaxLength(50).IsRequired().HasDefaultValue("MBBank");
        builder.Property(x => x.MasterBankName).HasMaxLength(200).IsRequired().HasDefaultValue("MB Bank (Ngân hàng Quân Đội)");
        builder.Property(x => x.MasterAccountNumber).HasMaxLength(50).IsRequired().HasDefaultValue("190065228888");
        builder.Property(x => x.MasterAccountName).HasMaxLength(200).IsRequired().HasDefaultValue("CONG TY TNHH PHAN MEM MT-GRMS");

        builder.Property(x => x.SupportHotline).HasMaxLength(50).IsRequired().HasDefaultValue("1900 6522");
        builder.Property(x => x.SupportEmail).HasMaxLength(100).IsRequired().HasDefaultValue("support@thviet.vn");
        builder.Property(x => x.SupportZalo).HasMaxLength(100).IsRequired().HasDefaultValue("0912345678");

        builder.Property(x => x.IsMaintenanceMode).HasDefaultValue(false);
        builder.Property(x => x.MaintenanceMessage).HasMaxLength(500);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
    }
}
