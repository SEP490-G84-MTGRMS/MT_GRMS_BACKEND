using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class ShiftConfigConfiguration : IEntityTypeConfiguration<ShiftConfig>
{
    public void Configure(EntityTypeBuilder<ShiftConfig> builder)
    {
        builder.ToTable("ShiftConfigs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ShiftConfigID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();

        builder.Property(x => x.ShiftCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ShiftName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DurationHours).HasColumnType("decimal(4,2)").HasDefaultValue(8.00m);
        builder.Property(x => x.Description).HasMaxLength(255);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Index đảm bảo trong 1 chi nhánh không trùng mã ca
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.ShiftCode })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Quan hệ với Branch
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

