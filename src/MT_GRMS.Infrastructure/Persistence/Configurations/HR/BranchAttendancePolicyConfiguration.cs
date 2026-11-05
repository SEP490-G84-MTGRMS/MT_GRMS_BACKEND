using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class BranchAttendancePolicyConfiguration : IEntityTypeConfiguration<BranchAttendancePolicy>
{
    public void Configure(EntityTypeBuilder<BranchAttendancePolicy> builder)
    {
        builder.ToTable("BranchAttendancePolicies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("BranchAttendancePolicyID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();

        // 1. Số giờ & Quy đổi công
        builder.Property(x => x.StandardDayHours).HasColumnType("decimal(4,2)").HasDefaultValue(8.00m);
        builder.Property(x => x.IsConvertHoursToDays).HasDefaultValue(true);
        builder.Property(x => x.ConversionRulesJson).HasColumnType("text");

        // 2. Đi muộn - Về sớm
        builder.Property(x => x.EnableLateThreshold).HasDefaultValue(true);
        builder.Property(x => x.LateThresholdMinutes).HasDefaultValue(0);
        builder.Property(x => x.EnableEarlyLeaveThreshold).HasDefaultValue(true);
        builder.Property(x => x.EarlyLeaveThresholdMinutes).HasDefaultValue(0);

        // 3. Làm thêm giờ (OT)
        builder.Property(x => x.EnableOtBeforeShift).HasDefaultValue(true);
        builder.Property(x => x.OtBeforeShiftMinutes).HasDefaultValue(0);
        builder.Property(x => x.EnableOtAfterShift).HasDefaultValue(true);
        builder.Property(x => x.OtAfterShiftMinutes).HasDefaultValue(0);

        // Index đảm bảo mỗi Chi nhánh chỉ có 1 chính sách chấm công duy nhất
        builder.HasIndex(x => new { x.TenantId, x.BranchId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Quan hệ với Branch
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

