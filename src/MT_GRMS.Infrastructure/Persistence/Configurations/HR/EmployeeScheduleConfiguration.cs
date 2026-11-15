using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class EmployeeScheduleConfiguration : IEntityTypeConfiguration<EmployeeSchedule>
{
    public void Configure(EntityTypeBuilder<EmployeeSchedule> builder)
    {
        builder.ToTable("EmployeeSchedules");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ScheduleID");

        builder.Property(x => x.EmployeeId).HasColumnName("EmployeeID").IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();
        builder.Property(x => x.ShiftConfigId).HasColumnName("ShiftConfigID");
        builder.Property(x => x.PayslipId).HasColumnName("PayslipID");
        builder.Property(x => x.ShiftCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ShiftName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DurationHours).HasColumnType("decimal(4,2)").HasDefaultValue(4.00m);
        builder.Property(x => x.CheckInMethod).HasMaxLength(30);
        builder.Property(x => x.CheckInFaceImageUrl).HasMaxLength(255);
        builder.Property(x => x.FaceMatchScore).HasColumnType("decimal(5,2)");
        builder.Property(x => x.CheckOutMethod).HasMaxLength(30);
        builder.Property(x => x.CheckOutFaceImageUrl).HasMaxLength(255);
        builder.Property(x => x.LateMinutes).HasDefaultValue(0);
        builder.Property(x => x.EarlyLeaveMinutes).HasDefaultValue(0);
        builder.Property(x => x.OvertimeMinutes).HasDefaultValue(0);
        builder.Property(x => x.ActualWorkHours).HasColumnType("decimal(4,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Status)
            .HasConversion(
                v => v.ToString().ToLower(),
                v => Enum.Parse<ScheduleStatus>(v, true))
            .HasMaxLength(30)
            .HasDefaultValue(ScheduleStatus.Draft);
        builder.Property(x => x.ApprovalType).HasMaxLength(30);
        builder.Property(x => x.ApprovalStatus)
            .HasConversion(
                v => v.ToString().ToUpper(),
                v => Enum.Parse<ScheduleApprovalStatus>(v, true))
            .HasMaxLength(30)
            .HasDefaultValue(ScheduleApprovalStatus.None);
        builder.Property(x => x.ApprovalReason).HasMaxLength(255);
        builder.Property(x => x.ApprovedBy).HasMaxLength(100);

        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.WorkDate });
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate });

        builder.HasOne(x => x.Employee)
            .WithMany(e => e.Schedules)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ShiftConfig)
            .WithMany()
            .HasForeignKey(x => x.ShiftConfigId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Payslip)
            .WithMany(p => p.Schedules)
            .HasForeignKey(x => x.PayslipId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
